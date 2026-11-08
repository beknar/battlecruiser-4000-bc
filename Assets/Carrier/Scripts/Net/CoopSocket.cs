using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// A WebSocket to the co-op server, the same for every build: in a browser (the WebGL and
    /// WebGPU builds) through the browser's own WebSocket (Plugins/WebGL/CoopSocket.jslib), and
    /// elsewhere through .NET's ClientWebSocket on a background thread. Messages are whole text
    /// frames; Receive is polled from the main thread.
    /// </summary>
    public abstract class CoopSocket
    {
        public enum State { Connecting, Open, Closed }
        public abstract State Status { get; }
        /// <summary>Why it closed (empty if it closed normally or is still open).</summary>
        public abstract string Error { get; }
        public abstract void Send(string text);
        public abstract bool Receive(out string text);
        public abstract void Close();

        public static CoopSocket Open(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new BrowserSocket(url);
#else
            return new DotNetSocket(url);
#endif
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    sealed class BrowserSocket : CoopSocket
    {
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int CoopWsOpen(string url);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int CoopWsState(int h);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void CoopWsSend(int h, string text);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string CoopWsRecv(int h);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string CoopWsError(int h);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void CoopWsClose(int h);

        readonly int handle;
        public BrowserSocket(string url) { handle = CoopWsOpen(url); }
        public override State Status => (State)Mathf.Clamp(CoopWsState(handle), 0, 2);
        public override string Error => CoopWsError(handle) ?? "";
        public override void Send(string text) => CoopWsSend(handle, text);
        public override bool Receive(out string text) { text = CoopWsRecv(handle); return text != null; }
        public override void Close() => CoopWsClose(handle);
    }
#else
    sealed class DotNetSocket : CoopSocket
    {
        readonly System.Net.WebSockets.ClientWebSocket ws = new System.Net.WebSockets.ClientWebSocket();
        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        readonly System.Collections.Concurrent.BlockingCollection<string> outbox = new System.Collections.Concurrent.BlockingCollection<string>(4000);
        readonly System.Threading.CancellationTokenSource stop = new System.Threading.CancellationTokenSource();
        volatile int state;
        volatile string error = "";

        public DotNetSocket(string url)
        {
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            System.Threading.Tasks.Task.Run(() => Run(url));
        }

        async System.Threading.Tasks.Task Run(string url)
        {
            try
            {
                await ws.ConnectAsync(new Uri(url), stop.Token);
                state = (int)State.Open;
                var sender = new System.Threading.Thread(SendLoop) { IsBackground = true, Name = "co-op send" };
                sender.Start();
                var buffer = new byte[64 * 1024];
                // a message can arrive in pieces: keep the bytes, and decode it whole (a letter
                // can be split between pieces)
                var bytes = new System.IO.MemoryStream();
                while (ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token);
                    if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        if (r.CloseStatus != System.Net.WebSockets.WebSocketCloseStatus.NormalClosure) error = r.CloseStatusDescription ?? "closed by the server";
                        break;
                    }
                    bytes.Write(buffer, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    inbox.Enqueue(System.Text.Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length));
                    bytes.SetLength(0);
                }
            }
            catch (Exception e)
            {
                if (!stop.IsCancellationRequested) error = Explain(e);
            }
            state = (int)State.Closed;
            outbox.CompleteAdding();
        }

        /// <summary>A failure in words a player can act on.</summary>
        static string Explain(Exception e)
        {
            string m = (e.InnerException != null ? e.InnerException.Message + " " : "") + e.Message;
            if (m.Contains("403")) return "the server refused this address (it isn't on the server's allow-list)";
            if (m.Contains("refused") || m.Contains("actively")) return "nothing is listening at that address and port";
            if (m.Contains("No such host") || m.Contains("resolve") || m.Contains("NameResolution")) return "that server address can't be found";
            if (m.Contains("timed out") || m.Contains("timeout")) return "the server didn't answer";
            return m.Trim();
        }

        void SendLoop()
        {
            try
            {
                foreach (var s in outbox.GetConsumingEnumerable(stop.Token))
                {
                    if (ws.State != System.Net.WebSockets.WebSocketState.Open) break;
                    ws.SendAsync(new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(s)), System.Net.WebSockets.WebSocketMessageType.Text, true, stop.Token).Wait();
                }
            }
            catch { }
        }

        public override State Status => (State)state;
        public override string Error => error;
        public override void Send(string text)
        {
            if (state != (int)State.Open) return;
            // (the connection can close between the check and the add: then it's dropped)
            try { outbox.TryAdd(text); } catch (InvalidOperationException) { }
        }
        public override bool Receive(out string text) => inbox.TryDequeue(out text);

        public override void Close()
        {
            if (state == (int)State.Closed) return;
            state = (int)State.Closed;
            try
            {
                outbox.CompleteAdding();
                // say goodbye if we can (don't wait long), then stop
                ws.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", System.Threading.CancellationToken.None).Wait(300);
            }
            catch { }
            stop.Cancel();
        }
    }
#endif
}
