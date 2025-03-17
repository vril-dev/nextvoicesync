using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.ML.Tokenizers;

namespace NextVoiceSync.Libs.Ai
{
    /// <summary>
    /// OpenAI API を利用してテキスト解析を行うクラス。
    /// </summary>
    class OpenAiProvider : IAiProvider
    {
        /// <summary>
        /// OpenAI API のキー。
        /// </summary>
        private readonly string apiKey;

        /// <summary>
        /// OpenAI API のエンドポイント URL。
        /// </summary>
        private readonly string endpoint;

        /// <summary>
        /// HTTP クライアントインスタンス。
        /// </summary>
        private readonly HttpClient httpClient;

        /// <summary>
        /// トークンカウント用のトークナイザー。
        /// </summary>
        private readonly Tokenizer tokenizer;

        /// <summary>
        /// ログ出力用のデリゲート。
        /// </summary>
        public Action<string> AppendLog { get; set; } = message => { };

        /// <summary>
        /// コンストラクタ。
        /// 設定情報をもとに OpenAI API への接続情報を初期化する。
        /// </summary>
        public OpenAiProvider(IConfiguration configuration)
        {
            var settings = configuration.GetSection("AISettings:Providers:OpenAI");
            string apiKeyPath = settings["ApiKeyPath"];
            endpoint = settings["Endpoint"];
            httpClient = new HttpClient();
            tokenizer = Tokenizer.CreateTiktokenForModel("gpt-4-o");

            if (File.Exists(apiKeyPath))
            {
                apiKey = File.ReadAllText(apiKeyPath).Trim();
            }
            else
            {
                apiKey = "";
                AppendLog($"API キーのファイルが見つかりません: {apiKeyPath}");
            }
        }

        /// <summary>
        /// 指定されたプロンプトを OpenAI API に送信し、解析結果を取得する。
        /// </summary>
        public async Task<string> AnalyzeAsync(string prompt)
        {
            AppendLog("OpenAI にリクエストを送信中...");
            int inputTokens = tokenizer.CountTokens(prompt);
            int maxAllowedTokens = 4096;

            if (inputTokens >= maxAllowedTokens - 50) // 50トークン以上は出力用に確保
            {
                AppendLog($"エラー: 入力テキストが {inputTokens} トークンで上限 ({maxAllowedTokens}) を超えています。解析を中止します。");
                return "エラー: 入力テキストのトークン数が上限を超えています。解析できません。";
            }

            int maxTokens = Math.Min(500, maxAllowedTokens - inputTokens);
            if (maxTokens < 50)
            {
                AppendLog($"トークン制限のため解析をスキップ: {inputTokens} / {maxAllowedTokens}");
                return "エラー: 十分なトークン数を確保できないため解析できません。";
            }

            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful AI assistant." },
                    new { role = "user", content = prompt }
                },
                max_tokens = maxTokens,
                temperature = 0.7
            };

            var requestJson = JsonSerializer.Serialize(requestBody);
            var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = requestContent;

            try
            {
                var response = await httpClient.SendAsync(request);
                var responseJson = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(responseJson);

                if (!doc.RootElement.TryGetProperty("choices", out JsonElement choicesElement) || choicesElement.GetArrayLength() == 0)
                {
                    AppendLog("エラー: OpenAIのレスポンスに 'choices' が含まれていません。");
                    return "エラー: OpenAIのレスポンスが無効です。";
                }

                var firstChoice = choicesElement[0];
                if (!firstChoice.TryGetProperty("message", out JsonElement messageElement))
                {
                    AppendLog("エラー: OpenAIのレスポンスに 'message' が含まれていません。");
                    return "エラー: OpenAIのレスポンスが無効です。";
                }

                string result = messageElement.GetProperty("content").GetString();

                AppendLog($"OpenAI の応答を受信: {result.Substring(0, Math.Min(result.Length, 50))}...");
                return result;
            }
            catch (Exception ex)
            {
                AppendLog($"OpenAI のリクエストエラー: {ex.Message}");
                throw;
            }
        }
    }
}
