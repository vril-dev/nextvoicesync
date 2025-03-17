using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NextVoiceSync.Libs.Ai
{
    /// <summary>
    /// AI プロバイダーのインターフェース。
    /// すべての AI プロバイダーはこのインターフェースを実装する必要がある。
    /// </summary>
    interface IAiProvider
    {
        /// <summary>
        /// 指定されたプロンプトを AI に送信し、解析結果を非同期で取得する。
        /// </summary>
        /// <param name="prompt">解析対象の入力テキスト</param>
        /// <returns>解析結果のテキスト</returns>
        Task<string> AnalyzeAsync(string prompt);

        /// <summary>
        /// ログ出力用のデリゲート。
        /// `AppendLog` を設定すると、AI プロバイダーのログが外部で処理される。
        /// </summary>
        Action<string> AppendLog { get; set; }
    }
}
