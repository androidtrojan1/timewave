using System.Collections.Generic;
using TimeWave.Shared;
using UnityEngine;

namespace TimeWave.Client
{
    /// <summary>
    /// Shows the units of the tick the player is looking at. Positions are smoothed between engine ticks;
    /// a large displacement (a jump in time, or the timeline changing under you) snaps instantly.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        sealed class UnitView
        {
            public GameObject Go;
            public Renderer Body;
            public UnitSnapshot Snap;
            public Vector3 Target;
            public Color BaseColor;
            public bool Flashing;
            public bool Seen;
        }

        readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>();
        readonly List<int> _gone = new List<int>();
        TimeWaveGame _game;
        CameraRig _rig;

        void Awake()
        {
            _game = GetComponent<TimeWaveGame>();
            _rig = GetComponent<CameraRig>();
            _game.FrameReceived += OnFrame;
        }

        public bool TryGetUnit(int id, out UnitSnapshot unit, out Vector3 position)
        {
            if (_views.TryGetValue(id, out var v)) { unit = v.Snap; position = v.Go.transform.position; return true; }
            unit = default;
            position = default;
            return false;
        }

        public IEnumerable<KeyValuePair<UnitSnapshot, Vector3>> Units
        {
            get
            {
                foreach (var v in _views.Values)
                    yield return new KeyValuePair<UnitSnapshot, Vector3>(v.Snap, v.Go.transform.position);
            }
        }

        void OnFrame(WorldFrame frame)
        {
            foreach (var v in _views.Values) v.Seen = false;
            foreach (var u in frame.Units)
            {
                if (!_views.TryGetValue(u.Id, out var v))
                {
                    v = Create(u);
                    _views.Add(u.Id, v);
                    v.Go.transform.position = Place(u);
                }
                v.Snap = u;
                v.Seen = true;
                v.Target = Place(u);
                if ((v.Go.transform.position - v.Target).sqrMagnitude > 9f) v.Go.transform.position = v.Target;
                bool flash = (u.Flags & UnitFlags.Firing) != 0 && (Time.frameCount / 3) % 2 == 0;
                if (flash != v.Flashing)
                {
                    v.Flashing = flash;
                    Paint(v.Body, flash ? Color.Lerp(v.BaseColor, Color.white, 0.6f) : v.BaseColor);
                }
            }
            _gone.Clear();
            foreach (var pair in _views) if (!pair.Value.Seen) _gone.Add(pair.Key);
            foreach (var id in _gone)
            {
                Destroy(_views[id].Go);
                _views.Remove(id);
            }
        }

        void Update()
        {
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            foreach (var v in _views.Values)
            {
                var t = v.Go.transform;
                var move = v.Target - t.position;
                if (move.sqrMagnitude > 0.0004f && v.Snap.Type != UnitType.Factory)
                    t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(new Vector3(move.x, 0f, move.z)), k);
                t.position = Vector3.Lerp(t.position, v.Target, k);
            }
        }

        static Vector3 Place(UnitSnapshot u) => TimeWaveGame.ToWorld(u.X, u.Y) + Vector3.up * Height(u.Type) * 0.5f;

        static float Height(UnitType type) => type == UnitType.Factory ? 1.6f : type == UnitType.Tank ? 0.6f : 1.0f;

        UnitView Create(UnitSnapshot u)
        {
            GameObject go;
            switch (u.Type)
            {
                case UnitType.Factory:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.localScale = new Vector3(2.4f, 1.6f, 2.4f);
                    break;
                case UnitType.Tank:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.localScale = new Vector3(0.9f, 0.6f, 1.3f);
                    break;
                default:
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.transform.localScale = new Vector3(0.55f, 0.5f, 0.55f);
                    break;
            }
            go.name = $"{u.Type} {u.Id} (p{u.Owner})";
            go.transform.SetParent(transform, true);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            UseLitMaterial(go);
            var color = TimeWaveGame.PlayerColors[u.Owner % TimeWaveGame.PlayerColors.Length];
            if (u.Type == UnitType.Factory) color = Color.Lerp(color, Color.gray, 0.35f);
            var view = new UnitView { Go = go, Body = go.GetComponent<Renderer>(), BaseColor = color };
            Paint(view.Body, color);
            return view;
        }

        static Material _lit;

        /// <summary>
        /// Primitives get a material from Resources: the built-in default material's shader is stripped
        /// from player builds when no asset references it, which renders everything magenta.
        /// </summary>
        public static void UseLitMaterial(GameObject go)
        {
            if (_lit == null)
            {
                _lit = Resources.Load<Material>("TimeWaveLit");
                if (_lit == null) _lit = new Material(Shader.Find("Standard"));
            }
            go.GetComponent<Renderer>().sharedMaterial = _lit;
        }

        public static void Paint(GameObject go, Color color) => Paint(go.GetComponent<Renderer>(), color);

        public static void Paint(Renderer renderer, Color color)
        {
            var m = renderer.material;
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        }

        void OnGUI()
        {
            if (_rig == null || _rig.Camera == null) return;
            var cam = _rig.Camera;
            foreach (var v in _views.Values)
            {
                if (v.Snap.Hp >= v.Snap.MaxHp) continue;
                var p = cam.WorldToScreenPoint(v.Go.transform.position + Vector3.up * (Height(v.Snap.Type) * 0.5f + 0.4f));
                if (p.z < 0) continue;
                float w = v.Snap.Type == UnitType.Factory ? 50f : 26f;
                var rect = new Rect(p.x - w / 2f, Screen.height - p.y, w, 4f);
                float frac = Mathf.Clamp01(v.Snap.Hp / (float)v.Snap.MaxHp);
                GuiDraw.Rect(rect, new Color(0f, 0f, 0f, 0.7f));
                GuiDraw.Rect(new Rect(rect.x, rect.y, rect.width * frac, rect.height), Color.Lerp(Color.red, Color.green, frac));
            }
        }
    }

    /// <summary>Tiny IMGUI drawing helpers.</summary>
    public static class GuiDraw
    {
        public static void Rect(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Frame(Rect r, Color color, float thickness = 1f)
        {
            Rect(new Rect(r.x, r.y, r.width, thickness), color);
            Rect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            Rect(new Rect(r.x, r.y, thickness, r.height), color);
            Rect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        public static void Label(Rect rect, string text, GUIStyle style, Color color)
        {
            var old = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, text, style);
            GUI.contentColor = old;
        }
    }
}
