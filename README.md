# NexVoiceSync

リアルタイムでPCの音声（ループバックまたはマイク）をキャプチャし、AIを利用して音声をテキストに変換するWindows向けアプリケーションです。

---

## 📚 概要

NexVoiceSync は、PCの音声（ループバックまたはマイク）をキャプチャし、リアルタイムでテキスト化することを主目的とするアプリケーションです。  
さらに、テキスト化した音声データを AI を活用して解析する機能を備えており、  
会議の要約やキーワード抽出、議事録作成などを効率化できます。

NexVoiceSync は、会議やオンラインミーティングなどの音声をリアルタイムに認識・テキスト化するためのツールです。  
ユーザーは任意のタイミングで音声解析を開始・停止したり、解析内容のプロンプトを自由に設定することができます。

---

## ⚙️ 機能

- **リアルタイム音声キャプチャ**
  - システム音声（ループバック）またはマイク入力をキャプチャ
  - 音声レベルをリアルタイム表示

- **音声認識エンジン対応**
  - Vosk（オフライン音声認識）
  - Google Cloud Speech-to-Text
  - Web Speech API（WebView2経由）

- **AI解析機能**
  - OpenAI API を活用したリアルタイムテキスト解析
  - **Grok や Gemini などの AI プロバイダー対応は今後追加予定**
  - ユーザーが指定したプロンプトに基づく解析（要約、キーワード抽出など）
  - **Microsoft.ML.Tokenizers によるリアルタイムトークンカウント**
  - **入力トークン数を管理し、解析時のエラーハンドリングを実装**

- **ユーザーインターフェース（WPF）**
  - 認識結果をリアルタイムで表示
  - 音声入力ソースおよびマイクデバイス選択可能
  - プロンプトの自由設定（カスタム解析が可能）

---

## 🌐 動作環境

- Windows 10 / Windows 11
- .NET 6.0 以上

---

## ⚙️ 使用技術

- C# (.NET)
- WPF（UIフレームワーク）
- NAudio（音声キャプチャ）
- Vosk / Google Cloud Speech-to-Text / Web Speech API（音声認識エンジン）
- WebView2（Web Speech API連携）
- OpenAI / Grok / Gemini API（AI解析）
- **Microsoft.ML.Tokenizers（トークンカウント）**

---

## 📦 必要ライブラリ

- NAudio
- Vosk
- Google.Cloud.Speech.V1
- Microsoft.Web.WebView2.Wpf
- System.Net.Http
- System.Text.Json
- **Microsoft.ML.Tokenizers**

```shell
# NuGet経由でインストール
Install-Package NAudio
Install-Package Vosk
Install-Package Google.Cloud.Speech.V1
Install-Package Microsoft.Web.WebView2.Wpf
Install-Package System.Net.Http
Install-Package System.Text.Json
Install-Package Microsoft.ML.Tokenizers -Version 0.22.0-preview.24179.1
```

---

## 🛠️ セットアップ手順

1. リポジトリをクローン

```shell
git clone https://github.com/vril-dev/NexVoiceSync.git
```

2. 必要なライブラリをインストール

```shell
cd NexVoiceSync
dotnet restore
```

3. `appsettings.json` を設定（APIキー、モデルパスなど）

```json
{
  "VoskSettings": {
    "ModelPath": "config/vosk-model-ja-0.22"
  },
  "GoogleCloud": {
    "ApiKeyPath": "config/google-cloud-key.json"
  },
  "AISettings": {
    "DefaultProvider": "OpenAI",
    "Providers": {
      "OpenAI": {
        "ApiKeyPath": "config/openai-key.txt",
        "Endpoint": "https://api.openai.com/v1/chat/completions"
      }
    }
  }
}
```

