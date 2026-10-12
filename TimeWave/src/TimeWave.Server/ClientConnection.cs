using System.Collections.Concurrent;
using System.Net.Sockets;
using TimeWave.Shared;

namespace TimeWave.Server;

/// <summary>One connected client. Incoming frames go to the shared inbox; a null message means it disconnected.</summary>
sealed class ClientConnection
{
    readonly TcpClient _tcp;
    readonly NetworkStream _stream;
    readonly ConcurrentQueue<(ClientConnection, Message?)> _inbox;
    readonly object _sendLock = new();
    volatile bool _closed;

    public int Player = -1;
    public string Name = "";

    public ClientConnection(TcpClient tcp, ConcurrentQueue<(ClientConnection, Message?)> inbox)
    {
        _tcp = tcp;
        _tcp.NoDelay = true;
        _stream = tcp.GetStream();
        _inbox = inbox;
    }

    public void Start() => new Thread(ReadLoop) { IsBackground = true, Name = "client reader" }.Start();

    void ReadLoop()
    {
        try
        {
            while (!_closed)
            {
                var m = Protocol.ReadFrame(_stream);
                if (m == null) break;
                _inbox.Enqueue((this, m));
            }
        }
        catch (Exception) { }
        Close();
        _inbox.Enqueue((this, null));
    }

    public void Send(Message message)
    {
        if (_closed) return;
        var bytes = Protocol.Encode(message);
        try
        {
            lock (_sendLock) _stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception) { Close(); }
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        try { _tcp.Close(); } catch { }
    }
}
