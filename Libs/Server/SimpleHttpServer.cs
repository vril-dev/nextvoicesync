using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NextVoiceSync.Libs.Server
{
    /// <summary>
    /// 指定された HTML ファイルをローカル HTTP サーバーで配信する軽量なサーバークラス。
    /// WebView2 などからのローカルアクセス用途を想定。
    /// </summary>
    class SimpleHttpServer
    {
        private readonly string htmlFilePath;
        private readonly string hostUrl;
        private HttpListener listener;

        /// <summary>
        /// HTMLファイルパスとホストURLを指定してインスタンスを初期化する。
        /// </summary>
        public SimpleHttpServer(string htmlFilePath, string hostUrl)
        {
            this.htmlFilePath = htmlFilePath;
            this.hostUrl = hostUrl;
        }

        /// <summary>
        /// HTTPサーバーを開始し、指定されたURLでリクエストを待機する。
        /// </summary>
        public void Start()
        {
            listener = new HttpListener();
            listener.Prefixes.Add(hostUrl);
            listener.Start();

            Task.Run(() => Listen());
        }

        /// <summary>
        /// 非同期でクライアントからの接続を待ち受け、HTMLを返す。
        /// </summary>
        private async Task Listen()
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                var response = context.Response;

                string htmlContent = File.ReadAllText(htmlFilePath);
                byte[] buffer = Encoding.UTF8.GetBytes(htmlContent);

                response.ContentType = "text/html";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
        }

        /// <summary>
        /// HTTPサーバーを停止する。
        /// </summary>
        public void Stop()
        {
            listener.Stop();
        }
    }
}
