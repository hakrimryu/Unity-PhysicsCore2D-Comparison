using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using PhysicsComparisonLab.Battle;

namespace PhysicsComparisonLab.Presentation
{
    /// <summary>
    /// 最大実数の生成、移動、命中、画面範囲と解放をPlayerで検査します。
    /// [KO] 최대 실제 인원의 생성, 이동, 명중, 화면 범위와 해제를 Player에서 검사합니다.
    /// [EN] Checks full-capacity creation, movement, hits, framing, and release in the Player.
    /// </summary>
    public sealed class LargeBattleValidation : MonoBehaviour
    {
        private readonly StringBuilder _report = new StringBuilder();
        private string _output;
        private bool _error;

        public string OutputDirectory { get; set; }

        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "--output");
            _output =
                !string.IsNullOrEmpty(OutputDirectory) ? OutputDirectory :
                at >= 0 ? args[at + 1] :
                Path.Combine(Application.persistentDataPath, "LargeValidation");
            Directory.CreateDirectory(_output);
            Application.logMessageReceived += HandleLog;
            var routine = Validate();
            Exception failure = null;

            while (true)
            {
                bool next;

                try
                {
                    next = routine.MoveNext();
                }
                catch (Exception exception)
                {
                    failure = exception;
                    break;
                }

                if (!next)
                    break;

                yield return routine.Current;
            }

            (routine as IDisposable)?.Dispose();
            Application.logMessageReceived -= HandleLog;
            var passed = failure == null && !_error;
            _report.AppendLine(passed ? "PASS" : "FAIL");

            if (failure != null)
                _report.AppendLine(failure.ToString());

            File.WriteAllText(
                Path.Combine(_output, passed ? "validation-pass.txt" : "validation-failed.txt"),
                _report.ToString());

            if (!Application.isEditor)
                Application.Quit(passed ? 0 : 1);
        }

        /// <summary>
        /// 両backendを順番に動かし、同時に二つの物理世界を走らせません。
        /// [KO] 두 backend를 순서대로 실행하고 두 물리 세계를 동시에 돌리지 않습니다.
        /// [EN] Exercises both backends sequentially without simultaneous physics runs.
        /// </summary>
        private IEnumerator Validate()
        {
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.25f);
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);
            var entry = GetComponent<BattleComparisonEntryPoint>();

            while (entry.IsBusy)
                yield return null;

            foreach (var core in new[] { false, true })
            {
                var name = core ? "Core" : "Legacy";
                File.WriteAllText(Path.Combine(_output, "progress.txt"), name + " creating 1500:1500");
                entry.Configure(1500, 1500, core);

                while (entry.IsBusy)
                    yield return null;

                yield return null;
                Check(entry.Battle != null, "construction completed");
                Check(
                    entry.Battle.Input.Count == 3000 && entry.ViewCount == 3000,
                    "actual 3000 units and visible quads");
                Check(
                    entry.Battle.Physics.BodyCount == 3004 && entry.Battle.Physics.ShapeCount == 3004,
                    "actual native body/shape count");
                Check(
                    GameObject.Find("BattleUnitViews").transform.childCount == 0,
                    "batched display without per-unit objects");
                var field = GetComponent<BattlefieldView>();
                Check(field.CameraSize > 8, "camera zoomed out for maximum deployment");

                foreach (var unit in entry.Battle.Units)
                {
                    var point = Camera.main.WorldToViewportPoint(unit.Position);
                    Check(
                        point.x > 0.01f && point.x < 0.99f && point.y > 0.16f && point.y < 0.84f,
                        "all units inside UI-safe viewport");
                }

                var visibleMin = Camera.main.ViewportToWorldPoint(new Vector3(0, 0, 10));
                var visibleMax = Camera.main.ViewportToWorldPoint(new Vector3(1, 1, 10));
                Check(
                    field.GroundBounds.Contains(Vector3Int.FloorToInt(visibleMin))
                    && field.GroundBounds.Contains(Vector3Int.FloorToInt(visibleMax)),
                    "tiles cover the visible field");
                ScreenCapture.CaptureScreenshot(Path.Combine(_output, name + "-1500-ready.png"));
                yield return new WaitForEndOfFrame();
                entry.StartBattle();
                entry.TogglePause();
                var origin = entry.Battle.Units[0].Position;
                yield return new WaitForSecondsRealtime(0.1f);
                Check(
                    entry.Battle.StepIndex == 0 && entry.Battle.Units[0].Position == origin,
                    "maximum load paused");

                for (var step = 0; step < 120; step++)
                {
                    entry.SingleStep();

                    if (step % 20 == 0)
                        File.WriteAllText(
                            Path.Combine(_output, "progress.txt"),
                            name + " functional step " + entry.Battle.StepIndex + "/120");

                    yield return null;
                }

                Check(entry.Battle.StepIndex == 120, "120 exact steps");
                Check(entry.Battle.Units[0].Position != origin, "native movement");
                Check(entry.Battle.HitCount > 0, "native overlaps produce hits at maximum load");
                Check(entry.Battle.SoldierAlive + entry.Battle.OrcAlive < 3000, "death applied at maximum load");
                Check(
                    entry.Battle.Physics.BodyCount == entry.Battle.SoldierAlive + entry.Battle.OrcAlive + 4,
                    "dead bodies removed");
                ScreenCapture.CaptureScreenshot(Path.Combine(_output, name + "-1500-combat.png"));
                yield return new WaitForEndOfFrame();
                _report.AppendLine(name
                    + " 1500:1500; bodies/shapes=3004 initially; displayed=3000; step="
                    + entry.Battle.StepIndex
                    + "; hits="
                    + entry.Battle.HitCount
                    + "; alive="
                    + (entry.Battle.SoldierAlive
                    + entry.Battle.OrcAlive)
                    + "; camera="
                    + field.CameraSize
                    + "; tiles="
                    + field.GroundBounds.size
                    + "; movement/pause/query/death=PASS");
                GameObject.Find("Soldiers").GetComponent<InputField>().SetTextWithoutNotify("25");
                GameObject.Find("Orcs").GetComponent<InputField>().SetTextWithoutNotify("25");
                entry.ResetBattle();

                while (entry.IsBusy)
                    yield return null;

                yield return null;
                Check(
                    entry.Battle.Physics.BodyCount == 54 && entry.ViewCount == 50,
                    "large run released and small run rebuilt");
                Check(field.CameraSize < 10, "camera zoomed in after reducing counts");
                Check(
                    field.GroundBounds.size.x < 40 && field.GroundBounds.size.y < 40,
                    "terrain shrunk after reducing counts");
                Check(
                    UnityEngine.Object.FindObjectsByType<Rigidbody2D>().Length == (core ? 0 : 54),
                    "inactive legacy bodies absent");
                Check(Unity.U2D.Physics.PhysicsWorld.worldCount == (core ? 2 : 1), "owned Core world count");
                _report.AppendLine(name + " reset to25:25; camera/terrain shrink; body/world cleanup=PASS");
            }

            File.WriteAllText(
                Path.Combine(_output, "progress.txt"),
                "Functional validation complete. No performance benchmark executed.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private void HandleLog(string message, string trace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
                return;

            _error = true;
            _report.AppendLine(message + "\n" + trace);
        }
    }
}
