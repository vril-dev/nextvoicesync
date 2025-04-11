using Microsoft.Extensions.Configuration;
using NextVoiceSync.Libs.Ai;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace NextVoiceSync.Libs.PostAnalysis
{
    /// <summary>
    /// whisper-cli.exe を使用して録音済み音声ファイルを解析する非同期サービス。
    /// PostAnalysis 処理の一部として利用される。
    /// </summary>
    class WhisperService : IPostAnalysisService
    {
        /// <summary>
        /// アプリケーションの設定情報を保持する構成オブジェクト。
        /// </summary>
        private readonly IConfiguration configuration;

        /// <summary>
        /// ログ出力用のデリゲート。
        /// </summary>
        public Action<string> AppendLog { get; set; } = message => { };

        /// <summary>
        /// WhisperService を初期化し、PostAnalysis:Whisper セクションの設定を取得する。
        /// </summary>
        public WhisperService(IConfiguration configuration)
        {
            this.configuration = configuration.GetSection("PostAnalysis:Whisper");
        }

        /// <summary>
        /// 指定された WAV ファイルを whisper-cli で解析し、テキスト出力を取得する。
        /// </summary>
        public async Task<string> AnalyzeAsync(string wavPath)
        {
            if (!File.Exists(wavPath))
            {
                throw new FileNotFoundException(wavPath);
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = configuration["ExecutablePath"],
                    Arguments = $"-m \"{configuration["ModelPath"]}\" -f \"{wavPath}\" --language {configuration["Language"]} --output-{configuration["OutputFormat"]}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"whisper-cli がエラーコード {process.ExitCode} で終了しました。");
            }

            return output;
        }
    }
}
