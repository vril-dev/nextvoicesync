# NexVoiceSync

リアルタイムでPCの音声（ループバックまたはマイク）をキャプチャし、AIを利用して音声をテキストに変換するWindows向けアプリケーションです。

---

## 概要

NexVoiceSync は、PCの音声（ループバックまたはマイク）をキャプチャし、リアルタイムでテキスト化することを主目的とするアプリケーションです。  
さらに、テキスト化した音声データを AI を活用して解析する機能を備えており、  
会議の要約やキーワード抽出、議事録作成などを効率化できます。

NexVoiceSync は、会議やオンラインミーティングなどの音声をリアルタイムに認識・テキスト化するためのツールです。  
ユーザーは任意のタイミングで音声解析を開始・停止したり、解析内容のプロンプトを自由に設定することができます。

---

## アーキテクチャ

プロジェクト構成は `Presentation` / `Application` / `Domain` / `Infrastructure` のレイヤーに分離しています。  
詳細は [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) を参照してください。

---

## GUI と CLI

- **GUI**: `NextVoiceSync`（既存の Windows WPF アプリ）
- **CLI**: `NextVoiceSync.Cli`（Windows / Linux / macOS / WSL 向け）
- **共通ロジック**: `NextVoiceSync.Core`

CLI 例:

```bash
# プロンプト一覧
dotnet run --project NextVoiceSync.Cli -- prompts

# AI解析（ファイル入力）
dotnet run --project NextVoiceSync.Cli -- ai --input ./input.txt --prompt-key 要約

# Whisper後解析
dotnet run --project NextVoiceSync.Cli -- whisper --wav ./sample.wav

# マイク録音 + Whisper解析（15秒）
dotnet run --project NextVoiceSync.Cli -- mic --seconds 15

# リアルタイム文字起こし（無音区切り、1秒チャンク）
dotnet run --project NextVoiceSync.Cli -- realtime --chunk-seconds 1
```

---

## CLI コマンド仕様

- `prompts`
  - `appsettings.json` に定義されたプロンプトキーを一覧表示
- `ai`
  - 入力テキストを AI 解析して標準出力またはファイルへ出力
  - `--input`（テキストファイル）または `--text`（インライン文字列）を指定
  - `--prompt-key` / `--prompt-text` でプロンプトを指定
- `whisper`
  - WAV ファイルを `WhisperService` で後解析
  - `--wav` で対象ファイルを指定
- `mic`
  - `ffmpeg` でマイクを録音し、録音後に `WhisperService` で解析
  - `--seconds`（録音秒数、既定15秒）
  - `--ffmpeg` / `--format` / `--device` で録音方法を調整
  - `--keep-wav` で録音WAVを保存可能
- `realtime`
  - `ffmpeg` で短い音声チャンクを連続録音し、無音が一定時間続いたら発話として確定
  - `--chunk-seconds`（既定1秒）で反応速度を調整
  - `--silence-threshold-db`（既定 `-42`）で無音判定しきい値を調整
  - `--silence-seconds`（既定 `1.2`）で発話確定までの無音継続時間を調整
  - `--min-speech-seconds`（既定 `0.6`）未満の短音声は出力しない
  - `--max-segments`（既定0=無制限）で自動停止回数を指定
  - `Ctrl+C` で停止

検証ステータス（暫定）:
- `realtime` / `mic` は実装済みですが、環境差分（OS/デバイス/ffmpeg設定）による影響が大きいため、全パターンでの実機テストは未完了です。
- 実運用前に、対象環境で音声入力デバイスと `PostAnalysis:Whisper` 設定の疎通確認を推奨します。

主な共通オプション:
- `--config <path>`: 設定ファイルパス（デフォルトは実行ファイル横の `appsettings.json`）
- `--output <path>`: 解析結果の保存先（未指定時は標準出力）

CLI サンプル:

```bash
# 1) プロンプトキーを確認
dotnet run --project NextVoiceSync.Cli -- prompts

# 2) インライン入力を要約し、結果をファイル保存
dotnet run --project NextVoiceSync.Cli -- ai \
  --text "本日の会議では次期リリース方針を議論した" \
  --prompt-key 要約 \
  --output ./out/summary.txt

# 3) プロンプト本文を直接指定
dotnet run --project NextVoiceSync.Cli -- ai \
  --input ./sample/input.txt \
  --prompt-text "以下の文章を3行で要約してください。"

# 4) Whisper後解析
dotnet run --project NextVoiceSync.Cli -- whisper \
  --wav ./sample/meeting.wav \
  --output ./out/meeting_whisper.txt

# 5) マイク録音して文字起こし（30秒）
dotnet run --project NextVoiceSync.Cli -- mic \
  --seconds 30 \
  --keep-wav ./out/mic.wav \
  --output ./out/mic_transcript.txt

# 6) リアルタイム文字起こし（無音区切り、20回で停止）
dotnet run --project NextVoiceSync.Cli -- realtime \
  --chunk-seconds 1 \
  --silence-threshold-db -42 \
  --silence-seconds 1.2 \
  --max-segments 20 \
  --output ./out/realtime_transcript.txt
```

