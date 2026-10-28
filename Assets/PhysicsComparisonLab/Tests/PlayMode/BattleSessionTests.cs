using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PhysicsComparisonLab.Battle;
using PhysicsComparisonLab.Presentation;

namespace PhysicsComparisonLab.Tests
{
    /// <summary>
    /// 実シーンで停止、単一step、切替、最大兵力と寿命を検査します。
    /// [KO] 실제 씬에서 정지, 한 스텝, 전환, 최대 병력과 수명을 검사합니다.
    /// [EN] Tests pause, single-step, switching, maximum armies and lifetime in the real scene.
    /// </summary>
    public sealed class BattleSessionTests
    {
        [UnityTest]
        public IEnumerator BothBackendsCompleteMatrixAndTenResetsWithoutLeaks()
        {
            yield return SceneManager.LoadSceneAsync("BattleComparison");
            yield return null;
            yield return null;
            var entry = Object.FindAnyObjectByType<BattleComparisonEntryPoint>();
            Assert.NotNull(entry);

            while (entry.IsBusy)
                yield return null;

            var pairs = new[] { (1, 1), (1, 2), (25, 25), (100, 100), (250, 250) };

            for (var backend = 0; backend < 2; backend++)
            {
                foreach (var pair in pairs)
                {
                    entry.Configure(pair.Item1, pair.Item2, backend == 1);

                    while (entry.IsBusy)
                        yield return null;

                    yield return null;
                    Assert.AreEqual(pair.Item1 + pair.Item2 + 4, entry.Battle.Physics.BodyCount);
                    Assert.AreEqual(pair.Item1 + pair.Item2, entry.ViewCount);
                    Assert.AreEqual(
                        backend == 1 ? 0 : pair.Item1 + pair.Item2 + 4,
                        Object.FindObjectsByType<Rigidbody2D>().Length);
                    Assert.AreEqual(backend == 1 ? 2 : 1, Unity.U2D.Physics.PhysicsWorld.worldCount);
                    entry.StartBattle();
                    entry.TogglePause();
                    var before = entry.Battle.StepIndex;
                    var position = entry.Battle.Units[0].Position;
                    yield return new WaitForSecondsRealtime(0.08f);
                    Assert.AreEqual(before, entry.Battle.StepIndex, "Pause must stop the clock.");
                    Assert.AreEqual(
                        position,
                        entry.Battle.Units[0].Position,
                        "Automatic physics must not move a paused body.");
                    entry.SingleStep();
                    Assert.AreEqual(before + 1, entry.Battle.StepIndex);

                    for (var i = 0; i < 1900 && !entry.IsFinished; i++)
                    {
                        entry.SingleStep();

                        if (i % 60 == 0)
                            yield return null;
                    }

                    Assert.IsTrue(entry.IsFinished, "Battle and death animations must finish by timeout + 54 steps.");
                    Assert.AreNotEqual(BattleSimulation.Outcome.None, entry.Battle.Result);
                    Assert.AreEqual(entry.Battle.SoldierAlive + entry.Battle.OrcAlive + 4, entry.Battle.Physics.BodyCount);
                    yield return null;
                    Assert.AreEqual(entry.Battle.SoldierAlive + entry.Battle.OrcAlive, entry.ViewCount);
                    var endedStep = entry.Battle.StepIndex;
                    var endedHits = entry.Battle.HitCount;
                    entry.SingleStep();
                    Assert.AreEqual(endedStep, entry.Battle.StepIndex);
                    Assert.AreEqual(endedHits, entry.Battle.HitCount);
                    entry.ResetBattle();

                    while (entry.IsBusy)
                        yield return null;

                    Assert.IsTrue(entry.IsReady);
                    Assert.AreEqual(0, entry.Battle.StepIndex);
                }
            }

            for (var i = 0; i < 10; i++)
            {
                entry.ToggleBackend();

                while (entry.IsBusy)
                    yield return null;

                yield return null;
                Assert.AreEqual(504, entry.Battle.Physics.BodyCount);
                Assert.AreEqual(500, entry.ViewCount);
                Assert.AreEqual(entry.IsCore ? 0 : 504, Object.FindObjectsByType<Rigidbody2D>().Length);
                Assert.AreEqual(entry.IsCore ? 2 : 1, Unity.U2D.Physics.PhysicsWorld.worldCount);
                var speed = GameObject.Find("Speed").GetComponent<Button>();
                var old = entry.Speed;
                speed.onClick.Invoke();
                Assert.AreNotEqual(old, entry.Speed, "A single UI click must invoke exactly one listener.");
                speed.onClick.Invoke();
                Assert.AreEqual(old, entry.Speed);
            }

            Object.Destroy(entry.gameObject);
            yield return null;
            yield return null;
            Assert.AreEqual(0, Object.FindObjectsByType<Rigidbody2D>().Length);
            Assert.AreEqual(1, Unity.U2D.Physics.PhysicsWorld.worldCount);
            Assert.IsNull(GameObject.Find("BattleUnitViews"));
            Assert.AreEqual(SimulationMode2D.FixedUpdate, Physics2D.simulationMode);
        }

