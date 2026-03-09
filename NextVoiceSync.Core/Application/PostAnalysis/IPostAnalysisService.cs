using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NextVoiceSync.Application.PostAnalysis
{
    /// <summary>
    /// 録音済み音声ファイルに対して後処理を行う解析サービスの共通インターフェース。
    /// Whisper、感情解析、話者識別などの後処理に拡張可能。
    /// </summary>
    public interface IPostAnalysisService
    {
        /// <summary>
        /// 指定された WAV ファイルを解析し、テキスト形式の結果を非同期で返す。
        /// </summary>
        Task<string> AnalyzeAsync(string wavFilePath);
    }
}
