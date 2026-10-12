using System;
using System.Collections;
using System.IO;
using System.Linq;
using TimeWave.Shared;
using UnityEngine;

namespace TimeWave.Client
{
    /// <summary>
    /// Scripted run used to check a player build without a human:
    ///   TimeWaveClient.exe -timewaveAutotest &lt;output dir&gt;
    /// Travels to the future and the past, issues an order in the past, returns to the present,
    /// takes a screenshot at every step, logs "AUTOTEST ..." lines and quits.
    /// </summary>
    public sealed class TimeWaveAutotest : MonoBehaviour
    {
        string _outDir;
        TimeWaveGame _game;
        int _failures;

        public static bool TryGetOutputDir(out string dir)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-timewaveAutotest");
            dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            return i >= 0;
        }

        void Awake()
        {
            _game = GetComponent<TimeWaveGame>();
            TryGetOutputDir(out _outDir);
            _outDir = string.IsNullOrEmpty(_outDir) ? Path.Combine(Application.persistentDataPath, "autotest") : _outDir;
            Directory.CreateDirectory(_outDir);
        }

        IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (!_game.Ready && Time.realtimeSinceStartup < deadline) yield return null;
            Check(_game.Ready, "connected and receiving frames (" + _game.Status + ")");
            if (!_game.Ready) { Finish(); yield break; }

            yield return new WaitForSecondsRealtime(4f);
            Report("present");
            yield return Shot("1_present");

            _game.Send(new SetRate { Rate = TimeRate.Fast });
            yield return new WaitForSecondsRealtime(4f);
            Check(_game.Frame.PlayerTick > _game.Frame.PresentTick, "fast-forward into the future");
            Report("future");
            yield return Shot("2_future");

            _game.Send(new SetRate { Rate = TimeRate.Normal });
            _game.Send(new JumpTo { Tick = Mathf.Max(1, _game.Frame.PresentTick - 60) });
            yield return new WaitForSecondsRealtime(1.5f);
            Check(_game.Frame.PlayerTick < _game.Frame.PresentTick, "jump into the past");
            Report("past");
            yield return Shot("3_past");

            var mine = _game.Frame.Units.Where(u => u.Owner == _game.Me && u.Type != UnitType.Factory).Select(u => u.Id).ToArray();
            float chrono = _game.Frame.Chrono;
            _game.Send(new IssueCommand { Order = OrderType.Move, UnitIds = mine, TargetX = 3000, TargetY = 3000 });
            yield return new WaitForSecondsRealtime(3f);
            Check(_game.Frame.Chrono < chrono, "order in the past cost chronoenergy");
            Report("past after order");
            yield return Shot("4_past_after_order");

            _game.Send(new JumpToPresent());
            yield return new WaitForSecondsRealtime(1f);
            Check(_game.Frame.Players[_game.Me].AtPresent, "back at the present");
            Report("present right after returning");
            float before = DistanceToOrderPoint();
            yield return Shot("5_present_before_wave");

            float waitStart = Time.realtimeSinceStartup;
            while (DistanceToOrderPoint() > before - 2f && Time.realtimeSinceStartup - waitStart < 60f) yield return null;
            float waited = Time.realtimeSinceStartup - waitStart;
            yield return new WaitForSecondsRealtime(2f);
            Report("present after waves passed");
            float after = DistanceToOrderPoint();
            Check(after < before - 2f, $"order from the past reached the present after {waited:F1} s (avg distance to target {before:F1} -> {after:F1} tiles)");
            yield return Shot("6_present_after_wave");
            Finish();
        }

        /// <summary>Average distance in tiles from the player's mobile units to the point ordered in the past (3, 3).</summary>
        float DistanceToOrderPoint()
        {
            var mine = _game.Frame.Units.Where(u => u.Owner == _game.Me && u.Type != UnitType.Factory).ToArray();
            if (mine.Length == 0) return 0f;
            return mine.Average(u => Mathf.Sqrt(Mathf.Pow(u.X - 3000, 2) + Mathf.Pow(u.Y - 3000, 2))) / World.TileSize;
        }

        void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            Debug.Log($"AUTOTEST {(ok ? "ok  " : "FAIL")} {what}");
        }

        void Report(string label)
        {
            var f = _game.Frame;
            int mine = f.Units.Count(u => u.Owner == _game.Me), enemy = f.Units.Length - mine;
            var waves = string.Join(" ", f.Waves.Select(w => $"{w.Kind.ToString()[0]}{_game.Clock(w.Tick - f.PresentTick)}"));
            Debug.Log($"AUTOTEST [{label}] you {_game.Clock(f.PlayerTick - f.PresentTick)} present {f.PresentTick} CE {f.Chrono:F0} units {mine}/{enemy} waves {waves} stats {(_game.Stats != null ? _game.Stats.BucketCount : 0)} buckets");
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(_outDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            Debug.Log("AUTOTEST screenshot " + path);
        }

        void Finish()
        {
            Debug.Log(_failures == 0 ? "AUTOTEST DONE OK" : $"AUTOTEST DONE FAILED ({_failures})");
            Application.Quit(_failures == 0 ? 0 : 1);
        }
    }
}
