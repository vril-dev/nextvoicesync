using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace NextVoiceSync.Libs.Ai
{
    /// <summary>
    /// AI プロバイダーを管理し、テキスト解析を実行するクラス。
    /// </summary>
    class AiAnalyzer
    {
        /// <summary>
        /// 使用する AI プロバイダーのインスタンス。
        /// </summary>
        private readonly IAiProvider aiProvider;

        /// <summary>
        /// アプリケーションの設定情報を保持する構成オブジェクト。
        /// </summary>
        private readonly IConfiguration configuration;

        /// <summary>
        /// ログ出力用のデリゲート。
        /// `AppendLog` に値をセットすると、`aiProvider` の `AppendLog` も自動的に更新される。
        /// </summary>
        public Action<string> AppendLog
        {
            get => _appendLog;
            set
            {
                _appendLog = value ?? (message => { });
                if (aiProvider != null)
                {
                    aiProvider.AppendLog = _appendLog;
                }
            }
        }

        /// <summary>
        /// ログ出力のデフォルト値（空のラムダ関数）。
        /// </summary>
        private Action<string> _appendLog = message => { };

        /// <summary>
        /// `AiAnalyzer` クラスのコンストラクタ。
        /// 指定された構成情報を使用して AI プロバイダーを初期化する。
        /// </summary>
        public AiAnalyzer(IConfiguration config)
        {
            configuration = config;
            aiProvider = CreateProvider(configuration["AISettings:DefaultProvider"]);
        }

        /// <summary>
        /// 指定されたプロバイダー名に基づいて AI プロバイダーを作成する。
        /// </summary>
        private IAiProvider CreateProvider(string providerName)
        {
            switch (providerName)
            {
                case "OpenAI":
                    return new OpenAiProvider(configuration);
                case "Grok":
                    throw new NotImplementedException("Grok integration is not yet implemented.");
                case "Gemini":
                    throw new NotImplementedException("Gemini integration is not yet implemented.");
                default:
                    throw new ArgumentException($"Unknown AI provider: {providerName}");
            }
        }

        /// <summary>
        /// 指定されたテキストを AI に解析させる。
        /// </summary>
        public async Task<string> AnalyzeTextAsync(string inputText, string editedPrompt)
        {
            if (string.IsNullOrWhiteSpace(inputText) || string.IsNullOrWhiteSpace(editedPrompt))
            {
                AppendLog("入力テキストまたはプロンプトが空です。");
                throw new ArgumentException("入力テキストまたはプロンプトが空です。");
            }

            string fullPrompt = $"{editedPrompt}\n\n{inputText}";

            return await aiProvider.AnalyzeAsync(fullPrompt);
        }
    }
}
