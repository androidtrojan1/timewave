using TimeWave.Shared;
using UnityEngine;

namespace TimeWave.Client
{
    /// <summary>
    /// Achron-style time panel drawn with IMGUI: chronoenergy, time-rate buttons, the time window with
    /// activity bands, and markers for the present, the player, other players and every timewave.
    /// Clicking or dragging on the window jumps there.
    /// </summary>
    public sealed class TimelinePanel : MonoBehaviour
    {
        const float PanelHeight = 214f;
        const float LabelWidth = 112f;
        static readonly string[] RowNames = { "Units", "Under attack", "Damage dealt", "Created", "Orders" };

        TimeWaveGame _game;
        GUIStyle _small, _clock, _rowLabel, _center;
        int _lastJumpTick = int.MinValue;

        Rect PanelRect => new Rect(10f, Screen.height - PanelHeight - 10f, Screen.width - 20f, PanelHeight);

        /// <summary>True if a screen point (origin bottom-left) is over the panel.</summary>
        public bool ContainsScreenPoint(Vector2 p) => PanelRect.Contains(new Vector2(p.x, Screen.height - p.y));

        void Awake() => _game = GetComponent<TimeWaveGame>();

        void EnsureStyles()
        {
            if (_small != null) return;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            _small.normal.textColor = Color.white;
            _center = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter };
            _rowLabel = new GUIStyle(_small) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            _rowLabel.normal.textColor = new Color(0.55f, 0.75f, 1f);
            _clock = new GUIStyle(_small) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _clock.normal.textColor = new Color(1f, 0.85f, 0.25f);
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawOverlay();
            if (!_game.Ready) return;

            var f = _game.Frame;
            var panel = PanelRect;
            GuiDraw.Rect(panel, new Color(0.03f, 0.05f, 0.1f, 0.88f));
            GuiDraw.Frame(panel, new Color(0.25f, 0.45f, 0.8f, 0.9f), 2f);

            // Chronoenergy
            var ce = new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, 14f);
            float frac = f.ChronoMax > 0 ? Mathf.Clamp01(f.Chrono / f.ChronoMax) : 0f;
            GuiDraw.Rect(ce, new Color(0.05f, 0.1f, 0.25f));
            GuiDraw.Rect(new Rect(ce.x, ce.y, ce.width * frac, ce.height), frac < 0.1f ? new Color(1f, 0.3f, 0.2f) : new Color(0.15f, 0.45f, 1f));
            GuiDraw.Label(ce, $"CHRONOENERGY  {f.Chrono:F0} / {f.ChronoMax:F0}", _center, Color.white);

            // Rate buttons and clock
            float bx = panel.x + 10f, by = panel.y + 28f;
            RateButton(new Rect(bx, by, 46, 24), "||", TimeRate.Paused, f.Rate);
            RateButton(new Rect(bx + 50, by, 46, 24), "0.5x", TimeRate.Half, f.Rate);
            RateButton(new Rect(bx + 100, by, 46, 24), "1x", TimeRate.Normal, f.Rate);
            RateButton(new Rect(bx + 150, by, 46, 24), "2x", TimeRate.Fast, f.Rate);
            bool atPresent = f.Players[_game.Me].AtPresent;
            GUI.color = atPresent ? new Color(0.5f, 0.6f, 0.8f) : Color.white;
            if (GUI.Button(new Rect(bx + 210, by, 90, 24), "PRESENT")) _game.Send(new JumpToPresent());
            GUI.color = Color.white;
            string where = atPresent ? "at present" : f.PlayerTick < f.PresentTick ? "in the PAST" : "in the FUTURE";
            GuiDraw.Label(new Rect(bx + 310, by, 300, 24), $"you are {where}   |   game time {_game.Clock(f.PresentTick).Substring(1)}", _small, Color.white);
            GuiDraw.Label(new Rect(panel.xMax - 130f, by, 120f, 24f), _game.Clock(f.PlayerTick - f.PresentTick), _clock, _clock.normal.textColor);

