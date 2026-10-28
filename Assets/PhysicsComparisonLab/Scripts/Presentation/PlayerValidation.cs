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
    /// ローカルPlayerの操作経路と画面を再現可能な記録に残します。
    /// [KO] 로컬 Player의 조작 경로와 화면을 재현 가능한 기록으로 남깁니다.
    /// [EN] Records reproducible control-path and screen checks in the local Player.
    /// </summary>
    public sealed class PlayerValidation : MonoBehaviour
    {
        private readonly StringBuilder _report = new StringBuilder();
        private string _output;
        private bool _errorLogged;

        private IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--output");
            _output = index >= 0 && index + 1 < args.Length
                ? args[index + 1]
                : Path.Combine(Application.persistentDataPath, "Validation");
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
            var success = failure == null && !_errorLogged;
            _report.AppendLine(success ? "PASS" : "FAIL");

            if (failure != null)
                _report.AppendLine(failure.ToString());

            File.WriteAllText(
                Path.Combine(_output, success ? "validation-pass.txt" : "validation-failed.txt"),
                _report.ToString());

            if (!Application.isEditor)
                Application.Quit(success ? 0 : 1);
        }

        private IEnumerator Validate()
        {
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.25f);
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return null;
            var entry = GetComponent<BattleComparisonEntryPoint>();

            while (entry.IsBusy)
                yield return null;

            _report.AppendLine("Unity="
                + Application.unityVersion
                + "; Editor="
                + Application.isEditor
                + "; resolution="
                + Screen.width
                + "x"
                + Screen.height);

            foreach (var core in new[] { false, true })
            {
                foreach (var pair in new[] { (1, 1), (1, 2), (25, 25), (100, 100), (250, 250) })
                {
                    entry.Configure(pair.Item1, pair.Item2, core);

                    while (entry.IsBusy)
                        yield return null;

                    yield return null;
                    Check(entry.Battle.Physics.BodyCount == pair.Item1 + pair.Item2 + 4, "body count");
                    Check(entry.ViewCount == pair.Item1 + pair.Item2, "view count");

                    if (pair.Item1 == 25)
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(_output, (core ? "core" : "legacy") + "-ready-1920.png"));
                        yield return new WaitForEndOfFrame();
                        yield return null;
                    }

                    GameObject.Find("Start").GetComponent<Button>().onClick.Invoke();
                    GameObject.Find("Pause").GetComponent<Button>().onClick.Invoke();
                    Check(entry.IsPaused && !entry.IsReady, "start/pause buttons");
                    var step = entry.Battle.StepIndex;
                    var position = entry.Battle.Units[0].Position;
                    yield return new WaitForSecondsRealtime(0.1f);
                    Check(
                        entry.Battle.StepIndex == step && entry.Battle.Units[0].Position == position,
                        "pause stops automatic physics");
                    GameObject.Find("Step").GetComponent<Button>().onClick.Invoke();
                    Check(entry.Battle.StepIndex == step + 1, "single step button");
                    GameObject.Find("Speed").GetComponent<Button>().onClick.Invoke();
                    Check(entry.Speed == 2, "speed button");
                    GameObject.Find("Speed").GetComponent<Button>().onClick.Invoke();

                    for (var i = 0; i < 1900 && !entry.IsFinished; i++)
                    {
                        entry.SingleStep();

                        if (pair.Item1 == 25 && entry.Battle.StepIndex == 90)
                        {
                            ScreenCapture.CaptureScreenshot(Path.Combine(_output, (core ? "core" : "legacy") + "-combat-1920.png"));
                            yield return new WaitForEndOfFrame();
                        }

                        if (i % 60 == 0)
                            yield return null;
                    }

                    Check(entry.IsFinished, "result and finishing");
                    yield return null;
                    Check(
                        entry.ViewCount == entry.Battle.SoldierAlive + entry.Battle.OrcAlive,
                        "death view cleanup");
                    _report.AppendLine((core ? "Core" : "Legacy")
                        + " "
                        + pair.Item1
                        + ":"
                        + pair.Item2
                        + " result="
                        + entry.Battle.Result
                        + " steps="
                        + entry.Battle.StepIndex
                        + " alive="
                        + entry.ViewCount
                        + " bodies="
                        + entry.Battle.Physics.BodyCount);

                    if (pair.Item1 == 25)
                    {
                        yield return new WaitForSecondsRealtime(0.3f);
                        ScreenCapture.CaptureScreenshot(Path.Combine(_output, (core ? "core" : "legacy") + "-result-1920.png"));
                        yield return new WaitForEndOfFrame();
                        yield return null;
                    }

                    GameObject.Find("Reset").GetComponent<Button>().onClick.Invoke();

                    while (entry.IsBusy)
                        yield return null;

                    Check(entry.IsReady && entry.Battle.StepIndex == 0, "reset button");
                }
            }

            for (var i = 0; i < 10; i++)
            {
                GameObject.Find("Backend").GetComponent<Button>().onClick.Invoke();

                while (entry.IsBusy)
                    yield return null;

                yield return null;
                Check(entry.Battle.Physics.BodyCount == 504 && entry.ViewCount == 500, "switch lifetime");
                Check(Unity.U2D.Physics.PhysicsWorld.worldCount == (entry.IsCore ? 2 : 1), "world count");
                Check(
                    UnityEngine.Object.FindObjectsByType<Rigidbody2D>().Length == (entry.IsCore ? 0 : 504),
                    "inactive legacy bodies");
            }

            var input = GameObject.Find("Soldiers").GetComponent<InputField>();

            foreach (var invalid in new[] { "", " ", "abc", "-1", "1.2", "0", "100001" })
            {
                input.text = invalid;
                input.onEndEdit.Invoke(invalid);
                yield return null;
                yield return null;
                Check(!GameObject.Find("Start").GetComponent<Button>().interactable, "invalid input rejected");
            }

            input.text = "25";
            input.onEndEdit.Invoke("25");
            yield return null;

            while (entry.IsBusy)
                yield return null;

            entry.Configure(25, 25, false);

            while (entry.IsBusy)
                yield return null;

            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);
            Check(Screen.width == 1280 && Screen.height == 720, "720p resolution");
            ScreenCapture.CaptureScreenshot(Path.Combine(_output, "ready-1280.png"));
            yield return new WaitForEndOfFrame();
            yield return null;
            GameObject.Find("Start").GetComponent<Button>().onClick.Invoke();
            GameObject.Find("Pause").GetComponent<Button>().onClick.Invoke();
            GameObject.Find("Step").GetComponent<Button>().onClick.Invoke();
            Check(entry.Battle.StepIndex == 1, "720p controls");
            GameObject.Find("Reset").GetComponent<Button>().onClick.Invoke();

            while (entry.IsBusy)
                yield return null;

            _report.AppendLine("10 backend switches; invalid inputs; 1920/1280 screenshots; actual button listeners; pause/step/reset/speed: PASS");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private void HandleLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;

            _errorLogged = true;
            _report.AppendLine(message + "\n" + stackTrace);
        }
    }
}