4. **Voskのモデルをダウンロードして設定**

   Vosk を利用する場合は、日本語モデル `vosk-model-ja-0.22` をダウンロードして `config` フォルダに配置する必要があります。

   ```shell
   mkdir config
   cd config
   curl -LO https://alphacephei.com/vosk/models/vosk-model-ja-0.22.zip
   unzip vosk-model-ja-0.22.zip
   rm vosk-model-ja-0.22.zip
   ```

   または、[公式サイト](https://alphacephei.com/vosk/models) から手動でダウンロードして `config` フォルダ内に解凍してください。

   **注意:** `vosk-model-ja-0.22` は2GB以上のメモリを使用するため、リソースに余裕がある環境で使用してください。
   軽量版のモデルも存在しますが、私の環境では精度が低く、実用的ではありませんでした。

5. **APIキーの設定（Google Speech-to-Text / OpenAI）**

   - **Google Speech-to-Text** を利用する場合、Google Cloud Console でプロジェクトを作成し、認証情報を生成してください。
   - 生成された **サービスアカウントキー（JSONファイル）** を `config/google-cloud-key.json` に保存してください。
   - **OpenAI API** を利用する場合、[OpenAIの公式サイト](https://platform.openai.com/signup/) でアカウントを作成し、APIキーを取得してください。
   - 取得したAPIキーを `config/openai-key.txt` に保存してください。

6. アプリケーションを実行

```shell
dotnet run
```

---

## ⚙️ Web サーバーの設定

`appsettings.json` で `WebServer` セクションを編集することで、  
ローカルサーバー (`http://localhost`) または外部 API に接続できます。

```json
{
  "WebServer": {
    "UseLocalServer": true,               // true: ローカルサーバー, false: 外部API
    "ServerUrl": "http://localhost:3800/" // 使用するサーバーの URL
  }
}
```

### 📢 `UseLocalServer` の説明
- `true` → ローカルサーバー (`SimpleHttpServer`) を `ServerUrl` で指定された URL で起動。
- `false` → `ServerUrl` に指定された **外部 API URL** に接続。

---

## 🌐 WebView2 の設定

`appsettings.json` で `WebView2` セクションを編集することで、  
WebView2 のユーザーデータフォルダの場所を指定できます。

```json
{
  "WebView2": {
    "UserDataFolder": "C:\\NextVoiceSync\\WebView2Data"
  }
}
```

### 📢 `UserDataFolder` の説明
- **`UserDataFolder`** → WebView2 のユーザーデータを保存するフォルダの場所を指定。  
- 設定されていない場合は、`WebView2Data` というデフォルトフォルダが使用されます。

---

## 📂 index.html の配置

ローカルサーバーを使用する場合、  
`Resources/index.html` に HTML ファイルを配置してください。

`index.html` は WebView2 で読み込まれるため、  
正しい場所にファイルが存在しない場合は **404 エラー** になります。

```bash
/NextVoiceSync/
├── /Libs/
│   ├── /Recognizers/
│   └── /Server/
├── /Resources/
│   └── index.html
└── /appsettings.json
```

---

## 📝 appsettings.json のサンプル

```json
{
  "WebServer": {
    "UseLocalServer": true,
    "ServerUrl": "http://localhost:3800/"
  },
  "WebView2": {
    "UserDataFolder": "C:\\NextVoiceSync\\WebView2Data"
  },
  "VoskSettings": {
    "ModelPath": "config/vosk-model-ja-0.22"
  },
  "GoogleCloud": {
    "ApiKeyPath": "config/google-cloud-key.json"
  },
  "AISettings": {
    "DefaultProvider": "OpenAI",
    "Providers": {
      "OpenAI": {
        "ApiKeyPath": "config/openai-key.txt",
        "Endpoint": "https://api.openai.com/v1/chat/completions"
      }
    }
  }
}
```

---

## 🚀 使用方法

1. `appsettings.json` を編集して、`ServerUrl` や `WebView2` の設定を更新します。
2. `index.html` を `Resources` フォルダに配置します。
3. Visual Studio で `NextVoiceSync.sln` を開き、ビルドして実行します。

---

## 💡 推奨設定

- **会議での使用には、ループバックを利用し、Google Cloud Speech-to-Textを推奨します。**

---

## 🎯 今後の展望

- **AI解析機能の拡張**
  - 複数話者対応
  - 解析結果のフォーマットオプション追加（要約、リスト表示など）
  - **Grok や Gemini の統合（開発予定）**

---

## 📄 ライセンス

本プロジェクトは MIT ライセンスの下で公開されています。  
詳細は [LICENSE](LICENSE) を参照してください。

---

## [1.0.2] - 2025-04-07

### 追加
- Whisper (whisper.cpp) を使用した **録音後の音声ファイル解析機能** を追加。
- メニューバーに「音声ファイル解析」メニューを新設。録音済み `.wav` ファイルを選択し、`whisper-cli.exe` による解析を実行可能。
- `Libs/PostAnalysis/WhisperService.cs` を新設し、`IPostAnalysisService` インターフェースにより拡張性を確保。
- 解析結果は `.wav` ファイルと同じディレクトリに `.txt` 形式で自動保存される。
- `appsettings.json` に `"PostAnalysis:Whisper"` セクションを追加。

### その他
- 設定構成とログ出力の簡素化、および UI 側の状態管理を調整。

## [1.0.1] - 2025-03-31
### 追加
- `WebSpeechRecognizer` で `GetUserDataFolder()` を `IConfiguration` から取得可能に修正。
- `appsettings.json` で `WebView2:UserDataFolder` を動的に指定可能。
- `SimpleHttpServer` から `port` 引数を削除し、`ServerUrl` で URL を管理。
- `MainWindow.xaml.cs` で `WebSpeechRecognizer` に `IConfiguration` を DI で注入。
- `appsettings.json` で `ServerUrl` にポート込みの URL を直接指定可能。
- `UseLocalServer` が `false` の場合は `ServerUrl` に外部 API を指定可能。
- `設定` メニューに「アプリケーションフォルダを開く」項目を追加。

### 修正
- `SimpleHttpServer` で `port` を削除して `BaseUrl` に一本化。
- `WebView2` の初期化エラーを回避するための `UserDataFolder` の動的設定追加。

### 互換性
- `Microsoft.ML.Tokenizers` のバージョンを `0.22.0-preview.24179.1` に固定。
