using System.Collections.Generic;
using System.Linq;
using TimeWave.Shared;
using UnityEngine;

namespace TimeWave.Client
{
    /// <summary>
    /// Box/click selection and orders. Orders always apply at the time the player is viewing:
    /// in the past they rewrite history (and cost chronoenergy), in the present they are plain RTS orders.
    /// Unit ids are deterministic across timelines, so a selection survives jumps in time.
    /// </summary>
    public sealed class SelectionController : MonoBehaviour
    {
        const float ClickRadius = 22f;
        readonly HashSet<int> _selected = new HashSet<int>();
        TimeWaveGame _game;
        WorldView _view;
        CameraRig _rig;
        TimelinePanel _panel;
        Vector2 _dragStart;
        bool _dragging;
        bool _attackMoveArmed;

        public IReadOnlyCollection<int> Selected => _selected;

        // Start, not Awake: sibling components are added one by one in TimeWaveGame.Awake.
        void Start()
        {
            _game = GetComponent<TimeWaveGame>();
            _view = GetComponent<WorldView>();
            _rig = GetComponent<CameraRig>();
            _panel = GetComponent<TimelinePanel>();
        }

        void Update()
        {
            if (_game == null || !_game.Ready) return;
            var mouse = InputShim.MousePosition;
            bool overUi = _panel.ContainsScreenPoint(mouse);

            if (InputShim.KeyDown(KeyCode.Space)) _game.Send(new JumpToPresent());
            if (InputShim.KeyDown(KeyCode.Alpha1)) _game.Send(new SetRate { Rate = TimeRate.Paused });
            if (InputShim.KeyDown(KeyCode.Alpha2)) _game.Send(new SetRate { Rate = TimeRate.Half });
            if (InputShim.KeyDown(KeyCode.Alpha3)) _game.Send(new SetRate { Rate = TimeRate.Normal });
            if (InputShim.KeyDown(KeyCode.Alpha4)) _game.Send(new SetRate { Rate = TimeRate.Fast });
            if (InputShim.KeyDown(KeyCode.A) && _selected.Count > 0) _attackMoveArmed = true;
            if (InputShim.KeyDown(KeyCode.S) && _selected.Count > 0) Order(OrderType.Stop, Vector3.zero, 0);
            if (InputShim.KeyDown(KeyCode.Escape)) { _attackMoveArmed = false; _selected.Clear(); }

            if (InputShim.MouseDown(0) && !overUi)
            {
                if (_attackMoveArmed)
                {
                    _attackMoveArmed = false;
                    if (GroundPoint(mouse, out var p)) Order(OrderType.AttackMove, p, 0);
                }
                else
                {
                    _dragStart = mouse;
                    _dragging = true;
                }
            }
            if (_dragging && InputShim.MouseUp(0))
            {
                _dragging = false;
                Select(_dragStart, mouse, InputShim.KeyHeld(KeyCode.LeftShift));
            }

            if (InputShim.MouseDown(1) && !overUi && _selected.Count > 0)
            {
                int enemy = Pick(mouse, u => u.Owner != _game.Me);
                if (enemy != 0)
                    Order(OrderType.Attack, Vector3.zero, enemy);
                else if (GroundPoint(mouse, out var p))
                    Order(_attackMoveArmed ? OrderType.AttackMove : OrderType.Move, p, 0);
                _attackMoveArmed = false;
            }
        }

        void Order(OrderType order, Vector3 point, int targetId)
        {
            var target = TimeWaveGame.FromWorld(point);
            _game.Send(new IssueCommand { Order = order, UnitIds = _selected.ToArray(), TargetX = target.x, TargetY = target.y, TargetId = targetId });
        }

        void Select(Vector2 a, Vector2 b, bool add)
        {
            if (!add) _selected.Clear();
            var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            if (rect.width < 6f && rect.height < 6f)
            {
                int id = Pick(b, u => u.Owner == _game.Me && u.Type != UnitType.Factory);
                if (id != 0) _selected.Add(id);
                return;
            }
            foreach (var pair in _view.Units)
            {
                var u = pair.Key;
                if (u.Owner != _game.Me || u.Type == UnitType.Factory) continue;
                var s = _rig.Camera.WorldToScreenPoint(pair.Value);
                if (s.z > 0 && rect.Contains(new Vector2(s.x, s.y))) _selected.Add(u.Id);
            }
        }

        /// <summary>Nearest unit to the cursor on screen that passes <paramref name="filter"/>, or 0.</summary>
        int Pick(Vector2 mouse, System.Func<UnitSnapshot, bool> filter)
        {
            int best = 0;
            float bestDist = ClickRadius;
            foreach (var pair in _view.Units)
            {
                if (!filter(pair.Key)) continue;
                var s = _rig.Camera.WorldToScreenPoint(pair.Value);
                if (s.z <= 0) continue;
                float d = Vector2.Distance(new Vector2(s.x, s.y), mouse);
                if (pair.Key.Type == UnitType.Factory) d -= 25f;
                if (d < bestDist) { bestDist = d; best = pair.Key.Id; }
            }
            return best;
        }

        bool GroundPoint(Vector2 mouse, out Vector3 point)
        {
            var ray = _rig.Camera.ScreenPointToRay(mouse);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float enter)) { point = ray.GetPoint(enter); return true; }
            point = default;
            return false;
        }

        void OnGUI()
        {
            if (_game == null || !_game.Ready) return;
            var cam = _rig.Camera;
            foreach (var id in _selected)
            {
                if (!_view.TryGetUnit(id, out var u, out var pos)) continue;
                var s = cam.WorldToScreenPoint(pos);
                if (s.z <= 0) continue;
                float size = u.Type == UnitType.Tank ? 30f : 22f;
                GuiDraw.Frame(new Rect(s.x - size / 2f, Screen.height - s.y - size / 2f, size, size), new Color(0.4f, 1f, 0.4f, 0.9f));
            }
            if (_dragging)
            {
                var m = InputShim.MousePosition;
                var r = Rect.MinMaxRect(Mathf.Min(_dragStart.x, m.x), Screen.height - Mathf.Max(_dragStart.y, m.y),
                                        Mathf.Max(_dragStart.x, m.x), Screen.height - Mathf.Min(_dragStart.y, m.y));
                GuiDraw.Rect(r, new Color(0.4f, 1f, 0.4f, 0.12f));
                GuiDraw.Frame(r, new Color(0.4f, 1f, 0.4f, 0.8f));
            }
            if (_attackMoveArmed)
            {
                var m = InputShim.MousePosition;
                GUI.Label(new Rect(m.x + 16, Screen.height - m.y - 8, 220, 22), "Attack-move: click a point");
            }
        }
    }
}
