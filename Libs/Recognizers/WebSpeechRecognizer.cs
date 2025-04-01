using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NAudio.CoreAudioApi;
using NextVoiceSync.Libs.Server;
using static Google.Protobuf.Reflection.SourceCodeInfo.Types;

namespace NextVoiceSync.Libs.Recognizers
{
    /// <summary>
    /// Web Speech API を使用した音声認識クラス。
    /// WebView2 を利用して JavaScript の音声認識を実行する。
    /// </summary>
    public class WebSpeechRecognizer : IRecognizer
    {
        /// <summary>
        /// WebView2のユーザーデータを保存するフォルダのパス。
        /// </summary>
        private string userDataFolder;

        /// <summary>
        /// WebView2 コントロールのインスタンス。
        /// </summary>
        private readonly WebView2 webView;

        /// <summary>
        /// 音声認識の結果が確定した際のイベント。
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
        /// HTTPサーバ
        /// </summary>
        private SimpleHttpServer server;

        /// <summary>
        /// ローカルサーバ使用有無
        /// </summary>
        private bool useLocalServer;

        /// <summary>
        /// サーバURL
        /// </summary>
        private string serverUrl;

        /// <summary>
        /// WebSpeechRecognizerのコンストラクタ。
        /// WebView2を初期化し、設定を行う。
        /// </summary>
        public WebSpeechRecognizer(WebView2 webView, IConfiguration configuration)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            this.userDataFolder = configuration.GetValue<string>("WebView2:UserDataFolder", string.Empty);
            this.useLocalServer = configuration.GetValue<bool>("UseLocalServer", true);
            this.serverUrl = configuration.GetValue<string>("WebServer:ServerUrl", "http://localhost:3800");

            InitializeWebView();
        }

        /// <summary>
        /// WebView2 のユーザーデータフォルダのパスを取得する。
        /// </summary>
        private string GetUserDataFolder()
        {
            if (string.IsNullOrWhiteSpace(userDataFolder))
            {
                // デフォルトのフォルダを使用
                userDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Data");
            }

            return userDataFolder;
        }

        /// <summary>
        /// htmlファイルを取得
        /// </summary>
        private string GetHtmlFilePath()
        {
            string appFolder = AppDomain.CurrentDomain.BaseDirectory;
            string htmlFilePath = Path.Combine(appFolder, "Resources", "index.html");

            if (!File.Exists(htmlFilePath))
            {
                AppendLog($"ERROR: HTML ファイルが見つかりません: {htmlFilePath}");
            }

            return htmlFilePath;
        }

        /// <summary>
        /// 簡易WEBサーバを起動
        /// </summary>
        private void StartLocalHttpServer(string htmlFilePath)
        {
            server = new SimpleHttpServer(htmlFilePath, serverUrl);
            server.Start();
        }

        /// <summary>
        /// WebView2 を初期化し、音声認識スクリプトを設定する。
        /// </summary>
        private async void InitializeWebView()
        {
            if (useLocalServer)
            {
                string htmlFilePath = GetHtmlFilePath();
                StartLocalHttpServer(htmlFilePath);
            }

            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: GetUserDataFolder(),
                options: new CoreWebView2EnvironmentOptions(
                    $"--disable-web-security --use-fake-ui-for-media-stream --unsafely-treat-insecure-origin-as-secure={serverUrl}"
                )
            );

            await webView.EnsureCoreWebView2Async(env);

            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

                webView.CoreWebView2.PermissionRequested += (sender, args) =>
                {
                    if (args.PermissionKind == CoreWebView2PermissionKind.Microphone)
                    {
                        AppendLog("マイクの権限を許可しました");
                        args.State = CoreWebView2PermissionState.Allow;
                    }
                };

                webView.CoreWebView2.WebMessageReceived += WebMessageReceived;
                webView.CoreWebView2.Navigate(serverUrl);
            }
            else
            {
                AppendLog("ERROR: WebView2 の初期化に失敗しました。");
            }
        }

        /// <summary>
        /// WebView2からのメッセージを処理し認識結果を取得する。
        /// </summary>
        private void WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string json = e.WebMessageAsJson;

            try
            {
                if (json.StartsWith("\"") && json.EndsWith("\""))
                {
                    json = JsonSerializer.Deserialize<string>(json);
                }

                var message = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (message != null && message.ContainsKey("type") && message.ContainsKey("data"))
                {
                    string type = message["type"];
                    string text = message["data"];

                    if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(text))
                    {
                        if (type == "partial")
                        {
                            OnPartialRecognized?.Invoke(text);
                        }
                        else if (type == "result")
                        {
                            OnTextRecognized?.Invoke(text);
                            webView.CoreWebView2.ExecuteScriptAsync("onWebMessageProcessed('result');");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("ERROR: JSON パースに失敗: " + ex.Message);
                AppendLog("JSON 内容: " + json);
            }
        }

        /// <summary>
        /// 音声認識のキャプチャ開始。
        /// </summary>
        public async Task StartCaptureAsync()
        {
            if (webView.CoreWebView2 != null)
            {
                if (SelectedMicrophone != null)
                {
                    string deviceId = SelectedMicrophone.ID;
                    webView.CoreWebView2.ExecuteScriptAsync($"startRecognition('{deviceId}')");
                }
                else
                {
                    webView.CoreWebView2.ExecuteScriptAsync("startRecognition(null)");
                }
            }
            else
            {
                AppendLog("ERROR: StartCapture() - CoreWebView2 が null です！");
            }
        }

        /// <summary>
        /// 音声認識のキャプチャ停止。
        /// </summary>
        public async Task StopCaptureAsync()
        {
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.ExecuteScriptAsync("stopRecognition()");
            }
            else
            {
                AppendLog("ERROR: StopCapture() - CoreWebView2 が null です！");
            }
        }

        /// <summary>
        /// リソースを解放するDisposeメソッド。
        /// </summary>
        public void Dispose()
        {
            if (webView?.CoreWebView2 != null)
            {
                webView.CoreWebView2.Stop();
                webView.Dispose();
            }
        }
    }
}
