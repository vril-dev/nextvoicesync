using Microsoft.Extensions.Configuration;
using NextVoiceSync.Application.Ai;
using NextVoiceSync.Application.PostAnalysis;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NextVoiceSync.Cli;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsageError = 2;
    private const int ExitRuntimeError = 1;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || HasHelpFlag(args))
            {
                ShowHelp();
                return ExitSuccess;
            }

            string command = args[0].ToLowerInvariant();
            string[] rest = args.Skip(1).ToArray();

            return command switch
            {
                "prompts" => RunPromptList(rest),
                "ai" => await RunAiAnalyzeAsync(rest),
                "whisper" => await RunWhisperAnalyzeAsync(rest),
                "mic" => await RunMicCaptureAsync(rest),
                "realtime" => await RunRealtimeCaptureAsync(rest),
                _ => FailUsage($"未知のコマンドです: {command}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return ExitRuntimeError;
        }
    }

    private static int RunPromptList(string[] args)
    {
        string? configPath = GetOptionValue(args, "--config");
        IConfiguration configuration = LoadConfiguration(configPath);

        var prompts = configuration.GetSection("PromptSettings:Prompts")
            .Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();

        if (prompts.Count == 0)
        {
            Console.WriteLine("プロンプトは未定義です。");
            return ExitSuccess;
        }

        foreach (string key in prompts.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            Console.WriteLine(key);
        }

        return ExitSuccess;
    }

    private static async Task<int> RunAiAnalyzeAsync(string[] args)
    {
        string? inputPath = GetOptionValue(args, "--input");
        string? inlineText = GetOptionValue(args, "--text");
        string? promptKey = GetOptionValue(args, "--prompt-key");
        string? promptText = GetOptionValue(args, "--prompt-text");
        string? configPath = GetOptionValue(args, "--config");
        string? outputPath = GetOptionValue(args, "--output");

        if (string.IsNullOrWhiteSpace(inputPath) && string.IsNullOrWhiteSpace(inlineText))
        {
            return FailUsage("`ai` は `--input` か `--text` のどちらかが必要です。");
        }

        IConfiguration configuration = LoadConfiguration(configPath);
        string inputText = ResolveInputText(inputPath, inlineText);
        string resolvedPrompt = ResolvePrompt(configuration, promptKey, promptText);

        var analyzer = new AiAnalyzer(configuration);
        analyzer.AppendLog = message => Console.Error.WriteLine($"[ai] {message}");

        string result = await analyzer.AnalyzeTextAsync(inputText, resolvedPrompt);
        WriteOutput(result, outputPath);
        return ExitSuccess;
    }

    private static async Task<int> RunWhisperAnalyzeAsync(string[] args)
    {
        string? wavPath = GetOptionValue(args, "--wav");
        string? configPath = GetOptionValue(args, "--config");
        string? outputPath = GetOptionValue(args, "--output");

        if (string.IsNullOrWhiteSpace(wavPath))
        {
            return FailUsage("`whisper` は `--wav` が必要です。");
        }

        IConfiguration configuration = LoadConfiguration(configPath);
        var whisper = new WhisperService(configuration);
        whisper.AppendLog = message => Console.Error.WriteLine($"[whisper] {message}");

        string result = await whisper.AnalyzeAsync(wavPath);
        WriteOutput(result, outputPath);
        return ExitSuccess;
    }

    private static async Task<int> RunMicCaptureAsync(string[] args)
    {
        string? configPath = GetOptionValue(args, "--config");
        string? outputPath = GetOptionValue(args, "--output");
        string ffmpegPath = GetOptionValue(args, "--ffmpeg") ?? "ffmpeg";
        string? keepWavPath = GetOptionValue(args, "--keep-wav");

        int seconds = GetIntOptionValue(args, "--seconds", defaultValue: 15, minValue: 1, maxValue: 3600);
        int sampleRate = GetIntOptionValue(args, "--sample-rate", defaultValue: 16000, minValue: 8000, maxValue: 192000);
        int channels = GetIntOptionValue(args, "--channels", defaultValue: 1, minValue: 1, maxValue: 8);

        string format = GetOptionValue(args, "--format") ?? GetDefaultFfmpegInputFormat();
        string inputDevice = GetOptionValue(args, "--device") ?? GetDefaultFfmpegInputDevice();

        string tempWavPath = Path.Combine(
            Path.GetTempPath(),
            $"nextvoicesync-mic-{DateTime.UtcNow:yyyyMMddHHmmssfff}.wav");

        try
        {
            IConfiguration configuration = LoadConfiguration(configPath);

            Console.Error.WriteLine($"[mic] ffmpeg 録音開始: {seconds} 秒");
            await CaptureWithFfmpegAsync(
                ffmpegPath,
                format,
                inputDevice,
                sampleRate,
                channels,
                seconds,
                tempWavPath);

            var whisper = new WhisperService(configuration);
            whisper.AppendLog = message => Console.Error.WriteLine($"[whisper] {message}");

            string result = await whisper.AnalyzeAsync(tempWavPath);
            WriteOutput(result, outputPath);

            if (!string.IsNullOrWhiteSpace(keepWavPath))
            {
                string fullKeepPath = Path.GetFullPath(keepWavPath);
                string? dir = Path.GetDirectoryName(fullKeepPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.Copy(tempWavPath, fullKeepPath, overwrite: true);
                Console.Error.WriteLine($"[mic] 録音ファイルを保存しました: {fullKeepPath}");
            }

            return ExitSuccess;
        }
        finally
        {
            if (File.Exists(tempWavPath))
            {
                File.Delete(tempWavPath);
            }
        }
    }

    private static async Task<int> RunRealtimeCaptureAsync(string[] args)
    {
        string? configPath = GetOptionValue(args, "--config");
        string? outputPath = GetOptionValue(args, "--output");
        string ffmpegPath = GetOptionValue(args, "--ffmpeg") ?? "ffmpeg";
        int chunkSeconds = GetIntOptionValue(args, "--chunk-seconds", defaultValue: 1, minValue: 1, maxValue: 30);
        int sampleRate = GetIntOptionValue(args, "--sample-rate", defaultValue: 16000, minValue: 8000, maxValue: 192000);
        int channels = GetIntOptionValue(args, "--channels", defaultValue: 1, minValue: 1, maxValue: 8);
        int maxSegments = GetIntOptionValue(args, "--max-segments", defaultValue: 0, minValue: 0, maxValue: 100000);
        double silenceThresholdDb = GetDoubleOptionValue(args, "--silence-threshold-db", defaultValue: -42.0, minValue: -90.0, maxValue: -5.0);
        double silenceSeconds = GetDoubleOptionValue(args, "--silence-seconds", defaultValue: 1.2, minValue: 0.2, maxValue: 10.0);
        double minSpeechSeconds = GetDoubleOptionValue(args, "--min-speech-seconds", defaultValue: 0.6, minValue: 0.2, maxValue: 10.0);
        string format = GetOptionValue(args, "--format") ?? GetDefaultFfmpegInputFormat();
        string inputDevice = GetOptionValue(args, "--device") ?? GetDefaultFfmpegInputDevice();

        IConfiguration configuration = LoadConfiguration(configPath);
        var whisper = new WhisperService(configuration);
        whisper.AppendLog = message => Console.Error.WriteLine($"[whisper] {message}");

        Console.Error.WriteLine("[realtime] Ctrl+C で停止します。");
        Console.Error.WriteLine($"[realtime] chunk={chunkSeconds}s silence={silenceSeconds}s threshold={silenceThresholdDb}dB");
        Console.Error.WriteLine($"[realtime] format={format} device={inputDevice}");

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler? handler = null;
        handler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.Error.WriteLine("[realtime] 停止要求を受信しました。現在のチャンク処理後に終了します。");
        };
        Console.CancelKeyPress += handler;

        try
        {
            int segment = 0;
            bool speaking = false;
            double silenceAccumulated = 0;
            double speechAccumulated = 0;
            string? lastChunkText = null;
            var speechTexts = new List<string>();

            while (!cts.IsCancellationRequested)
            {
                if (maxSegments > 0 && segment >= maxSegments)
                {
                    break;
                }

                segment++;
                string tempWavPath = Path.Combine(
                    Path.GetTempPath(),
                    $"nextvoicesync-rt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{segment}.wav");

                try
                {
                    Console.Error.WriteLine($"[realtime] chunk {segment} 録音開始");
                    await CaptureWithFfmpegAsync(
                        ffmpegPath,
                        format,
                        inputDevice,
                        sampleRate,
                        channels,
                        chunkSeconds,
                        tempWavPath);

                    double dbfs = CalculateWavDbfs(tempWavPath);
                    bool isSilent = dbfs < silenceThresholdDb;
                    string chunkText = (await whisper.AnalyzeAsync(tempWavPath)).Trim();
                    Console.Error.WriteLine($"[realtime] chunk {segment} db={dbfs:F1} silent={isSilent}");

                    if (!isSilent)
                    {
                        speaking = true;
                        silenceAccumulated = 0;
                        speechAccumulated += chunkSeconds;
                        AddChunkText(speechTexts, ref lastChunkText, chunkText);
                    }
                    else if (speaking)
                    {
                        silenceAccumulated += chunkSeconds;
                        AddChunkText(speechTexts, ref lastChunkText, chunkText);

                        if (silenceAccumulated >= silenceSeconds)
                        {
                            FlushSpeechSegment(speechTexts, outputPath, speechAccumulated, minSpeechSeconds);
                            speaking = false;
                            silenceAccumulated = 0;
                            speechAccumulated = 0;
                            lastChunkText = null;
                        }
                    }
                }
                finally
                {
                    if (File.Exists(tempWavPath))
                    {
                        File.Delete(tempWavPath);
                    }
                }
            }

            if (speaking)
            {
                FlushSpeechSegment(speechTexts, outputPath, speechAccumulated, minSpeechSeconds);
            }
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        return ExitSuccess;
    }

    private static IConfiguration LoadConfiguration(string? configPath)
    {
        string baseDir = AppContext.BaseDirectory;
        string path = string.IsNullOrWhiteSpace(configPath)
            ? Path.Combine(baseDir, "appsettings.json")
            : Path.GetFullPath(configPath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"設定ファイルが見つかりません: {path}");
        }

        return new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(path)!)
            .AddJsonFile(Path.GetFileName(path), optional: false, reloadOnChange: false)
            .Build();
    }

    private static string ResolveInputText(string? inputPath, string? inlineText)
    {
        if (!string.IsNullOrWhiteSpace(inlineText))
        {
            return inlineText;
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("入力テキストが指定されていません。");
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"入力ファイルが見つかりません: {inputPath}");
        }

        return File.ReadAllText(inputPath, Encoding.UTF8);
    }

    private static string ResolvePrompt(IConfiguration configuration, string? promptKey, string? promptText)
    {
        if (!string.IsNullOrWhiteSpace(promptText))
        {
            return promptText;
        }

        var prompts = configuration.GetSection("PromptSettings:Prompts")
            .Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();

        string key = string.IsNullOrWhiteSpace(promptKey)
            ? configuration["PromptSettings:DefaultPrompt"] ?? string.Empty
            : promptKey;

        if (!string.IsNullOrWhiteSpace(key) && prompts.TryGetValue(key, out string? prompt))
        {
            return prompt;
        }

        throw new InvalidOperationException("利用可能なプロンプトが見つかりません。`--prompt-text` を指定してください。");
    }

    private static void FlushSpeechSegment(
        List<string> speechTexts,
        string? outputPath,
        double speechAccumulated,
        double minSpeechSeconds)
    {
        if (speechAccumulated < minSpeechSeconds || speechTexts.Count == 0)
        {
            speechTexts.Clear();
            return;
        }

        string merged = MergeChunkTexts(speechTexts);
        speechTexts.Clear();
        if (string.IsNullOrWhiteSpace(merged))
        {
            return;
        }

        string line = $"[{DateTime.Now:HH:mm:ss}] {merged}";
        WriteRealtimeOutput(line, outputPath);
    }

    private static void AddChunkText(List<string> speechTexts, ref string? lastChunkText, string chunkText)
    {
        if (string.IsNullOrWhiteSpace(chunkText))
        {
            return;
        }

        string normalized = NormalizeWhitespace(chunkText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (string.Equals(normalized, lastChunkText, StringComparison.Ordinal))
        {
            return;
        }

        speechTexts.Add(normalized);
        lastChunkText = normalized;
    }

    private static string MergeChunkTexts(List<string> texts)
    {
        if (texts.Count == 0)
        {
            return string.Empty;
        }

        string merged = texts[0];
        for (int i = 1; i < texts.Count; i++)
        {
            merged = MergeWithOverlap(merged, texts[i]);
        }

        return merged.Trim();
    }

    private static string MergeWithOverlap(string previous, string next)
    {
        if (string.IsNullOrWhiteSpace(previous))
        {
            return next;
        }

        if (string.IsNullOrWhiteSpace(next))
        {
            return previous;
        }

        if (previous.Contains(next, StringComparison.Ordinal))
        {
            return previous;
        }

        int maxOverlap = Math.Min(previous.Length, next.Length);
        for (int overlap = maxOverlap; overlap >= 4; overlap--)
        {
            if (previous.EndsWith(next[..overlap], StringComparison.Ordinal))
            {
                return previous + next[overlap..];
            }
        }

        return $"{previous} {next}";
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static double CalculateWavDbfs(string wavPath)
    {
        using var fs = File.OpenRead(wavPath);
        using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

        if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "RIFF")
        {
            throw new InvalidDataException("WAVヘッダ(RIFF)が不正です。");
        }

        _ = br.ReadUInt32();
        if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "WAVE")
        {
            throw new InvalidDataException("WAVヘッダ(WAVE)が不正です。");
        }

        ushort bitsPerSample = 16;
        long dataOffset = -1;
        uint dataSize = 0;

        while (fs.Position + 8 <= fs.Length)
        {
            string chunkId = Encoding.ASCII.GetString(br.ReadBytes(4));
            uint chunkSize = br.ReadUInt32();
            long chunkDataPos = fs.Position;

            if (chunkId == "fmt ")
            {
                _ = br.ReadUInt16();
                _ = br.ReadUInt16();
                _ = br.ReadUInt32();
                _ = br.ReadUInt32();
                _ = br.ReadUInt16();
                bitsPerSample = br.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                dataOffset = chunkDataPos;
                dataSize = chunkSize;
                break;
            }

            fs.Position = chunkDataPos + chunkSize;
            if ((chunkSize & 1) == 1)
            {
                fs.Position++;
            }
        }

        if (dataOffset < 0 || dataSize == 0)
        {
            return -90.0;
        }

        if (bitsPerSample != 16)
        {
            throw new InvalidDataException("16bit PCM 以外のWAVには対応していません。");
        }

        fs.Position = dataOffset;
        byte[] data = br.ReadBytes((int)dataSize);
        if (data.Length < 2)
        {
            return -90.0;
        }

        double sumSquares = 0;
        int sampleCount = 0;
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            short sample = BitConverter.ToInt16(data, i);
            double v = sample / 32768.0;
            sumSquares += v * v;
            sampleCount++;
        }

        if (sampleCount == 0)
        {
            return -90.0;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);
        if (rms <= 1e-9)
        {
            return -90.0;
        }

        return 20.0 * Math.Log10(rms);
    }

    private static int GetIntOptionValue(
        string[] args,
        string option,
        int defaultValue,
        int minValue,
        int maxValue)
    {
        string? raw = GetOptionValue(args, option);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out int value) || value < minValue || value > maxValue)
        {
            throw new ArgumentException($"`{option}` は {minValue} から {maxValue} の整数で指定してください。");
        }

        return value;
    }

    private static double GetDoubleOptionValue(
        string[] args,
        string option,
        double defaultValue,
        double minValue,
        double maxValue)
    {
        string? raw = GetOptionValue(args, option);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!double.TryParse(raw, out double value) || value < minValue || value > maxValue)
        {
            throw new ArgumentException($"`{option}` は {minValue} から {maxValue} の数値で指定してください。");
        }

        return value;
    }

    private static async Task CaptureWithFfmpegAsync(
        string ffmpegPath,
        string inputFormat,
        string inputDevice,
        int sampleRate,
        int channels,
        int seconds,
        string wavPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-loglevel");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add(inputFormat);
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(inputDevice);
        process.StartInfo.ArgumentList.Add("-ac");
        process.StartInfo.ArgumentList.Add(channels.ToString());
        process.StartInfo.ArgumentList.Add("-ar");
        process.StartInfo.ArgumentList.Add(sampleRate.ToString());
        process.StartInfo.ArgumentList.Add("-t");
        process.StartInfo.ArgumentList.Add(seconds.ToString());
        process.StartInfo.ArgumentList.Add(wavPath);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ffmpeg を起動できませんでした。`--ffmpeg` でパスを指定するか、PATH を確認してください: {ffmpegPath}",
                ex);
        }

        string stdOut = await process.StandardOutput.ReadToEndAsync();
        string stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || !File.Exists(wavPath))
        {
            string detail = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
            throw new InvalidOperationException(
                $"ffmpeg 録音に失敗しました。`--format` / `--device` を見直してください。詳細: {detail}".Trim());
        }
    }

    private static string GetDefaultFfmpegInputFormat()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "dshow";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "avfoundation";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "pulse";
        }

        throw new PlatformNotSupportedException("このOSの ffmpeg マイク入力フォーマットは未定義です。`--format` を指定してください。");
    }

    private static string GetDefaultFfmpegInputDevice()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "audio=default";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return ":0";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "default";
        }

        throw new PlatformNotSupportedException("このOSの ffmpeg マイク入力デバイスは未定義です。`--device` を指定してください。");
    }

    private static void WriteOutput(string text, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.WriteLine(text);
            return;
        }

        string fullPath = Path.GetFullPath(outputPath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, text, Encoding.UTF8);
        Console.Error.WriteLine($"出力を保存しました: {fullPath}");
    }

    private static void WriteRealtimeOutput(string line, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.WriteLine(line);
            return;
        }

        string fullPath = Path.GetFullPath(outputPath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.AppendAllText(fullPath, line + Environment.NewLine, Encoding.UTF8);
        Console.Error.WriteLine($"[realtime] 出力追記: {fullPath}");
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasHelpFlag(string[] args)
    {
        return args.Any(a =>
            string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase));
    }

    private static int FailUsage(string message)
    {
        Console.Error.WriteLine($"USAGE ERROR: {message}");
        Console.Error.WriteLine();
        ShowHelp();
        return ExitUsageError;
    }

    private static void ShowHelp()
    {
        Console.WriteLine(
@"nextvoicesync-cli

Usage:
  nextvoicesync-cli prompts [--config <path>]
  nextvoicesync-cli ai --input <text-file> [--prompt-key <name>] [--prompt-text <text>] [--config <path>] [--output <path>]
  nextvoicesync-cli ai --text <value> [--prompt-key <name>] [--prompt-text <text>] [--config <path>] [--output <path>]
  nextvoicesync-cli whisper --wav <path> [--config <path>] [--output <path>]
  nextvoicesync-cli mic [--seconds <n>] [--ffmpeg <path>] [--format <name>] [--device <name>] [--sample-rate <n>] [--channels <n>] [--keep-wav <path>] [--config <path>] [--output <path>]
  nextvoicesync-cli realtime [--chunk-seconds <n>] [--silence-threshold-db <n>] [--silence-seconds <n>] [--min-speech-seconds <n>] [--max-segments <n>] [--ffmpeg <path>] [--format <name>] [--device <name>] [--sample-rate <n>] [--channels <n>] [--config <path>] [--output <path>]
");
    }
}
