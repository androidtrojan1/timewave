using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace TimeWave.Shared
{
    /// <summary>
    /// A connection to a timeline engine. Implemented over TCP by <see cref="TimeWaveClient"/>
    /// and in-process by TimeWave.Core's EmbeddedLink, so a game client works with either.
    /// </summary>
    public interface ITimeWaveLink : IDisposable
    {
        bool Connected { get; }
        string Error { get; }
        void Send(Message message);
        /// <summary>Call from the game thread; never blocks.</summary>
        bool TryReceive(out Message message);
        /// <summary>Advances an in-process engine; a no-op for network links.</summary>
        void Pump(float deltaSeconds);
    }

    /// <summary>TCP client: a background thread reads frames into a queue the game thread drains.</summary>
    public sealed class TimeWaveClient : ITimeWaveLink
    {
        readonly ConcurrentQueue<Message> _inbox = new ConcurrentQueue<Message>();
        readonly object _sendLock = new object();
        TcpClient _tcp;
        NetworkStream _stream;
        Thread _reader;
        volatile bool _connected;
        volatile string _error;

        public bool Connected => _connected;
        public string Error => _error;

        public void Connect(string host, int port, string playerName, int requestedPlayer = -1, int timeoutMs = 3000)
        {
            _tcp = new TcpClient { NoDelay = true };
            var connect = _tcp.ConnectAsync(host, port);
            if (!connect.Wait(timeoutMs) || !_tcp.Connected)
                throw new SocketException((int)SocketError.TimedOut);
            _stream = _tcp.GetStream();
            _connected = true;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "TimeWaveClient reader" };
            _reader.Start();
            Send(new Hello { Name = playerName, RequestedPlayer = requestedPlayer });
        }

        public void Send(Message message)
        {
            if (!_connected) return;
            var bytes = Protocol.Encode(message);
            try
            {
                lock (_sendLock) _stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        public bool TryReceive(out Message message) => _inbox.TryDequeue(out message);

        public void Pump(float deltaSeconds) { }

        void ReadLoop()
        {
            try
            {
                while (_connected)
                {
                    var m = Protocol.ReadFrame(_stream);
                    if (m == null) { Fail("engine closed the connection"); return; }
                    _inbox.Enqueue(m);
                }
            }
            catch (Exception e)
            {
                if (_connected) Fail(e.Message);
            }
        }

        void Fail(string error)
        {
            if (_error == null) _error = error;
            _connected = false;
        }

        public void Dispose()
        {
            _connected = false;
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Dispose(); } catch { }
        }
    }
}
