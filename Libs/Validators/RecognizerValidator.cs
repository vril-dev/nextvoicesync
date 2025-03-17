using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace NextVoiceSync.Libs.Validators
{
    /// <summary>
    /// 音声認識エンジンの設定を検証するユーティリティクラス。
    /// Vosk のモデルパスと Google Speech-to-Text の API キーファイルをチェックする。
    /// </summary>
    class RecognizerValidator
    {
        /// <summary>
        /// Vosk の音声認識モデルが設定されているか確認する。
        /// </summary>
        public static bool CheckVoskModel(IConfiguration configuration)
        {
            string modelPath = configuration["VoskSettings:ModelPath"];
            return !string.IsNullOrEmpty(modelPath) && Directory.Exists(modelPath);
        }

        /// <summary>
        /// Google Speech-to-TextのAPI キーファイルが存在するか確認する。
        /// </summary>
        public static bool CheckGoogleApiKey(IConfiguration configuration)
        {
            string apiKeyPath = configuration["GoogleCloud:ApiKeyPath"];
            return !string.IsNullOrEmpty(apiKeyPath) && File.Exists(apiKeyPath);
        }
    }
}
