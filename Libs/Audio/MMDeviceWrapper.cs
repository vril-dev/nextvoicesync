using NAudio.CoreAudioApi;

namespace NextVoiceSync.Libs.Audio
{
    /// <summary>
    /// マイクデバイスをUIで選択するためのラッパークラス。
    /// </summary>
    class MMDeviceWrapper
    {
        /// <summary>
        /// 実際のMMDeviceオブジェクト。ループバック入力の場合は null。
        /// </summary>
        public MMDevice Device { get; }

        /// <summary>
        /// UIに表示されるデバイス名。
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// MMDeviceWrapper のコンストラクタ。
        /// </summary>
        public MMDeviceWrapper(MMDevice device, string name)
        {
            Device = device;
            DisplayName = name;
        }

        /// <summary>
        /// オーバーライドされた ToString() メソッド。
        /// UI に表示するデバイス名を返す。
        /// </summary>
        public override string ToString()
        {
            return DisplayName;
        }
    }
}
