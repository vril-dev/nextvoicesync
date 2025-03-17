using NAudio.CoreAudioApi;

namespace NextVoiceSync.Libs.Recognizers
{
    /// <summary>
    /// 音声入力ソースの種類を定義する列挙型。
    /// - Loopback: システム音声（ループバック）
    /// - Microphone: マイク入力
    /// </summary>
    public enum CaptureSource
    {
        Loopback,
        Microphone
    }

    /// <summary>
    /// 音声認識の共通インターフェース。
    /// すべての音声認識クラスはこのインターフェースを実装する。
    /// </summary>
    public interface IRecognizer
    {
        /// <summary>
        /// 音声認識が確定した際に発火するイベント。
        /// </summary>
        event Action<string> OnTextRecognized;

        /// <summary>
        /// 一時的な認識結果（未確定のテキスト）が更新された際に発火するイベント。
        /// </summary>
        event Action<string> OnPartialRecognized;

        /// <summary>
        /// 音声入力ソースの種類（ループバックまたはマイク）。
        /// </summary>
        CaptureSource InputSource { get; set; }

        /// <summary>
        /// 選択されたマイクデバイス。
        /// ループバック時は null になる。
        /// </summary>
        MMDevice SelectedMicrophone { get; set; }

        /// <summary>
        /// 音声認識のキャプチャを非同期で開始する。
        /// </summary>
        Task StartCaptureAsync();

        /// <summary>
        /// 音声認識のキャプチャを停止する。
        /// </summary>
        Task StopCaptureAsync();

        /// <summary>
        /// ログ出力用のデリケート
        /// </summary>
        Action<string> AppendLog { get; set; }
    }
}
