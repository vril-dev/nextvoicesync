using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.Tokenizers;
using NAudio.CoreAudioApi;
using NextVoiceSync.Libs.Audio;
using NextVoiceSync.Libs.Validators;
using NextVoiceSync.Libs.Ai;
using NextVoiceSync.Libs.Models;
using NextVoiceSync.Libs.Recognizers;
using System.Windows.Threading;
using System.Windows.Documents;

namespace NextVoiceSync;

/// <summary>
/// WPFアプリケーションのメインウィンドウクラス。
/// 音声認識の開始・停止、ログ管理、UI更新を担当する。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// アプリケーションの設定を管理するための構成オブジェクト。
    /// </summary>
    private IConfiguration configuration;

    /// <summary>
    /// 音声録音を制御する `Recorder` インスタンス。
    /// 録音の開始・停止を管理し、WAV ファイルとして保存する役割を持つ。
    /// </summary>
    private Recorder recorder;

    /// <summary>
    /// 録音機能の有効/無効を管理するフラグ。
    /// 設定 (`appsettings.json`) から読み込まれ、
    /// ユーザーがメニューで変更可能。
    /// </summary>
    private bool isRecordingEnabled;

    /// <summary>
    /// 認識エンジンのタイプを指定する列挙型。
    /// </summary>
    private enum RecognizerType { WebSpeechAPI, Vosk, GoogleSTT }

    /// <summary>
    /// 現在使用している音声認識エンジン。
    /// </summary>
    private RecognizerType currentRecognizer = RecognizerType.Vosk;

    /// <summary>
    /// 現在の音声認識インスタンス。
    /// </summary>
    private IRecognizer recognizer;

    /// <summary>
    /// DIのためのサービスプロバイダー。
    /// </summary>
    private IServiceProvider serviceProvider;

    /// <summary>
    /// Vosk音声認識インスタンス。
    /// </summary>
    private static VoskSpeechRecognizer voskRecognizer;

    /// <summary>
    /// Google Cloud Speech-to-Text音声認識インスタンス。
    /// </summary>
    private static GoogleCloudSpeechRecognizer googleCloudSpeechRecognizer;

    /// <summary>
    /// Web Speech API音声認識インスタンス。
    /// </summary>
    private static WebSpeechRecognizer webSpeechRecognizer;

    /// <summary>
    /// 解析結果のログを記録するかどうか
    /// </summary>
    private bool isTextLoggingEnabled;

    /// <summary>
    /// 解析結果のログファイルパス
    /// </summary>
    private string analysisLogFilePath;

    /// <summary>
    /// AI解析エンジンの管理を行うインスタンス。
    /// </summary>
    private AiAnalyzer aiAnalyzer;

    /// <summary>
    /// 認識されたテキストを保持するリスト。
    /// UI上で更新される。
    /// </summary>
    private ObservableCollection<RecognizedTextItem> recognizedTextItems = new ObservableCollection<RecognizedTextItem>();

    /// <summary>
    /// MainWindow のコンストラクタ。
    /// 初期化処理を実行し、音声認識イベントを設定する。
    /// </summary>
    public MainWindow()
    {
        LoadConfiguration();
        InitializeComponent();
        aiAnalyzer = new AiAnalyzer(configuration);
        aiAnalyzer.AppendLog = AppendLog;
        InitializeRecorder();
        LoadMicrophoneList();
        InitializeRecognizerDropdown();
        LoadPrompts();
        ResultListBox.ItemsSource = recognizedTextItems;
        recognizedTextItems.CollectionChanged += RecognizedTextItems_CollectionChanged;
        InitializeUI();
    }

    /// <summary>
    /// 設定ファイルの読み込み
    /// </summary>
    private void LoadConfiguration()
    {
        configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }

    /// <summary>
    /// 設定をメニュー項目に反映
    /// </summary>
    private void InitializeUI()
    {
        isRecordingEnabled = bool.TryParse(configuration["RecordingSettings:EnableRecording"], out bool result) ? result : false;
        EnableRecordingMenuItem.IsChecked = isRecordingEnabled;

        isTextLoggingEnabled = bool.TryParse(configuration["RecordingSettings:EnableTextLogging"], out bool textLogging) ? textLogging : false;
        EnableTextLoggingMenuItem.IsChecked = isTextLoggingEnabled;
    }

    /// <summary>
    /// 録音機能を初期化し、録音ファイルの保存先を設定する。
    /// 設定ファイル (`appsettings.json`) から保存パスを取得し、
    /// 設定が存在しない場合はデフォルトの "data" フォルダを使用する。
    /// </summary>
    private void InitializeRecorder()
    {
        var savePath = configuration["RecordingSettings:SavePath"];
        if (string.IsNullOrEmpty(savePath))
        {
            savePath = "data";
        }

        recorder = new Recorder(savePath);
    }

    /// <summary>
    /// 音声認識エンジンの種類を選択するドロップダウンを初期化する。
    /// </summary>
    private void InitializeRecognizerDropdown()
    {
        RecognizerComboBox.Items.Add("Web Speech API");
        RecognizerComboBox.Items.Add("Vosk");
        RecognizerComboBox.Items.Add("Google Speech-to-Text");
        RecognizerComboBox.SelectedIndex = 0;
        RecognizerComboBox.SelectionChanged += RecognizerComboBox_SelectionChanged;
    }

    /// <summary>
    /// システムに接続されているマイクデバイスのリストを取得し、ComboBox にセットする。
    /// </summary>
    private void LoadMicrophoneList()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
        var deviceItems = devices.Select(d => new MMDeviceWrapper(d, d.FriendlyName)).ToList();
        var loopbackItem = new MMDeviceWrapper(null, "ループバック入力");

        if (currentRecognizer == RecognizerType.WebSpeechAPI)
        {
            MicDeviceComboBox.ItemsSource = deviceItems;
        }
        else
        {
            MicDeviceComboBox.ItemsSource = new[] { loopbackItem }.Concat(deviceItems);
        }

        MicDeviceComboBox.SelectedIndex = 0;
    }

    /// <summary>
    /// appsettings.json からプロンプトリストを取得し、コンボボックスに表示する。
    /// </summary>
    private void LoadPrompts()
    {
        var prompts = configuration.GetSection("PromptSettings:Prompts")
            .GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);

        if (prompts != null && prompts.Count > 0)
        {
            PromptComboBox.ItemsSource = prompts.Keys;
            string defaultPrompt = configuration["PromptSettings:DefaultPrompt"];

            if (prompts.ContainsKey(defaultPrompt))
            {
                PromptComboBox.SelectedItem = defaultPrompt;
            }
            else
            {
                PromptComboBox.SelectedIndex = 0;
            }
        }
    }

    /// <summary>
    /// 音声認識エンジンが変更されたときのイベントハンドラ。
    /// </summary>
    private void RecognizerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int previousComboBoxIndex = e.RemovedItems.Count > 0 ? RecognizerComboBox.Items.IndexOf(e.RemovedItems[0]) : RecognizerComboBox.SelectedIndex;
        RecognizerType previousRecognizer = currentRecognizer;

        bool hasError = false;
        switch (RecognizerComboBox.SelectedIndex)
        {
            case 0:
                currentRecognizer = RecognizerType.WebSpeechAPI;
                break;
            case 1:
                if (!RecognizerValidator.CheckVoskModel(configuration))
                {
                    MessageBox.Show("Voskの音声認識モデルが設定されていません。\nappsettings.json設定ファイルを確認してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    hasError = true;
                    break;
                }

                currentRecognizer = RecognizerType.Vosk;
                break;
            case 2:
                if (!RecognizerValidator.CheckGoogleApiKey(configuration))
                {
                    MessageBox.Show("Google Speech-to-TextのAPI キーが見つかりません。\nappsettings.json設定を確認してください。",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    hasError = true;
                    break;
                }

                currentRecognizer = RecognizerType.GoogleSTT;
                break;
        }

        if (hasError)
        {
            currentRecognizer = previousRecognizer;

            Dispatcher.Invoke(() =>
            {
                RecognizerComboBox.SelectedIndex = -1;
                RecognizerComboBox.SelectedIndex = previousComboBoxIndex;
            });

            return;
        }

        LoadMicrophoneList();
        ToggleRecognizer();
    }

    /// <summary>
    /// 現在の音声認識エンジンを設定する。
    /// </summary>
    private void ToggleRecognizer()
    {
        if (recognizer != null)
        {
            recognizer.OnTextRecognized -= UpdateRecognizedText;
            recognizer.OnPartialRecognized -= UpdatePartialText;
        }

        if (recognizer is IDisposable disposableRecognizer)
        {
            disposableRecognizer.Dispose();
        }

        if (serviceProvider == null)
        {
            serviceProvider = new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton<VoskSpeechRecognizer>()
                .AddSingleton<GoogleCloudSpeechRecognizer>()
                .BuildServiceProvider();
        }

        switch (currentRecognizer)
        {
            case RecognizerType.WebSpeechAPI:
                if (webSpeechRecognizer == null)
                {
                    webSpeechRecognizer = new WebSpeechRecognizer(SpeechRecognitionWebView);
                }

                recognizer = webSpeechRecognizer;
                break;
            case RecognizerType.GoogleSTT:
                if (googleCloudSpeechRecognizer == null)
                {
                    googleCloudSpeechRecognizer = serviceProvider.GetRequiredService<GoogleCloudSpeechRecognizer>();
                }

                recognizer = googleCloudSpeechRecognizer;
                break;
            default:
                if (voskRecognizer == null)
                {
                    voskRecognizer = serviceProvider.GetRequiredService<VoskSpeechRecognizer>();
                }

                recognizer = voskRecognizer;
                break;
        }

        recognizer.AppendLog = AppendLog;
        recognizer.OnTextRecognized += UpdateRecognizedText;
        recognizer.OnPartialRecognized += UpdatePartialText;
    }

    /// <summary>
    /// `ResultListBox` に変更があったら `AnalyzeButton` を活性化・非活性化
    /// </summary>
    private void RecognizedTextItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        AnalyzeButton.IsEnabled = recognizedTextItems.Count > 0;
    }

    /// <summary>
    /// メニューのチェック状態変更時に EnableTextLogging を更新
    /// </summary>
    private void EnableTextLoggingMenuItem_Checked(object sender, RoutedEventArgs e)
    {
        isTextLoggingEnabled = EnableTextLoggingMenuItem.IsChecked == true;

        UpdateAppSettings("RecordingSettings:EnableTextLogging", isTextLoggingEnabled);
    }

    /// <summary>
    /// メニューの「録音を有効にする」項目のチェック状態が変更されたときに呼び出されるイベントハンドラ。
    /// チェック状態を `isRecordingEnabled` に反映し、
    /// `appsettings.json` に新しい値を保存する。
    /// </summary>
    private void EnableRecordingMenuItem_Checked(object sender, RoutedEventArgs e)
    {
        isRecordingEnabled = EnableRecordingMenuItem.IsChecked == true;

        UpdateAppSettings("RecordingSettings:EnableRecording", isRecordingEnabled);
    }

    /// <summary>
    /// `appsettings.json` の特定の設定値を更新する。
    /// アプリの実行時に変更された設定を永続化するために使用する。
    /// </summary>
    private void UpdateAppSettings(string key, object value)
    {
        string configPath = "appsettings.json";

        if (!File.Exists(configPath))
        {
            MessageBox.Show("appsettings.json設定ファイルを確認してください。",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            string json = File.ReadAllText(configPath);
            var jsonObj = Newtonsoft.Json.Linq.JObject.Parse(json);

            string[] keys = key.Split(':');
            Newtonsoft.Json.Linq.JObject section = jsonObj;
            for (int i = 0; i < keys.Length - 1; i++)
            {
                section = (Newtonsoft.Json.Linq.JObject)section[keys[i]];
            }
            section[keys[^1]] = Newtonsoft.Json.Linq.JToken.FromObject(value);

            string updatedJson = jsonObj.ToString(Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(configPath, updatedJson);

            LoadConfiguration();
            InitializeUI();
        }
        catch (Exception ex)
        {
            AppendLog($"設定の保存に失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析開始/停止ボタンのクリックイベント。
    /// 音声キャプチャの開始または停止を制御する。
    /// </summary>
    private async void ToggleCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        bool isToggled = ToggleCaptureButton.IsChecked ?? false;

        if (isToggled)
        {
            AppendLog("音声キャプチャ開始");
            ToggleCaptureButton.Content = "解析停止";
            recognizedTextItems.Clear();
            TokenCountLabel.Content = "トークン数: 0";
            PartialTextBlock.Text = "(音声認識中...)";
            AiResultBox.Text = "";

            RecognizerComboBox.IsEnabled = false;
            MicDeviceComboBox.IsEnabled = false;

            var selectedWrapper = (MMDeviceWrapper)MicDeviceComboBox.SelectedItem;

            if (selectedWrapper.Device == null)
            {
                recognizer.InputSource = CaptureSource.Loopback;
                recognizer.SelectedMicrophone = null;
            }
            else
            {
                recognizer.InputSource = CaptureSource.Microphone;
                recognizer.SelectedMicrophone = selectedWrapper.Device;
            }

            string saveDirectory = configuration["RecordingSettings:SavePath"];
            string analysisFilePath = Path.Combine(saveDirectory, "analysis.txt");

            if (File.Exists(analysisFilePath))
            {
                try
                {
                    File.Delete(analysisFilePath);
                }
                catch (Exception ex)
                {
                    AppendLog($"開始時の analysis.txt 削除エラー: {ex.Message}");
                }
            }

            await recognizer.StartCaptureAsync();

            if (isRecordingEnabled)
            {
                StartRecording();
            }
        }
        else
        {
            AppendLog("音声キャプチャ停止");
            ToggleCaptureButton.Content = "解析開始";
            PartialTextBlock.Text = "---";

            RecognizerComboBox.IsEnabled = true;
            MicDeviceComboBox.IsEnabled = true;

            await recognizer.StopCaptureAsync();

            if (isRecordingEnabled)
            {
                StopRecording();
            }

            if (isTextLoggingEnabled)
            {
                string saveDirectory = configuration["RecordingSettings:SavePath"];
                string analysisFilePath = Path.Combine(saveDirectory, "analysis.txt");
                string formattedFilePath = Path.Combine(saveDirectory, $"analysis_{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                try
                {
                    if (File.Exists(analysisFilePath))
                    {
                        string content = File.ReadAllText(analysisFilePath);

                        using (StreamWriter sw = new StreamWriter(formattedFilePath, false, Encoding.UTF8))
                        {
                            sw.WriteLine(content);
                            AppendLog($"{formattedFilePath} に保存しました");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"解析ログフォーマットエラー: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// AI解析
    /// </summary>
    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string selectedPromptKey = PromptComboBox.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(selectedPromptKey))
            {
                MessageBox.Show("プロンプトが選択されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var prompts = configuration.GetSection("PromptSettings:Prompts").Get<Dictionary<string, string>>();
            if (prompts == null || !prompts.ContainsKey(selectedPromptKey))
            {
                MessageBox.Show($"エラー: プロンプト '{selectedPromptKey}' が設定にありません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedPromptContent = prompts[selectedPromptKey];
            var dialog = new PromptEditDialog(selectedPromptContent);
            bool? result = dialog.ShowDialog();

            if (result != true)
            {
                return;
            }

            string editedPrompt = dialog.EditedPrompt;

            string inputText = string.Join("\n", recognizedTextItems.Select(item => item.Message));

            if (string.IsNullOrWhiteSpace(inputText))
            {
                MessageBox.Show("解析するテキストがありません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string response = await aiAnalyzer.AnalyzeTextAsync(inputText, editedPrompt);
            AiResultBox.Text = response;

            foreach (TabItem tab in MainTabControl.Items)
            {
                if ((string)tab.Header == "AI解析結果")
                {
                    tab.IsSelected = true;
                    break;
                }
            }

        }
        catch (Exception ex)
        {
            MessageBox.Show($"エラー: {ex.Message}", "解析エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 認識された最終的なテキストをUIに表示する。
    /// </summary>
    private void UpdateRecognizedText(string text)
    {
        Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                PartialTextBlock.Text = "";
                recognizedTextItems.Add(new RecognizedTextItem
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Message = text
                });
                ResultListBox.ScrollIntoView(recognizedTextItems.Last());
                UpdateTokenCount();

                string analysisFilePath = Path.Combine(configuration["RecordingSettings:SavePath"], "analysis.txt");
                try
                {
                    using (StreamWriter sw = new StreamWriter(analysisFilePath, true, Encoding.UTF8))
                    {
                        sw.WriteLine($"{DateTime.Now:HH:mm:ss} {text}");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"解析ログ書き込みエラー: {ex.Message}");
                }
            }
        });
    }

    /// <summary>
    /// 部分的な認識結果をUIに表示する。
    /// </summary>
    private void UpdatePartialText(string text)
    {
        Dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                PartialTextBlock.Text = text;
            }
        });
    }

    /// <summary>
    /// 録音を開始
    /// </summary>
    private async void StartRecording()
    {
        recorder.Start();
        await recognizer.StartCaptureAsync();

        if (isTextLoggingEnabled)
        {
            string saveDirectory = configuration["RecordingSettings:SavePath"] ?? "data";
            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            analysisLogFilePath = Path.Combine(saveDirectory, $"{timestamp}.txt");

            using (StreamWriter sw = new StreamWriter(analysisLogFilePath, false, Encoding.UTF8))
            {
                sw.WriteLine($"解析ログ開始: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
        }
    }

    /// <summary>
    /// 音声認識停止時に録音停止
    /// </summary>
    private async void StopRecording()
    {
        recorder.Stop();
    }

    /// <summary>
    /// トークン数を更新し、UIに表示する
    /// </summary>
    private void UpdateTokenCount()
    {
        // Microsoft.ML.Tokenizers 0.22.0-preview.24179.1
        var tokenizer = Tokenizer.CreateTiktokenForModel("gpt-4-o");
        string text = string.Join("\n", recognizedTextItems.Select(item => $"{item.Timestamp} {item.Message}"));
        int count = tokenizer.CountTokens(text);

        TokenCountLabel.Content = $"トークン数: {count}";
    }

    /// <summary>
    /// LogBox にテキストを追記し、最終行に自動スクロールする
    /// </summary>
    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogBox.AppendText($"{timestamp} {message}\n");
            LogBox.ScrollToEnd();
        });
    }
}
