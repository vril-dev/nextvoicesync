using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NAudio.CoreAudioApi;
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
        /// WebSpeechRecognizerのコンストラクタ。
        /// WebView2を初期化し、設定を行う。
        /// </summary>
        public WebSpeechRecognizer(WebView2 webView)
        {
            this.webView = webView ?? throw new ArgumentNullException(nameof(webView));
            userDataFolder = GetUserDataFolder();

            InitializeWebView();
        }

        /// <summary>
        /// WebView2 のユーザーデータフォルダのパスを取得する。
        /// </summary>
        private string GetUserDataFolder()
        {
            try
            {
                string appFolder = AppDomain.CurrentDomain.BaseDirectory;
                string configPath = Path.Combine(appFolder, "appsettings.json");

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("WebView2", out JsonElement webViewConfig) &&
                        webViewConfig.TryGetProperty("UserDataFolder", out JsonElement userDataPath))
                    {
                        string path = userDataPath.GetString();

                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            return path;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("ERROR: appsettings.json の読み込みに失敗: " + ex.Message);
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Data");
        }

        /// <summary>
        /// WebView2 を初期化し、音声認識スクリプトを設定する。
        /// </summary>
        private async void InitializeWebView()
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder,
                options: new CoreWebView2EnvironmentOptions("--disable-web-security --use-fake-ui-for-media-stream")
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
            }

            webView.CoreWebView2.NavigateToString(@"
                <html>
                    <script>
                        let recognition;
                        let audioContext;
                        let analyser;
                        let javascriptNode;
                        let finalText = '';
                        let partialText = '';
                        let isFinalProcessing = false;

                        function startRecognition(deviceId) {
                            recognition = new (window.SpeechRecognition || window.webkitSpeechRecognition)();
                            recognition.lang = 'ja-JP';
                            recognition.continuous = true;
                            recognition.interimResults = true;

                            recognition.onresult = event => {
                                let newFinalText = '';
                                let newPartialText = '';

                                for (let i = 0; i < event.results.length; i++) {
                                    if (event.results[i].isFinal) {
                                        newFinalText = event.results[i][0].transcript;
                                    } else {
                                        newPartialText = event.results[i][0].transcript;
                                    }
                                }

                                if (newPartialText.length > 0) {
                                    console.log(`partialText=${newPartialText}`);
                                    window.chrome.webview.postMessage(JSON.stringify({ type: 'partial', data: newPartialText }));
                                }

                                if (newFinalText.length > 0 && newFinalText !== finalText && !isFinalProcessing) {
                                    window.chrome.webview.postMessage(JSON.stringify({ type: 'result', data: newFinalText }));

                                    isFinalProcessing = true;
                                    finalText = newFinalText;
                                }
                            };

                            recognition.onerror = event => {
                                //
                            };

                            recognition.onend = () => {
                                recognition.start();
                            };

                            recognition.start();
                        }

                        function onWebMessageProcessed(type) {
                            if (type === 'result') {
                                console.log(`onWebMessageProcessed ${type}`);
                                isFinalProcessing = false;
                            }
                        }

                        function stopRecognition() {
                            if (recognition) {
                                recognition.onend = null;
                                recognition.stop();
                            }
                            if (audioContext) {
                                audioContext.close();
                            }
                        }
                    </script>
                </html>
            ");
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
