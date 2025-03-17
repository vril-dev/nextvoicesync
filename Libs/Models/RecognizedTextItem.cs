using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NextVoiceSync.Libs.Models
{
    /// <summary>
    /// 音声認識結果のデータモデル
    /// </summary>
    class RecognizedTextItem
    {
        /// <summary>
        /// 音声が認識された時間（HH:mm:ss 形式の文字列）。
        /// </summary>
        public string Timestamp { get; set; }

        /// <summary>
        /// 認識されたテキストメッセージ。
        /// </summary>
        public string Message { get; set; }
    }
}