        /// <summary>
        /// 加算ボタンと上限、再有効化後のlistener数を検査します。
        /// [KO] 증가 버튼과 상한, 재활성화 뒤 listener 중복을 검사합니다.
        /// [EN] Checks count increments, the cap, and listener duplication after re-enabling.
        /// </summary>
        [UnityTest]
        public IEnumerator CountAdjustmentButtonsAddClampAndUseConfiguredMaximum()
        {
            yield return SceneManager.LoadSceneAsync("BattleComparison");
            yield return null;
            var entry = Object.FindAnyObjectByType<BattleComparisonEntryPoint>();

            while (entry.IsBusy)
                yield return null;

            var panel = Object.FindAnyObjectByType<BattlePanelView>();
            panel.SetCounts(25, 60);

            while (entry.IsBusy)
                yield return null;

            var labels = new[] { "+10", "+50", "+100", "Max" };
            var expected = new[] { 35, 85, 185 };

            for (var i = 0; i < 3; i++)
            {
                var button = GameObject.Find("Preset" + i).GetComponent<Button>();
                Assert.AreEqual(labels[i], button.GetComponentInChildren<Text>().text);
                button.onClick.Invoke();

                while (entry.IsBusy)
                    yield return null;

                Assert.AreEqual(expected[i].ToString(), panel.Soldiers);
                Assert.AreEqual((expected[i] + 35).ToString(), panel.Orcs);
            }

            panel.enabled = false;
            panel.enabled = true;
            GameObject.Find("Preset0").GetComponent<Button>().onClick.Invoke();

            while (entry.IsBusy)
                yield return null;

            Assert.AreEqual("195", panel.Soldiers);
            Assert.AreEqual("230", panel.Orcs);
            var maximum = BattleSettings.MaximumUnitsPerTeam;
            GameObject.Find("Soldiers").GetComponent<InputField>().SetTextWithoutNotify((maximum - 5).ToString());
            GameObject.Find("Orcs").GetComponent<InputField>().SetTextWithoutNotify((maximum - 20).ToString());
            GameObject.Find("Preset0").GetComponent<Button>().onClick.Invoke();

            while (entry.IsBusy)
                yield return null;

            Assert.AreEqual(maximum.ToString(), panel.Soldiers);
            Assert.AreEqual((maximum - 10).ToString(), panel.Orcs);
            var maxButton = GameObject.Find("Preset3").GetComponent<Button>();
            Assert.AreEqual("Max", maxButton.GetComponentInChildren<Text>().text);
            maxButton.onClick.Invoke();

            while (entry.IsBusy)
                yield return null;

            Assert.AreEqual(maximum.ToString(), panel.Soldiers);
            Assert.AreEqual(maximum.ToString(), panel.Orcs);
            Assert.AreEqual(maximum * 2, entry.Battle.Input.Count);
            Assert.IsNull(GameObject.Find("Preset4"));
            Assert.IsFalse(BattleSettings.TryParse((maximum + 1).ToString(), "25", "12345", out _, out var error));
            StringAssert.Contains(maximum.ToString("N0"), error);
            panel.SetCounts(25, 25);

            while (entry.IsBusy)
                yield return null;

            Assert.AreEqual(54, entry.Battle.Physics.BodyCount);
        }

