using System;
using System.IO;
using NAudio.Wave;

namespace NextVoiceSync.Libs.Audio
{
    /// <summary>
    /// 音声の録音処理を担当するクラス。
    /// </summary>
    class Recorder : IDisposable
    {
        /// <summary>
        /// 音声入力を取得するためのオブジェクト。
        /// マイクまたはループバック音声をキャプチャする。
        /// </summary>
        private WaveInEvent waveIn;

        /// <summary>
        /// 録音データをWAVファイルとして書き込むためのオブジェクト。
        /// </summary>
        private WaveFileWriter waveWriter;

        /// <summary>
        /// 録音ファイルの保存先ディレクトリのパス。
        /// </summary>
        private readonly string savePath;

        /// <summary>
        /// Recorderクラスのコンストラクタ。
        /// </summary>
        public Recorder(string savePath)
        {
            this.savePath = EnsureDirectoryExists(savePath);
        }

        /// <summary>
        /// 録音が停止した際に呼び出されるイベントハンドラ。
        /// 必要なリソースを解放し、エラーが発生した場合はログを出力する。
        /// </summary>
        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            waveIn?.Dispose();
            waveIn = null;

            waveWriter?.Dispose();
            waveWriter = null;

            if (e.Exception != null)
            {
                //Console.WriteLine($"録音中にエラーが発生しました: {e.Exception.Message}");
            }
        }

        /// <summary>
        /// 録音を開始する。
        /// </summary>
        public void Start()
        {
            var filePath = GenerateFilePath();

            waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(44100, 16, 1)
            };

            waveWriter = new WaveFileWriter(filePath, waveIn.WaveFormat);

            waveIn.DataAvailable += OnDataAvailable;
            waveIn.RecordingStopped += OnRecordingStopped;

            waveIn.StartRecording();
        }

        /// <summary>
        /// 録音を停止する。
        /// </summary>
        public void Stop()
        {
            waveIn?.StopRecording();
        }

        /// <summary>
        /// リソースの破棄。
        /// </summary>
        public void Dispose()
        {
            waveIn?.Dispose();
            waveWriter?.Dispose();
        }

        /// <summary>
        /// ディレクトリ存在を保証する。
        /// </summary>
        private string EnsureDirectoryExists(string path)
        {
            var fullPath = Path.IsPathRooted(path: path)
                ? savePath
                : Path.Combine(AppContext.BaseDirectory, path);

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            return fullPath;
        }

        /// <summary>
        /// 録音ファイル名を生成する。
        /// </summary>
        private string GenerateFilePath()
        {
            var now = DateTime.Now;
            var fileName = $"{now:yyyyMMdd-HHmmss}.wav";
            return Path.Combine(savePath, fileName);
        }

        /// <summary>
        /// 録音データをWaveファイルに書き込むイベントハンドラ。
        /// </summary>
        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        /// <summary>
        /// 録音停止時にリソースを解放するイベントハンドラ。
        /// </summary>
        private void waveIn_RecordingStopped(object sender, StoppedEventArgs e)
        {
            waveWriter?.Dispose();
            waveWriter = null;

            waveIn?.Dispose();
            waveIn = null;
        }
    }
}
