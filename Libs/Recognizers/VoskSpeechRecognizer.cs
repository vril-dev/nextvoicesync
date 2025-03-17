using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using Vosk;
using Microsoft.Extensions.Configuration;

namespace NextVoiceSync.Libs.Recognizers
{
    /// <summary>
    /// Vosk を使用した音声認識を担当するクラス。
    /// 音声をキャプチャし、リアルタイムでテキスト化する。
    /// </summary>
    public class VoskSpeechRecognizer : IRecognizer
    {
        /// <summary>
        /// Voskモデルを配置するパス
        /// </summary>
        private readonly string modelPath;

        /// <summary>
        /// スレッド競合を防ぐためのロックオブジェクト
        /// </summary>
        private readonly object captureLock = new object();

        /// <summary>
        /// WASAPI を使用した音声キャプチャインスタンス。
        /// </summary>
        private WasapiCapture capture;

        /// <summary>
        /// Vosk 音声認識エンジンのインスタンス。
        /// </summary>
        private VoskRecognizer recognizer;

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
        public CaptureSource InputSource { get; set; } = CaptureSource.Loopback;

        /// <summary>
        /// 選択されたマイクデバイス。
        /// ループバック時は null になる。
        /// </summary>
        public MMDevice SelectedMicrophone { get; set; }

        /// <summary>
        /// ログ操作
        /// </summary>
        public Action<string> AppendLog { get; set; } = message => { };

        /// <summary>
        /// VoskSpeechRecognizer のコンストラクタ。
        /// Voskモデルをロードし、音声認識を準備する。
        /// </summary>
        public VoskSpeechRecognizer(IConfiguration configuration)
        {
            modelPath = configuration["VoskSettings:ModelPath"];
            Model model = new Model(modelPath);

            targetWaveFormat = new WaveFormat(16000, 16, 1);
            recognizer = new VoskRecognizer(model, targetWaveFormat.SampleRate);
            recognizer.SetMaxAlternatives(0);
            recognizer.SetWords(true);
            isCapturing = false;
        }

        /// <summary>
        /// 音声認識のキャプチャ開始。
        /// </summary>
        public Task StartCaptureAsync()
        {
            if (isCapturing)
            {
                return Task.CompletedTask;
            }

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
            var resampler = new MediaFoundationResampler(rawWaveProvider, targetWaveFormat);
            resampledWaveProvider = resampler;

            capture.DataAvailable += ProcessAudio;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();
            isCapturing = true;

            return Task.CompletedTask;
        }

        /// <summary>
        /// 音声認識のキャプチャ停止。
        /// </summary>
        public Task StopCaptureAsync()
        {
            if (!isCapturing)
            {
                return Task.CompletedTask;
            }

            isCapturing = false;

            if (capture != null)
            {
                capture.DataAvailable -= ProcessAudio;
                capture.RecordingStopped -= OnRecordingStopped;

                Task stopTask = Task.Run(() =>
                {
                    capture.StopRecording();
                });

                if (!stopTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    capture.Dispose();
                    capture = null;
                }
            }

            audioStream?.Dispose();
            audioStream = null;
            resampledWaveProvider = null;

            return Task.CompletedTask;
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
        /// 音声データを処理し、音声レベルや認識結果を更新する。
        /// </summary>
        private void ProcessAudio(object sender, WaveInEventArgs e)
        {
            if (!isCapturing || e.BytesRecorded == 0) return;

            try
            {
                audioStream.Write(e.Buffer, 0, e.BytesRecorded);
                audioStream.Position = 0;

                byte[] resampledBuffer = new byte[e.BytesRecorded];
                int bytesRead = resampledWaveProvider.Read(resampledBuffer, 0, resampledBuffer.Length);

                if (bytesRead == 0) return;

                if (recognizer.AcceptWaveform(resampledBuffer, bytesRead))
                {
                    string result = recognizer.Result();
                    string text = ExtractText(result);

                    if (!string.IsNullOrEmpty(text))
                    {
                        OnTextRecognized?.Invoke(text);
                    }
                } else {
                    string partial = recognizer.PartialResult();
                    string text = ExtractPartialText(partial);

                    if (!string.IsNullOrEmpty(text))
                    {
                        OnPartialRecognized?.Invoke(text);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"音声処理エラー: {ex.Message}");
            }
            finally
            {
                if (audioStream != null && audioStream.CanWrite)
                {
                    audioStream.SetLength(0);
                }
            }
        }

        /// <summary>
        /// 音声認識結果から確定したテキストを抽出する。
        /// </summary>
        private string ExtractText(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("text", out JsonElement textElement))
                {
                    string text = textElement.GetString();

                    return text;
                }

                return null;
            }
            catch (Exception ex)
            {
                AppendLog($"テキスト抽出エラー: {ex.Message}");

                return null;
            }
        }

        /// <summary>
        /// 音声認識の途中結果を抽出する。
        /// </summary>
        private string ExtractPartialText(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("partial", out JsonElement partialElement))
                {
                    string text = partialElement.GetString();

                    return text;
                }
                return null;
            }
            catch (Exception ex)
            {
                AppendLog($"PartialResult抽出エラー: {ex.Message}");
                return null;
            }
        }
    }
}