        [UnityTest]
        public IEnumerator InvalidUiInputsDisableStartAndBothSpeedsUseSameSteps()
        {
            yield return SceneManager.LoadSceneAsync("BattleComparison");
            yield return null;
            yield return null;
            var entry = Object.FindAnyObjectByType<BattleComparisonEntryPoint>();

            while (entry.IsBusy)
                yield return null;

            var input = GameObject.Find("Soldiers").GetComponent<InputField>();
            var start = GameObject.Find("Start").GetComponent<Button>();

            foreach (var invalid in new[]
            {
                "",
                "abc",
                "-1",
                "1.5",
                "0",
                (BattleSettings.MaximumUnitsPerTeam + 1).ToString()
            })
            {
                input.text = invalid;
                input.onEndEdit.Invoke(invalid);
                yield return null;
                yield return null;
                Assert.IsFalse(start.interactable);
                Assert.IsNull(entry.Battle);
            }

            input.text = "25";
            input.onEndEdit.Invoke("25");
            yield return null;
            yield return null;
            Assert.IsTrue(start.interactable);
            entry.StartBattle();
            entry.TogglePause();

            for (var i = 0; i < 18; i++)
                entry.SingleStep();

            var position = entry.Battle.Units[0].Position;
            entry.ToggleSpeed();
            entry.SingleStep();
            Assert.AreEqual(19, entry.Battle.StepIndex);
            Assert.LessOrEqual(Vector2.Distance(position, entry.Battle.Units[0].Position), BattleSettings.Speed / 60 + 0.002f);
            entry.ResetBattle();

            while (entry.IsBusy)
                yield return null;
        }

        /// <summary>
        /// 固定領域に操作を収め、実在するcounterと両軍の人数を表示します。
        /// [KO] 고정 영역에 조작을 배치하고 실제 counter와 양 진영 인원을 표시합니다.
        /// [EN] Checks reserved layout space and live counters with separate army counts.
        /// </summary>
        [UnityTest]
        public IEnumerator SidebarReservesSpaceAndShowsSeparateLiveArmies()
        {
            yield return SceneManager.LoadSceneAsync("BattleComparison");
            yield return null;
            var entry = Object.FindAnyObjectByType<BattleComparisonEntryPoint>();

            while (entry.IsBusy)
                yield return null;

            var panel = Object.FindAnyObjectByType<BattlePerformancePanel>();
            Assert.IsTrue(panel.isActiveAndEnabled);
            Assert.IsNull(GameObject.Find("Header").transform.Find("PerformanceToggle"));
            Assert.IsNull(GameObject.Find("ClosePerformance"));
            var header = GameObject.Find("Header").GetComponent<RectTransform>();
            var headerScale = header.localScale;
            var headerHeight = header.rect.height;

            void CheckHeaderControls()
            {
                Canvas.ForceUpdateCanvases();
                var corners = new Vector3[4];
                header.GetWorldCorners(corners);
                var bounds = new Rect(corners[0], corners[2] - corners[0]);
                var occupied = new System.Collections.Generic.List<Rect>();

                foreach (var control in header.GetComponentsInChildren<Selectable>())
                {
                    ((RectTransform)control.transform).GetWorldCorners(corners);
                    var rect = new Rect(corners[0], corners[2] - corners[0]);
                    Assert.GreaterOrEqual(rect.xMin, bounds.xMin - 1, control.name);
                    Assert.LessOrEqual(rect.xMax, bounds.xMax + 1, control.name);
                    Assert.GreaterOrEqual(rect.yMin, bounds.yMin - 1, control.name);
                    Assert.LessOrEqual(rect.yMax, bounds.yMax + 1, control.name);

                    foreach (var other in occupied)
                        Assert.IsFalse(rect.Overlaps(other), control.name + " overlaps another control");

                    occupied.Add(rect);
                }

                Assert.AreEqual(headerScale, header.localScale);
                Assert.AreEqual(headerHeight, header.rect.height);
            }

            CheckHeaderControls();
            var width = ((RectTransform)header.parent).rect.width;
            Assert.AreEqual(
                1 - GameObject.Find("PerformanceSidebar").GetComponent<RectTransform>().rect.width / width,
                Camera.main.rect.width,
                0.0001f);
            yield return new WaitForSecondsRealtime(0.5f);
            var text = GameObject.Find("PerformanceReadout").GetComponent<Text>().text;

            string ReadValue(string label)
            {
                var labels = GameObject.Find("PerformanceReadout").GetComponent<Text>().text.Split('\n');
                var values = GameObject.Find("PerformanceValues").GetComponent<Text>().text.Split('\n');
                var row = System.Array.FindIndex(labels, line => line.TrimEnd('\r') == label);
                Assert.GreaterOrEqual(row, 0, "Missing statistic label: " + label);
                Assert.Less(row, values.Length, "Missing statistic value: " + label);

                return values[row].TrimEnd('\r');
            }

            Assert.AreEqual("25 / 25", ReadValue("Allies (Soldier)"));
            Assert.AreEqual("25 / 25", ReadValue("Enemies (Orc)"));
            StringAssert.DoesNotContain("지원", text);
            StringAssert.DoesNotContain("Unsupported", text);
            var sidebar = GameObject.Find("PerformanceSidebar").GetComponent<RectTransform>();
            var panelCorners = new Vector3[4];
            sidebar.GetWorldCorners(panelCorners);

            foreach (var name in new[] { "Header", "Footer" })
            {
                var corners = new Vector3[4];
                GameObject.Find(name).GetComponent<RectTransform>().GetWorldCorners(corners);
                Assert.LessOrEqual(corners[2].x, panelCorners[0].x + 1, name + " overlaps sidebar");
            }

            foreach (var label in GameObject.Find("BattlePanel").GetComponentsInChildren<Text>(true))
            {
                Assert.AreEqual("LegacyRuntime", label.font.name, label.name);

                if (!label.gameObject.activeInHierarchy)
                    continue;

                Assert.LessOrEqual(
                    label.preferredHeight,
                    label.rectTransform.rect.height + 1,
                    label.name + " text exceeds height");

                if (label.GetComponentInParent<Button>() == null)
                    continue;

                Assert.AreEqual(Vector2.zero, label.rectTransform.anchorMin, label.transform.parent.name);
                Assert.AreEqual(Vector2.one, label.rectTransform.anchorMax, label.transform.parent.name);
                Assert.AreEqual(TextAnchor.MiddleCenter, label.alignment, label.transform.parent.name);
                Assert.LessOrEqual(
                    label.preferredWidth,
                    label.rectTransform.rect.width + 1,
                    label.transform.parent.name + " text exceeds width");
            }

            var evidence = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../_Development/Evidence"));
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(evidence, "ui-aligned-ready.png"));
            yield return new WaitForEndOfFrame();
            entry.StartBattle();
            entry.TogglePause();