---

## CLI 単一バイナリ配布

`NextVoiceSync.Cli` は Windows / Linux / macOS 向けに単一バイナリで publish できます。

```bash
# Linux / macOS 向けにまとめて publish
./scripts/publish-cli.sh

# Linux x64 のみ publish
./scripts/publish-cli.sh linux-x64

# Windows x64 を含めて publish（WSL/Linux/macOS でも実行可能）
./scripts/publish-cli.sh linux-x64 osx-arm64 win-x64
```

出力先:
- `artifacts/cli/linux-x64/NextVoiceSync.Cli`
- `artifacts/cli/linux-arm64/NextVoiceSync.Cli`
- `artifacts/cli/osx-x64/NextVoiceSync.Cli`
- `artifacts/cli/osx-arm64/NextVoiceSync.Cli`
- `artifacts/cli/win-x64/NextVoiceSync.Cli.exe`
- `artifacts/cli/win-arm64/NextVoiceSync.Cli.exe`

実行例:

```bash
./artifacts/cli/linux-x64/NextVoiceSync.Cli --help
./artifacts/cli/linux-x64/NextVoiceSync.Cli prompts
```

Windows 実行例（PowerShell）:

```powershell
.\artifacts\cli\win-x64\NextVoiceSync.Cli.exe --help
.\artifacts\cli\win-x64\NextVoiceSync.Cli.exe prompts
```

## プラットフォーム別の CLI 利用方法

前提:
- .NET 8 SDK 以上（`dotnet --version` で確認）
- `appsettings.json` は実行ファイルと同じディレクトリに置くか、`--config` で指定
- `mic` / `realtime` コマンドを使う場合は `ffmpeg` が必要（`ffmpeg -version` で確認）

`ffmpeg` インストール例:

```powershell
# Windows (PowerShell)
winget install --id Gyan.FFmpeg -e
```

```bash
# Ubuntu / WSL2
sudo apt update
sudo apt install -y ffmpeg

# macOS (Homebrew)
brew install ffmpeg
```

`ffmpeg` 入力デフォルト:
- Windows: `--format dshow --device audio=default`
- Linux / WSL2: `--format pulse --device default`
- macOS: `--format avfoundation --device :0`

Windows（PowerShell）:

```powershell
# ソースから実行
dotnet run --project .\NextVoiceSync.Cli -- prompts

# 単一バイナリを実行
.\artifacts\cli\win-x64\NextVoiceSync.Cli.exe prompts

# マイク録音（10秒）
.\artifacts\cli\win-x64\NextVoiceSync.Cli.exe mic --seconds 10

# リアルタイム文字起こし
.\artifacts\cli\win-x64\NextVoiceSync.Cli.exe realtime --chunk-seconds 1
```

Linux / macOS / WSL2（bash）:

```bash
# ソースから実行
dotnet run --project NextVoiceSync.Cli -- prompts

# 単一バイナリを実行（Linux x64 の例）
./artifacts/cli/linux-x64/NextVoiceSync.Cli prompts

# マイク録音（20秒）
./artifacts/cli/linux-x64/NextVoiceSync.Cli mic --seconds 20

# リアルタイム文字起こし
./artifacts/cli/linux-x64/NextVoiceSync.Cli realtime --chunk-seconds 1
```

個別 publish の例:

```bash
# Linux x64（WSL2 を含む）
dotnet publish NextVoiceSync.Cli/NextVoiceSync.Cli.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./artifacts/cli/linux-x64

# macOS Apple Silicon
dotnet publish NextVoiceSync.Cli/NextVoiceSync.Cli.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./artifacts/cli/osx-arm64
```

```powershell
# Windows x64
dotnet publish .\NextVoiceSync.Cli\NextVoiceSync.Cli.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o .\artifacts\cli\win-x64
```

---

## 機能

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

