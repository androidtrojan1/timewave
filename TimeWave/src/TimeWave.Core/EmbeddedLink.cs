using System.Collections.Generic;
using TimeWave.Shared;

namespace TimeWave.Core
{
    /// <summary>
    /// Runs the engine inside the client process behind the same <see cref="ITimeWaveLink"/> interface
    /// as the TCP client. Handy for debugging in Unity without starting the server.
    /// </summary>
    public sealed class EmbeddedLink : ITimeWaveLink
    {
        readonly EngineHost _host;
        readonly Queue<Message> _outbox = new Queue<Message>();
        int _player = -1;
        float _statsTimer;

        public EmbeddedLink(ScenarioConfig config = null) { _host = new EngineHost(config); }

        public EngineHost Host => _host;
        public bool Connected => true;
        public string Error => null;

        public void Send(Message message)
        {
            if (message is Hello hello)
            {
                _player = _host.Join(hello.Name, hello.RequestedPlayer);
                if (_player >= 0)
                {
                    _outbox.Enqueue(_host.BuildWelcome(_player));
                    _outbox.Enqueue(_host.BuildStats());
                }
                else _outbox.Enqueue(new Notice { Text = "Game is full" });
                return;
            }
            if (_player >= 0) _host.Handle(_player, message);
        }

        public bool TryReceive(out Message message)
        {
            if (_outbox.Count > 0) { message = _outbox.Dequeue(); return true; }
            message = null;
            return false;
        }

        public void Pump(float deltaSeconds)
        {
            if (_player < 0) return;
            _host.Update(deltaSeconds);
            while (_host.TryDequeueNotice(out var player, out var text))
                if (player == _player) _outbox.Enqueue(new Notice { Text = text });
            _outbox.Enqueue(_host.BuildFrame(_player));
            _statsTimer += deltaSeconds;
            if (_statsTimer >= 0.5f)
            {
                _statsTimer = 0f;
                _outbox.Enqueue(_host.BuildStats());
            }
        }

        public void Dispose() { }
    }
}