            for (var i = 0; i < 120; i++)
                entry.SingleStep();

            yield return new WaitForSecondsRealtime(0.3f);
            text = GameObject.Find("PerformanceReadout").GetComponent<Text>().text;
            Assert.AreEqual(entry.Battle.SoldierAlive.ToString("N0") + " / 25", ReadValue("Allies (Soldier)"));
            Assert.AreEqual(entry.Battle.OrcAlive.ToString("N0") + " / 25", ReadValue("Enemies (Orc)"));
            CheckHeaderControls();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(evidence, "ui-aligned-paused.png"));
            yield return new WaitForEndOfFrame();
        }

        /// <summary>
        /// Editor内で千五百対千五百を実生成し、両backendの動作と解放を検査します。
        /// [KO] Editor 안에서 1,500 대 1,500을 실제 생성하고 두 backend의 동작과 해제를 검사합니다.
        /// [EN] Creates 1,500 versus 1,500 in the Editor and checks both backends and cleanup.
        /// </summary>
        [UnityTest]
        [Category("LargeLoad")]
        public IEnumerator MaximumArmiesActuallyRunAndReleaseInEditor()
        {
            yield return SceneManager.LoadSceneAsync("BattleComparison");
            yield return null;
            var entry = Object.FindAnyObjectByType<BattleComparisonEntryPoint>();

            while (entry.IsBusy)
                yield return null;

            var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                Application.dataPath,
                "../../_Development/Evidence/editor-large-" + System.DateTime.Now.ToString("HHmmss")));
            var validator = entry.gameObject.AddComponent<LargeBattleValidation>();
            validator.OutputDirectory = output;
            var deadline = Time.realtimeSinceStartupAsDouble + 300;
            var pass = System.IO.Path.Combine(output, "validation-pass.txt");
            var fail = System.IO.Path.Combine(output, "validation-failed.txt");

            while (!System.IO.File.Exists(pass)
                && !System.IO.File.Exists(fail)
                && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;

            Assert.IsTrue(
                System.IO.File.Exists(pass),
                System.IO.File.Exists(fail) ? System.IO.File.ReadAllText(fail) : "Maximum load timed out: " + output);
            Object.Destroy(validator);
        }
    }
}