- **CLI（クロスプラットフォーム）**
  - プロンプト一覧の取得
  - AI解析（ファイル/インライン入力）
  - Whisper後解析（WAV入力）
  - `ffmpeg` マイク録音 + Whisper解析（`mic` コマンド）
  - `ffmpeg` チャンク録音 + 逐次文字起こし（`realtime` コマンド）
  - 単一バイナリ配布対応（Windows/Linux/macOS）

---

## 動作環境

- GUI: Windows 10 / Windows 11
- CLI: Windows / Linux / macOS / WSL
- .NET 8 SDK 以上
- `mic` / `realtime` 利用時は `ffmpeg`（外部コマンド）が必要

---

## 使用技術

- C# (.NET)
- WPF（UIフレームワーク）
- NAudio（音声キャプチャ）
- Vosk / Google Cloud Speech-to-Text / Web Speech API（音声認識エンジン）
- WebView2（Web Speech API連携）
- OpenAI / Grok / Gemini API（AI解析）
- **Microsoft.ML.Tokenizers（トークンカウント）**

---

## 必要ライブラリ

- NAudio
- Vosk
- Google.Cloud.Speech.V1
- Microsoft.Web.WebView2.Wpf
- System.Net.Http
- System.Text.Json
- **Microsoft.ML.Tokenizers**
- **ffmpeg（CLI: `mic` / `realtime` コマンド利用時）**

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

## セットアップ手順

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

## Web サーバーの設定

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

### `UseLocalServer` の説明
- `true` → ローカルサーバー (`SimpleHttpServer`) を `ServerUrl` で指定された URL で起動。
- `false` → `ServerUrl` に指定された **外部 API URL** に接続。

---

## WebView2 の設定

`appsettings.json` で `WebView2` セクションを編集することで、  
WebView2 のユーザーデータフォルダの場所を指定できます。

```json
{
  "WebView2": {
    "UserDataFolder": "C:\\NextVoiceSync\\WebView2Data"
  }
}
```

### `UserDataFolder` の説明
- **`UserDataFolder`** → WebView2 のユーザーデータを保存するフォルダの場所を指定。  
- 設定されていない場合は、`WebView2Data` というデフォルトフォルダが使用されます。

---

## index.html の配置

ローカルサーバーを使用する場合、  
`Resources/index.html` に HTML ファイルを配置してください。

`index.html` は WebView2 で読み込まれるため、  
正しい場所にファイルが存在しない場合は **404 エラー** になります。

```bash
/NextVoiceSync/
├── /NextVoiceSync.Core/
│   ├── /Application/
│   ├── /Domain/
│   └── /Infrastructure/Ai/
├── /NextVoiceSync.Cli/
│   └── Program.cs
├── /Infrastructure/            # Windows GUI 専用アダプタ
│   ├── /Recognizers/
│   └── /Server/
├── /Presentation/
│   └── /Windows/
├── /Resources/
│   └── index.html
└── /appsettings.json
```

---

## appsettings.json のサンプル

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

## 使用方法

1. `appsettings.json` を編集して、`ServerUrl` や `WebView2` の設定を更新します。
2. `index.html` を `Resources` フォルダに配置します。
3. Visual Studio で `NextVoiceSync.sln` を開き、ビルドして実行します。

---

## 推奨設定

- **会議での使用には、ループバックを利用し、Google Cloud Speech-to-Textを推奨します。**

---

## 今後の展望

- **AI解析機能の拡張**
  - 複数話者対応
  - 解析結果のフォーマットオプション追加（要約、リスト表示など）
  - **Grok や Gemini の統合（開発予定）**

---

## ライセンス

本プロジェクトは MIT ライセンスの下で公開されています。  
詳細は [LICENSE](LICENSE) を参照してください。

---

## [1.1.0] - 2026-03-09

### 追加
- `NextVoiceSync.Cli` を追加し、Windows / Linux / macOS / WSL で CLI 実行できるように対応。
- `prompts` / `ai` / `whisper` / `mic` / `realtime` の各コマンドを実装。
- `scripts/publish-cli.sh` を追加し、単一バイナリ publish を自動化。
- `realtime` コマンドに無音区切り（しきい値 + 無音継続時間）を追加。
- README に CLI の実行サンプル、`ffmpeg` 前提、無音区切りオプション、単一バイナリ配布手順を追記。

### 変更
- 共通ロジックを `NextVoiceSync.Core` に分離。
- 既存 GUI (`NextVoiceSync`) は維持しつつ、`Core` 参照へ移行。

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
