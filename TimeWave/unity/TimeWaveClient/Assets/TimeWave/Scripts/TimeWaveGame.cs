using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using TimeWave.Core;
using TimeWave.Shared;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TimeWave.Client
{
    public enum EngineMode
    {
        /// <summary>Connect to a running TimeWave.Server.</summary>
        Connect,
        /// <summary>Connect, and start the locally built server if nothing is listening.</summary>
        ConnectOrLaunch,
        /// <summary>Run the engine inside Unity (no server process).</summary>
        Embedded,
    }

    /// <summary>
    /// Entry point of the client: owns the link to the timeline engine, keeps the latest frame and
    /// builds the rest of the scene (camera, world view, selection, timeline panel) from code.
    /// </summary>
    public sealed class TimeWaveGame : MonoBehaviour
    {
        public EngineMode mode = EngineMode.ConnectOrLaunch;
        public string host = "127.0.0.1";
        public int port = Protocol.DefaultPort;
        public string playerName = "Unity";
        [Tooltip("Player slot to request, -1 for any")]
        public int requestedPlayer = -1;
        [Tooltip("Server executable used by ConnectOrLaunch; relative paths start at the Unity project folder")]
        public string serverPath = "../../src/TimeWave.Server/bin/Release/net10.0/TimeWave.Server.exe";
        [Tooltip("Fall back to the embedded engine if no server can be reached")]
        public bool embeddedFallback = true;

        public ITimeWaveLink Link { get; private set; }
        public Welcome Welcome { get; private set; }
        public WorldFrame Frame { get; private set; }
        public TimelineStats Stats { get; private set; }
        public string Status { get; private set; } = "connecting...";
        public bool Ready => Welcome != null && Frame != null;
        public int Me => Welcome != null ? Welcome.PlayerId : -1;

        /// <summary>Raised on the main thread for each new frame.</summary>
        public event Action<WorldFrame> FrameReceived;

        readonly List<KeyValuePair<float, string>> _notices = new List<KeyValuePair<float, string>>();
        public IReadOnlyList<KeyValuePair<float, string>> Notices => _notices;

        Process _serverProcess;

        public static readonly Color[] PlayerColors = { new Color(0.25f, 0.55f, 1f), new Color(1f, 0.3f, 0.25f), new Color(0.3f, 0.9f, 0.4f), new Color(1f, 0.85f, 0.2f) };

        void Awake()
        {
            Application.runInBackground = true;
            ApplyCommandLine();
            gameObject.AddComponent<CameraRig>();
            gameObject.AddComponent<WorldView>();
            gameObject.AddComponent<SelectionController>();
            gameObject.AddComponent<TimelinePanel>();
            if (TimeWaveAutotest.TryGetOutputDir(out _)) gameObject.AddComponent<TimeWaveAutotest>();
            CreateLight();
        }

        /// <summary>Player builds accept -timewaveHost H, -timewavePort N, -timewavePlayer N, -timewaveName S and -timewaveEmbedded.</summary>
        void ApplyCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                bool hasValue = i + 1 < args.Length;
                switch (args[i])
                {
                    case "-timewaveHost" when hasValue: host = args[++i]; break;
                    case "-timewavePort" when hasValue: int.TryParse(args[++i], out port); break;
                    case "-timewavePlayer" when hasValue: int.TryParse(args[++i], out requestedPlayer); break;
                    case "-timewaveName" when hasValue: playerName = args[++i]; break;
                    case "-timewaveEmbedded": mode = EngineMode.Embedded; break;
                }
            }
        }

        IEnumerator Start()
        {
            if (mode != EngineMode.Embedded)
            {
                if (!TryConnect(1500) && mode == EngineMode.ConnectOrLaunch && LaunchServer())
                {
                    Status = "starting local engine...";
                    for (int attempt = 0; attempt < 20 && Link == null; attempt++)
                    {
                        yield return new WaitForSecondsRealtime(0.5f);
                        TryConnect(500);
                    }
                }
            }
            if (Link == null)
            {
                if (mode != EngineMode.Embedded && !embeddedFallback)
                {
                    Status = $"no engine at {host}:{port}";
                    yield break;
                }
                var embedded = new EmbeddedLink();
                embedded.Send(new Hello { Name = playerName, RequestedPlayer = requestedPlayer });
                Link = embedded;
                Status = "embedded engine";
            }
        }

        bool TryConnect(int timeoutMs)
        {
            var client = new TimeWaveClient();
            try
            {
                client.Connect(host, port, playerName, requestedPlayer, timeoutMs);
                Link = client;
                Status = $"connected to {host}:{port}";
                return true;
            }
            catch (Exception)
            {
                client.Dispose();
                return false;
            }
        }

        bool LaunchServer()
        {
            string path = Path.IsPathRooted(serverPath) ? serverPath : Path.GetFullPath(Path.Combine(Application.dataPath, "..", serverPath));
            if (!File.Exists(path))
            {
                Debug.LogWarning($"TimeWave: server not found at {path}; build it with `dotnet build -c Release`");
                return false;
            }
            try
            {
                _serverProcess = Process.Start(new ProcessStartInfo(path, $"--port {port}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(path),
                });
                Debug.Log($"TimeWave: started local engine {path}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"TimeWave: could not start {path}: {e.Message}");
                return false;
            }
        }

        void Update()
        {
            if (Link == null) return;
            Link.Pump(Time.unscaledDeltaTime);
            while (Link.TryReceive(out var message))
            {
                switch (message)
                {
                    case Welcome w:
                        Welcome = w;
                        GetComponent<CameraRig>().FocusOnMap(w);
                        break;
                    case WorldFrame f:
                        Frame = f;
                        FrameReceived?.Invoke(f);
                        break;
                    case TimelineStats s:
                        Stats = s;
                        break;
                    case Notice n:
                        AddNotice(n.Text);
                        break;
                }
            }
            if (!Link.Connected) Status = "disconnected: " + Link.Error;
            _notices.RemoveAll(n => Time.unscaledTime - n.Key > 6f);
        }

        public void AddNotice(string text) => _notices.Add(new KeyValuePair<float, string>(Time.unscaledTime, text));

        public void Send(Message message) => Link?.Send(message);

        public string Clock(int ticks)
        {
            int tps = Welcome != null ? Welcome.TicksPerSecond : 10;
            int sec = Mathf.Abs(ticks) / tps;
            return $"{(ticks < 0 ? "-" : "+")}{sec / 60:00}:{sec % 60:00}";
        }

        public static Vector3 ToWorld(int x, int y) => new Vector3(x / (float)World.TileSize, 0f, y / (float)World.TileSize);

        public static Vector2Int FromWorld(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x * World.TileSize), Mathf.RoundToInt(p.z * World.TileSize));

        static void CreateLight()
        {
            foreach (var existing in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (existing.type == LightType.Directional) return;
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
        }

        /// <summary>Called by the camera rig once the map size is known.</summary>
        internal void BuildMap(Welcome w)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(w.MapWidth / 2f, -0.55f, w.MapHeight / 2f);
            ground.transform.localScale = new Vector3(w.MapWidth, 1f, w.MapHeight);
            WorldView.UseLitMaterial(ground);
            WorldView.Paint(ground, new Color(0.22f, 0.26f, 0.2f));
        }

        void OnDestroy()
        {
            Link?.Dispose();
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try { _serverProcess.Kill(); } catch { }
            }
        }
    }

    static class TimeWaveBootstrap
    {
        /// <summary>Lets you press Play in any scene: creates the game object if the scene has none.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureGame()
        {
            if (UnityEngine.Object.FindAnyObjectByType<TimeWaveGame>() != null) return;
            new GameObject("TimeWave").AddComponent<TimeWaveGame>();
        }
    }
}
