using System.IO;
using Google.Cloud.Speech.V1;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Microsoft.Extensions.Configuration;

namespace NextVoiceSync.Libs.Recognizers
{
    /// <summary>
    /// Google Cloud Speech-to-Text を使用した音声認識クラス。
    /// 音声をキャプチャしてリアルタイムでテキスト化する。
    /// </summary>
    public class GoogleCloudSpeechRecognizer : IRecognizer
    {
        /// <summary>
        /// Google Cloud Speech-to-Text API クライアント。
        /// </summary>
        private SpeechClient speechClient;

        /// <summary>
        /// APIキーのファイルパス。
        /// </summary>
        private readonly string apiKeyPath;

        /// <summary>
        /// スレッド競合を防ぐためのロックオブジェクト
        /// </summary>
        private readonly object captureLock = new object();

        /// <summary>
        /// WASAPI を使用した音声キャプチャインスタンス。
        /// </summary>
        private WasapiCapture capture;

        /// <summary>
        /// キャプチャが進行中かどうかを示すフラグ（スレッドセーフ）。
        /// </summary>
        private volatile bool isCapturing;

        /// <summary>
        /// 音声データを処理する際のターゲットフォーマット。
        /// </summary>
        private WaveFormat targetWaveFormat;

        /// <summary>
        /// 音声データを一時保存するメモリストリーム。
        /// </summary>
        private MemoryStream audioStream;

        /// <summary>
        /// 取得した音声をリサンプリングするためのプロバイダー。
        /// </summary>
        private IWaveProvider resampledWaveProvider;

        /// <summary>
        /// 音声認識の結果が確定した際に発火するイベント。
        /// </summary>
        public event Action<string> OnTextRecognized;

        /// <summary>
        /// 一時的な認識結果（途中のテキスト）が更新された際に発火するイベント。
        /// </summary>
        public event Action<string> OnPartialRecognized;

        /// <summary>
        /// 現在の音声入力ソース（ループバックまたはマイク）。
        /// </summary>
        public CaptureSource InputSource { get; set; } = CaptureSource.Microphone;

        /// <summary>
        /// 音声データのストリーム書き込み時の同期制御に使用するSemaphoreSlim。
        /// </summary>
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 選択されたマイクデバイス。
        /// ループバック時は null になる。
        /// </summary>
        public MMDevice SelectedMicrophone { get; set; }

        /// <summary>
        /// 音声ストリーミングのインスタンス。
        /// </summary>
        private SpeechClient.StreamingRecognizeStream streamingCall;

        /// <summary>
        /// ログ操作
        /// </summary>
        public Action<string> AppendLog { get; set; } = message => { };

        /// <summary>
        /// GoogleCloudSpeechRecognizer のコンストラクタ。
        /// APIキーを読み込み、クライアントを初期化する。
        /// </summary>
        public GoogleCloudSpeechRecognizer(IConfiguration configuration)
        {
            apiKeyPath = configuration["GoogleCloud:ApiKeyPath"];

            if (string.IsNullOrEmpty(apiKeyPath))
            {
                throw new InvalidOperationException("Google Cloud APIキーのパスが設定されていません。");
            }

            if (!Path.IsPathRooted(apiKeyPath))
            {
                apiKeyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, apiKeyPath);
            }

            if (!File.Exists(apiKeyPath))
            {
                throw new FileNotFoundException($"Google Cloud APIキーが見つかりません: {apiKeyPath}");
            }

            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", apiKeyPath);

            speechClient = SpeechClient.Create();
        }

        /// <summary>
        /// 音声キャプチャを開始する。
        /// ループバックまたはマイク入力を選択して録音を開始。
        /// </summary>
        public async Task StartCaptureAsync()
        {
            if (isCapturing) return;

            if (InputSource == CaptureSource.Microphone && SelectedMicrophone != null)
            {
                capture = new WasapiCapture(SelectedMicrophone);
            }
            else
            {
                capture = new WasapiLoopbackCapture();
            }

            audioStream = new MemoryStream();
            var rawWaveProvider = new RawSourceWaveStream(audioStream, capture.WaveFormat);
            if (capture.WaveFormat.SampleRate != 16000 || capture.WaveFormat.BitsPerSample != 16 || capture.WaveFormat.Channels != 1)
            {
                var resampler = new MediaFoundationResampler(rawWaveProvider, new WaveFormat(16000, 16, 1));
                resampledWaveProvider = resampler;
            }
            else
            {
                resampledWaveProvider = rawWaveProvider;
            }

            capture.DataAvailable += ProcessAudio;
            capture.RecordingStopped += OnRecordingStopped;

            await StartStreaming();

            capture.StartRecording();
            isCapturing = true;
        }

        /// <summary>
        /// 音声キャプチャを停止し、リソースを解放する。
        /// </summary>
        public async Task StopCaptureAsync()
        {
            if (!isCapturing) return;

            isCapturing = false;

            if (capture != null)
            {
                capture.DataAvailable -= ProcessAudio;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.StopRecording();
                capture.Dispose();
                capture = null;
            }

            // ストリームのクローズ
            streamingCall?.WriteCompleteAsync().Wait();
            streamingCall?.Dispose();
            streamingCall = null;

            audioStream?.Dispose();
            audioStream = null;
            resampledWaveProvider = null;
        }

        /// <summary>
        /// NAudioの録音停止時に呼ばれるイベントハンドラ
        /// </summary>
        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            lock (captureLock)
            {
                if (capture != null)
                {
                    capture.Dispose();
                    capture = null;
                }
                if (audioStream != null)
                {
                    audioStream.Dispose();
                    audioStream = null;
                }
                resampledWaveProvider = null;
            }
        }

        /// <summary>
        /// 音声ストリーミングを初期化する。
        /// </summary>
        private async Task StartStreaming()
        {
            streamingCall = speechClient.StreamingRecognize();
            await streamingCall.WriteAsync(new StreamingRecognizeRequest
            {
                StreamingConfig = new StreamingRecognitionConfig
                {
                    Config = new RecognitionConfig
                    {
                        Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                        SampleRateHertz = 16000,
                        LanguageCode = "ja-JP"
                    },
                    InterimResults = true
                }
            });

            var responseTask = Task.Run(async () =>
            {
                await foreach (var response in streamingCall.GetResponseStream())
                {
                    foreach (var result in response.Results)
                    {
                        if (result.IsFinal)
                        {
                            OnTextRecognized?.Invoke(result.Alternatives[0].Transcript);
                        }
                        else
                        {
                            OnPartialRecognized?.Invoke(result.Alternatives[0].Transcript);
                        }
                    }
                }
            });

            isCapturing = true;
        }

        /// <summary>
        /// 音声データを処理し、音声レベルや認識結果を更新する。
        /// </summary>
        private async void ProcessAudio(object sender, WaveInEventArgs e)
        {
            if (!isCapturing || e.BytesRecorded == 0) return;

            try
            {
                audioStream.Position = audioStream.Length;
                audioStream.Write(e.Buffer, 0, e.BytesRecorded);
                audioStream.Position = 0;

                byte[] buf16k = new byte[4096];
                int bytesRead;
                while ((bytesRead = resampledWaveProvider.Read(buf16k, 0, buf16k.Length)) > 0)
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await streamingCall.WriteAsync(new StreamingRecognizeRequest
                        {
                            AudioContent = Google.Protobuf.ByteString.CopyFrom(buf16k, 0, bytesRead)
                        });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }

                audioStream.SetLength(0);
                audioStream.Position = 0;
            }
            catch (Exception ex)
            {
                AppendLog($"Google STT エラー: {ex.Message}");
            }
        }
    }
}
