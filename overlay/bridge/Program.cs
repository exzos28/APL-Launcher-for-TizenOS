using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tizen.Applications;
using Tizen.Applications.Messages;

namespace APL.Bridge
{
    /// <summary>
    /// The launcher's private relay to Vetro, as launcher/bridge/service.js is outside a hybrid
    /// package. Vetro (http://127.0.0.1:8099) does not let the screen's apps read its answers (CORS):
    /// the launcher (launcher/js/system.js, bridgeRequest) sends { id, method, path, body } to the
    /// trusted port APL_BRIDGE, this service makes the request and answers on the launcher's trusted
    /// port APL_BRIDGE_REPLY with { id, status, part, parts, body }, large answers in pieces.
    /// Trusted ports only take apps signed with this package's certificate; no network port is
    /// opened. Only the paths the launcher needs are relayed. It stops after 15 minutes without a request.
    /// </summary>
    internal class Program : ServiceApplication
    {
        private const string PortName = "APL_BRIDGE";
        private const string LauncherId = "APLauncher.Home", ReplyPort = "APL_BRIDGE_REPLY";
        private const string Host = "127.0.0.1";
        private const int VetroPort = 8099;
        private const int Chunk = 4000;                       // characters per message, as service.js
        private static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(150);
        private static readonly Regex Allowed = new Regex(@"^/(status|screen|tv-settings|tv-menu|exec|components/action|control|apps)(/|\?|$)");

        private MessagePort port;
        private readonly object sendLock = new object();
        private Timer idle;

        protected override void OnCreate()
        {
            base.OnCreate();
            try
            {
                port = new MessagePort(PortName, true);
                port.MessageReceived += OnMessage;
                port.Listen();
                Note("listening on " + PortName);
            }
            catch (Exception ex) { Note("listen failed: " + ex.Message); }
            idle = new Timer(delegate { Note("idle, exiting"); try { Exit(); } catch (Exception) { } }, null, IdleAfter, Timeout.InfiniteTimeSpan);
        }

        protected override void OnTerminate()
        {
            try { port?.StopListening(); } catch (Exception) { }
            base.OnTerminate();
        }

        private void OnMessage(object sender, MessageReceivedEventArgs e)
        {
            idle?.Change(IdleAfter, Timeout.InfiniteTimeSpan);
            string id = Item(e.Message, "id"), method = Item(e.Message, "method"), path = Item(e.Message, "path"), body = Item(e.Message, "body");
            if (method.Length == 0) method = "GET";
            if (!Allowed.IsMatch(path) || (method != "GET" && method != "POST"))
            {
                Answer(id, 404, "{\"error\":\"not relayed\"}");
                return;
            }
            // Off the main loop: an /exec can take a while, and other requests keep coming meanwhile
            Task.Run(async () =>
            {
                try
                {
                    var (status, text) = await Request(method, path, body);
                    Answer(id, status, text);
                }
                catch (Exception ex)
                {
                    Note("Vetro unreachable: " + ex.Message);
                    Answer(id, 502, "{\"ok\":false,\"error\":\"Vetro unreachable: " + ex.Message.Replace("\\", "\\\\").Replace("\"", "'") + "\"}");
                }
            });
        }

        private void Answer(string id, int status, string body)
        {
            body = body ?? "";
            int parts = Math.Max(1, (body.Length + Chunk - 1) / Chunk);
            lock (sendLock)
            {
                for (int i = 0; i < parts; i++)
                {
                    try
                    {
                        var m = new Bundle();
                        m.AddItem("id", id);
                        m.AddItem("status", status.ToString());
                        m.AddItem("part", i.ToString());
                        m.AddItem("parts", parts.ToString());
                        m.AddItem("body", body.Substring(i * Chunk, Math.Min(Chunk, body.Length - i * Chunk)));
                        port.Send(m, LauncherId, ReplyPort);
                    }
                    catch (Exception ex) { Note("answer " + id + ": " + ex.Message); return; }
                }
            }
        }

        /// <summary>Plain HTTP/1.0 to Vetro, as the Overlay does (overlay/Vetro.cs): status and body.</summary>
        private static async Task<(int, string)> Request(string method, string path, string body)
        {
            using (var cts = new CancellationTokenSource(RequestTimeout))
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(Host, VetroPort, cts.Token);
                NetworkStream stream = client.GetStream();
                byte[] content = Encoding.UTF8.GetBytes(body ?? "");
                string head = method + " " + path + " HTTP/1.0\r\nHost: 127.0.0.1\r\nConnection: close\r\n" +
                              "Content-Type: application/json\r\nContent-Length: " + content.Length + "\r\n\r\n";
                byte[] headBytes = Encoding.ASCII.GetBytes(head);
                await stream.WriteAsync(headBytes, 0, headBytes.Length, cts.Token);
                if (content.Length > 0) await stream.WriteAsync(content, 0, content.Length, cts.Token);
                var all = new MemoryStream();
                byte[] buffer = new byte[16384];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0) all.Write(buffer, 0, read);
                string text = Encoding.UTF8.GetString(all.ToArray());
                int split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                int status = 502;
                Match m = Regex.Match(text, @"^HTTP/\d\.\d (\d{3})");
                if (m.Success) status = int.Parse(m.Groups[1].Value);
                return (status, split < 0 ? "" : text.Substring(split + 4));
            }
        }

        private static string Item(Bundle b, string key)
        {
            try { return b.Contains(key) ? b.GetItem<string>(key) ?? "" : ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>The package's data folder, next to the Overlay's overlay.log: bridge.log.</summary>
        private static void Note(string message)
        {
            try
            {
                string file = Path.Combine(Current.DirectoryInfo.Data, "bridge.log");
                if (File.Exists(file) && new FileInfo(file).Length > 32768) File.Delete(file);
                File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\n");
            }
            catch (Exception) { }
        }

        private static void Main(string[] args)
        {
            new Program().Run(args);
        }
    }
}
