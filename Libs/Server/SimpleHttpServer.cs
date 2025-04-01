using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NextVoiceSync.Libs.Server
{
    class SimpleHttpServer
    {
        private readonly string htmlFilePath;
        private readonly string hostUrl;
        private HttpListener listener;

        public SimpleHttpServer(string htmlFilePath, string hostUrl)
        {
            this.htmlFilePath = htmlFilePath;
            this.hostUrl = hostUrl;
        }

        public void Start()
        {
            listener = new HttpListener();
            listener.Prefixes.Add(hostUrl);
            listener.Start();

            Task.Run(() => Listen());
        }

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

        public void Stop()
        {
            listener.Stop();
        }
    }
}