            // Time window
            var area = new Rect(panel.x + LabelWidth + 10f, panel.y + 60f, panel.width - LabelWidth - 20f, panel.height - 68f);
            DrawWindow(area, f);
            HandleInput(area, f);
        }

        void RateButton(Rect r, string label, TimeRate rate, TimeRate current)
        {
            GUI.color = rate == current ? new Color(0.45f, 0.85f, 1f) : Color.white;
            if (GUI.Button(r, label)) _game.Send(new SetRate { Rate = rate });
            GUI.color = Color.white;
        }

        float X(Rect area, WorldFrame f, int tick) =>
            area.x + area.width * Mathf.Clamp01((tick - f.WindowStart) / (float)Mathf.Max(1, f.WindowEnd - f.WindowStart));

        int TickAt(Rect area, WorldFrame f, float x) =>
            f.WindowStart + Mathf.RoundToInt((x - area.x) / area.width * (f.WindowEnd - f.WindowStart));

        void DrawWindow(Rect area, WorldFrame f)
        {
            int tps = _game.Welcome.TicksPerSecond;
            const float rulerHeight = 16f;
            float rowHeight = (area.height - rulerHeight) / RowNames.Length;
            var bands = new Rect(area.x, area.y + rulerHeight, area.width, area.height - rulerHeight);

            // past is tinted blue, the future stays dark (as in Achron)
            float px = X(area, f, f.PresentTick);
            GuiDraw.Rect(new Rect(bands.x, bands.y, px - bands.x, bands.height), new Color(0.05f, 0.1f, 0.35f, 0.9f));
            GuiDraw.Rect(new Rect(px, bands.y, bands.xMax - px, bands.height), new Color(0.02f, 0.02f, 0.06f, 0.9f));

            for (int r = 0; r < RowNames.Length; r++)
            {
                var row = new Rect(bands.x, bands.y + r * rowHeight, bands.width, rowHeight);
                GuiDraw.Label(new Rect(area.x - LabelWidth - 6f, row.y, LabelWidth, rowHeight), RowNames[r], _rowLabel, _rowLabel.normal.textColor);
                GuiDraw.Rect(new Rect(row.x, row.yMax - 1f, row.width, 1f), new Color(0.2f, 0.3f, 0.5f, 0.5f));
            }

            // Ruler: one mark per minute relative to the present
            int minute = 60 * tps;
            for (int t = f.PresentTick - (f.PresentTick - f.WindowStart) / minute * minute; t <= f.WindowEnd; t += minute)
            {
                float x = X(area, f, t);
                int rel = (t - f.PresentTick) / minute;
                string label = rel == 0 ? "present" : (rel > 0 ? "+" : "") + rel + " min";
                GuiDraw.Rect(new Rect(x, area.y + rulerHeight - 4f, 1f, 4f), Color.white);
                GuiDraw.Label(new Rect(x - 30f, area.y - 2f, 60f, 14f), label, _center, Color.white);
            }

            DrawStats(bands, rowHeight, f);

            // Markers: background waves, other players, the present, and you
            foreach (var w in f.Waves)
            {
                if (w.Kind != WaveKind.Background) continue;
                float x = X(area, f, w.Tick);
                GuiDraw.Rect(new Rect(x, bands.y, 1f, bands.height), new Color(1f, 0.9f, 0.3f, 0.55f));
                GuiDraw.Rect(new Rect(x - 3f, bands.y - 4f, 7f, 4f), new Color(1f, 0.9f, 0.3f, 0.9f));
            }
            foreach (var p in f.Players)
            {
                if (p.Player == _game.Me || p.AtPresent) continue;
                float x = X(area, f, p.Tick);
                GuiDraw.Rect(new Rect(x - 1f, bands.y, 2f, bands.height), new Color(0.8f, 0.4f, 1f, 0.9f));
            }
            GuiDraw.Rect(new Rect(px - 1.5f, area.y + rulerHeight - 2f, 3f, bands.height + 2f), new Color(0.3f, 0.6f, 1f));
            if (!f.Players[_game.Me].AtPresent)
            {
                float mx = X(area, f, f.PlayerTick);
                GuiDraw.Rect(new Rect(mx - 1f, area.y + rulerHeight - 2f, 2f, bands.height + 2f), new Color(1f, 0.15f, 0.15f));
            }
            GuiDraw.Frame(bands, new Color(0.25f, 0.45f, 0.8f, 0.6f));

            // Hover read-out
            var mouse = Event.current.mousePosition;
            if (area.Contains(mouse))
            {
                int tick = TickAt(area, f, mouse.x);
                GuiDraw.Rect(new Rect(mouse.x, bands.y, 1f, bands.height), new Color(1f, 1f, 1f, 0.35f));
                GuiDraw.Label(new Rect(mouse.x + 6f, area.yMax - 16f, 70f, 14f), _game.Clock(tick - f.PresentTick), _small, Color.white);
            }
        }

        void DrawStats(Rect bands, float rowHeight, WorldFrame f)
        {
            var s = _game.Stats;
            if (s == null || s.PlayerCount == 0) return;
            int me = _game.Me, players = s.PlayerCount;
            int enemy = players > 1 ? (me == 0 ? 1 : 0) : me;
            var area = new Rect(bands.x, bands.y - 16f, bands.width, bands.height + 16f);

            float maxUnits = 1f, maxTaken = 1f, maxDealt = 1f;
            for (int b = 0; b < s.BucketCount; b++)
            {
                for (int p = 0; p < players; p++) maxUnits = Mathf.Max(maxUnits, s.UnitCount[b * players + p]);
                maxTaken = Mathf.Max(maxTaken, s.DamageTaken[b * players + me]);
                maxDealt = Mathf.Max(maxDealt, s.DamageDealt[b * players + me]);
            }

            var mine = TimeWaveGame.PlayerColors[me % TimeWaveGame.PlayerColors.Length];
            var theirs = TimeWaveGame.PlayerColors[enemy % TimeWaveGame.PlayerColors.Length];
            for (int b = 0; b < s.BucketCount; b++)
            {
                int t0 = s.FirstTick + b * s.BucketTicks;
                if (t0 + s.BucketTicks <= f.WindowStart || t0 >= f.WindowEnd) continue;
                float x0 = X(area, f, Mathf.Max(t0, f.WindowStart)), x1 = X(area, f, Mathf.Min(t0 + s.BucketTicks, f.WindowEnd));
                float w = Mathf.Max(1f, x1 - x0 - 0.5f);
                int i = b * players;

                float y = bands.y;
                float hm = rowHeight * s.UnitCount[i + me] / maxUnits, he = rowHeight * s.UnitCount[i + enemy] / maxUnits;
                if (enemy != me) GuiDraw.Rect(new Rect(x0, y + rowHeight - he, w, he), new Color(theirs.r, theirs.g, theirs.b, 0.45f));
                GuiDraw.Rect(new Rect(x0, y + rowHeight - hm, w * 0.6f, hm), new Color(mine.r, mine.g, mine.b, 0.85f));

                y += rowHeight;
                float a = s.DamageTaken[i + me] / maxTaken;
                if (a > 0) GuiDraw.Rect(new Rect(x0, y + 1f, w, rowHeight - 2f), new Color(1f, 0.15f, 0.1f, 0.2f + 0.8f * a));

                y += rowHeight;
                a = s.DamageDealt[i + me] / maxDealt;
                if (a > 0) GuiDraw.Rect(new Rect(x0, y + 1f, w, rowHeight - 2f), new Color(1f, 0.6f, 0.1f, 0.2f + 0.8f * a));

                y += rowHeight;
                if (s.Created[i + me] > 0) GuiDraw.Rect(new Rect(x0, y + 2f, w, rowHeight - 4f), new Color(0.3f, 1f, 0.4f, 0.75f));

                y += rowHeight;
                if (s.Commands[i + me] > 0) GuiDraw.Rect(new Rect(x0, y + 2f, Mathf.Max(2f, w * 0.5f), rowHeight / 2f - 2f), Color.white);
                if (enemy != me && s.Commands[i + enemy] > 0) GuiDraw.Rect(new Rect(x0, y + rowHeight / 2f, Mathf.Max(2f, w * 0.5f), rowHeight / 2f - 2f), theirs);
            }
        }

        void HandleInput(Rect area, WorldFrame f)
        {
            var e = Event.current;
            if (e.button != 0 || !area.Contains(e.mousePosition)) return;
            if (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)
            {
                int tick = TickAt(area, f, e.mousePosition.x);
                if (e.type == EventType.MouseDown || Mathf.Abs(tick - _lastJumpTick) >= 5)
                {
                    _lastJumpTick = tick;
                    _game.Send(new JumpTo { Tick = tick });
                }
                e.Use();
            }
        }

        void DrawOverlay()
        {
            GuiDraw.Rect(new Rect(10, 10, 330, 108), new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(18, 14, 320, 104),
                "LMB drag: select    RMB: move / attack\n" +
                "A + click: attack-move    S: stop\n" +
                "Click the timeline: travel in time\n" +
                "1-4: pause / 0.5x / 1x / 2x    Space: present\n" +
                "Arrows, MMB drag: pan    Wheel: zoom\n" +
                _game.Status, _small);

            if (!InputShim.Available)
            {
                GuiDraw.Rect(new Rect(10, 122, 330, 40), new Color(0.5f, 0f, 0f, 0.8f));
                GUI.Label(new Rect(18, 124, 320, 36), "No input backend: set Player Settings > Active Input\nHandling to 'Input Manager (Old)' or 'Both'", _small);
            }

            float y = 14f;
            foreach (var n in _game.Notices)
            {
                GuiDraw.Rect(new Rect(Screen.width / 2f - 220f, y, 440f, 22f), new Color(0f, 0f, 0f, 0.6f));
                GUI.Label(new Rect(Screen.width / 2f - 214f, y, 428f, 22f), n.Value, _center);
                y += 26f;
            }
        }
    }
}
