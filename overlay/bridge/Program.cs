using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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

        // The platform's message port API itself, to read its error codes (MessagePort.Send only says
        // "Can't send message"): TIZEN_ERROR_IO_ERROR -5, INVALID_PARAMETER -22, OUT_OF_MEMORY -12,
        // PORT_NOT_FOUND / CERTIFICATE_NOT_MATCH / MAX_EXCEEDED / RESOURCE_UNAVAILABLE = 0xfe4f0000 | 1..4.
        [DllImport("libcapi-message-port.so.1")] private static extern int message_port_check_trusted_remote_port(string appId, string port, out bool exist);
        [DllImport("libcapi-message-port.so.1")] private static extern int message_port_check_remote_port(string appId, string port, out bool exist);
        [DllImport("libcapi-message-port.so.1")] private static extern int message_port_send_trusted_message(string appId, string port, IntPtr bundle);
        [DllImport("libcapi-message-port.so.1")] private static extern int message_port_send_message(string appId, string port, IntPtr bundle);
        private bool diagnosed;

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
                Note("listening on " + PortName + ", main thread " + Environment.CurrentManagedThreadId + ", context " + (SynchronizationContext.Current?.GetType().Name ?? "none") +
                     ", my id " + SafeAppId());
                CheckReplyPort("at start");
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
            Relay(id, method, path, body);
        }

        /// <summary>
        /// The request to Vetro is awaited, not run on a thread of its own: the continuation comes back
        /// on the app's main loop (Tizen's synchronization context), where the message port has to be
        /// used — sent from a pool thread, every answer failed with "Can't send message".
        /// </summary>
        private async void Relay(string id, string method, string path, string body)
        {
            int status;
            string text;
            try
            {
                (status, text) = await Request(method, path, body);
            }
            catch (Exception ex)
            {
                Note("Vetro unreachable: " + ex.Message);
                status = 502;
                text = "{\"ok\":false,\"error\":\"Vetro unreachable: " + ex.Message.Replace("\\", "\\\\").Replace("\"", "'") + "\"}";
            }
            Answer(id, status, text);
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
                        if (i == 0) Note("answer " + id + " sent: status " + status + ", " + body.Length + " chars in " + parts + " part(s)");
                    }
                    catch (Exception ex)
                    {
                        Note("answer " + id + " (" + path(ex) + "), thread " + Environment.CurrentManagedThreadId + ": " + ex.GetType().Name + " " + ex.Message + " 0x" + ex.HResult.ToString("x"));
                        if (!diagnosed) { diagnosed = true; CheckReplyPort("after a failed answer"); TrySend(); }
                        return;
                    }
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

        private static string SafeAppId()
        {
            try { return Current.ApplicationInfo.ApplicationId + " / package " + Current.ApplicationInfo.PackageId; } catch (Exception ex) { return "? (" + ex.Message + ")"; }
        }

        private static string Code(int rc) { return rc == 0 ? "0" : rc + " (0x" + rc.ToString("x8") + ")"; }

        /// <summary>Is the launcher's reply port there, as a trusted and as a plain port?</summary>
        private static void CheckReplyPort(string when)
        {
            try
            {
                bool trusted, plain;
                int rt = message_port_check_trusted_remote_port(LauncherId, ReplyPort, out trusted);
                int rp = message_port_check_remote_port(LauncherId, ReplyPort, out plain);
                Note("reply port " + LauncherId + "/" + ReplyPort + " " + when + ": trusted check rc " + Code(rt) + " exist " + trusted +
                     "; plain check rc " + Code(rp) + " exist " + plain);
            }
            catch (Exception ex) { Note("reply port check: " + ex.GetType().Name + " " + ex.Message); }
        }

        /// <summary>One small message through the platform API directly, for its error code.</summary>
        private static void TrySend()
        {
            try
            {
                var m = new Bundle();
                m.AddItem("id", "diag");
                m.AddItem("status", "0");
                m.AddItem("part", "0");
                m.AddItem("parts", "1");
                m.AddItem("body", "");
                IntPtr h = m.SafeBundleHandle.DangerousGetHandle();
                int rt = message_port_send_trusted_message(LauncherId, ReplyPort, h);
                int rp = message_port_send_message(LauncherId, ReplyPort, h);
                Note("direct send: trusted rc " + Code(rt) + ", plain rc " + Code(rp));
            }
            catch (Exception ex) { Note("direct send: " + ex.GetType().Name + " " + ex.Message); }
            // The managed API with other ports than the listening one
            foreach (bool trusted in new[] { true, false })
            {
                try
                {
                    using (var other = new MessagePort("APL_BRIDGE_DIAG_" + (trusted ? "T" : "P"), trusted))
                    {
                        other.Listen();
                        var m = new Bundle();
                        m.AddItem("id", "diag-" + (trusted ? "trusted" : "plain"));
                        m.AddItem("status", "0"); m.AddItem("part", "0"); m.AddItem("parts", "1"); m.AddItem("body", "");
                        other.Send(m, LauncherId, ReplyPort);
                        Note("managed send through a new " + (trusted ? "trusted" : "plain") + " port: ok");
                    }
                }
                catch (Exception ex) { Note("managed send through a new " + (trusted ? "trusted" : "plain") + " port: " + ex.GetType().Name + " " + ex.Message); }
            }
            try
            {
                foreach (bool trusted in new[] { true, false })
                    Note("RemotePort(" + LauncherId + ", " + ReplyPort + ", trusted " + trusted + ").IsRunning: " + new RemotePort(LauncherId, ReplyPort, trusted).IsRunning());
            }
            catch (Exception ex) { Note("RemotePort: " + ex.GetType().Name + " " + ex.Message); }
        }

        private static string path(Exception ex) { return ex.InnerException != null ? ex.InnerException.Message : "-"; }

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
