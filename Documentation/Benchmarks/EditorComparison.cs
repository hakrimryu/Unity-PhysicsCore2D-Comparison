using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using PhysicsComparisonLab.Battle;
using PhysicsComparisonLab.Presentation;

namespace PhysicsComparisonLab.Documentation
{
    /// <summary>
    /// Assets外から一時実行するEditor診断です。Playerの正式測定とは区別します。
    /// [KO] Assets 밖에서 임시 실행하는 Editor 진단이며 Player 정식 측정과 구분합니다.
    /// [EN] Runs an ephemeral Editor diagnostic outside Assets, separate from Player benchmarks.
    /// </summary>
    public static class EditorComparison
    {
        private struct Sample
        {
            public int Step;
            public double Native, Query, Logic, Extract, View, Total;
        }

        private sealed class Context
        {
            public BattleSimulation Battle;
            public BattleWorldView View;
            public ComparisonMeasurement Host;
            public UnitAnimationSet Soldier, Orc;
            public string Output;
            public bool Success;
            public int CompletedRuns;
        }

        /// <summary>
        /// 保存済みシーンのPlay中に呼び、終了後はPlayを停止してください。
        /// [KO] 저장된 씬의 Play 중 호출하고 완료 후 Play를 종료합니다.
        /// [EN] Invoke in Play mode with a saved scene, then exit Play after completion.
        /// </summary>
        public static string Start(string output, bool smoke)
        {
            if (!Application.isPlaying || !Application.isEditor)
                throw new InvalidOperationException("An Editor Play session is required.");

            if (UnityEngine.Object.FindObjectsByType<ComparisonMeasurement>().Length != 0)
                throw new InvalidOperationException("A diagnostic is already running.");

            var entry = UnityEngine.Object.FindAnyObjectByType<BattleComparisonEntryPoint>();
            if (entry == null || entry.IsBusy)
                throw new InvalidOperationException("Wait for scene preparation first.");

            var context = new Context
            {
                Output = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), output)),
                Soldier = entry.SoldierAnimation,
                Orc = entry.OrcAnimation,
                Host = new GameObject("README benchmark").AddComponent<ComparisonMeasurement>()
            };
            Directory.CreateDirectory(context.Output);

            // 通常のUpdateと測定が同じworldを同時に進めないようにする。
            foreach (var panel in UnityEngine.Object.FindObjectsByType<BattlePerformancePanel>())
                panel.enabled = false;
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>())
                canvas.enabled = false;
            foreach (var field in UnityEngine.Object.FindObjectsByType<BattlefieldView>())
                field.enabled = false;

            UnityEngine.Object.Destroy(entry.gameObject);
            context.Host.StartCoroutine(Guard(context, Run(context, smoke)));
            return "Started Editor-only CPU diagnostic; poll completed.json or failed.txt in " + context.Output;
        }

        /// <summary>
        /// coroutineの例外も記録し、失敗を完了として扱いません。
        /// [KO] 코루틴 예외를 기록하며 실패를 완료로 표시하지 않습니다.
        /// [EN] Records coroutine failures without reporting them as completion.
        /// </summary>
        private static IEnumerator Guard(Context context, IEnumerator work)
        {
            while (true)
            {
                bool next;
                try
                {
                    next = work.MoveNext();
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(context.Output, "failed.txt"), error.ToString());
                    UnityEngine.Debug.LogException(error);
                    yield break;
                }

                if (!next)
                    yield break;

                yield return work.Current;
            }
        }

        /// <summary>
        /// 固定時間を最速で進め、各呼出しのCPU経過時間だけを採取します。
        /// [KO] 고정 시간을 가능한 빠르게 진행하며 호출별 CPU 경과시간만 수집합니다.
        /// [EN] Advances fixed steps without real-time pacing and samples CPU call elapsed time only.
        /// </summary>
        private static IEnumerator Run(Context context, bool smoke)
        {
            var cap = Application.targetFrameRate;
            var vsync = QualitySettings.vSyncCount;
            var profiler = UnityEngine.Profiling.Profiler.enabled;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Profiling.Profiler.enabled = false;

            try
            {
                yield return null;
                yield return null;
                CheckReleased();
                var initialMode = Physics2D.simulationMode;
                File.WriteAllText(Path.Combine(context.Output, "environment.txt"),
                    "UTC=" + DateTime.UtcNow.ToString("O") + "\nUnity=" + Application.unityVersion
                    + "\nOS=" + SystemInfo.operatingSystem + "\nCPU=" + SystemInfo.processorType
                    + "\nLogicalProcessors=" + SystemInfo.processorCount + "\nRAM_MB=" + SystemInfo.systemMemorySize
                    + "\nGPU=" + SystemInfo.graphicsDeviceName + "\nGraphicsAPI=" + SystemInfo.graphicsDeviceType
                    + "\nScreen=" + Screen.width + "x" + Screen.height
                    + "\nMode=Editor diagnostic; Built-in; Mono; Profiler disabled; vsync=0; frame cap=-1"
                    + "\nPacing=unpaced; at most 8 fixed steps per rendered frame; dt=1/60; seed=12345"
                    + "\nScope=CPU wall time of calls; not FPS, GPU time, worker CPU sum, memory or GC benchmark"
                    + "\nFixedLoad=120 warmup + 600 measured steps; deaths disabled, AI/contacts/attacks enabled"
                    + "\nBattle=step 1 to result or 1800; warmup uses a separate disposable battle"
                    + "\nPresentation=none or shared mesh Refresh per step; UI and camera tracking disabled"
                    + "\nOrder=Legacy/Core on odd repetitions, Core/Legacy on even repetitions; 5 repetitions"
                    + "\nCreation=backend and combat state only; excludes view construction; not a steady-state sample"
                    + "\nStatistics=per-run nearest-rank p50/p95; summary aggregation is documented separately\n");

                // JITとmesh経路を別の戦闘で準備し、本番の入力を変更しない。
                for (var backend = 0; backend < 2; backend++)
                {
                    context.Battle = BattleComparisonEntryPoint.CreateBattle(
                        new BattleInput(new BattleSettings(25, 25, 12345)), backend == 1, true);
                    context.View = new BattleWorldView(context.Battle.Units, context.Soldier, context.Orc);

                    for (var i = 0; i < 120; i++)
                    {
                        context.Battle.Step();
                        context.View.Refresh(context.Battle.Units, context.Battle.StepIndex);
                        if ((i + 1) % 8 == 0)
                            yield return null;
                    }

                    Release(context);
                    yield return null;
                    yield return null;
                    CheckReleased();
                }

                var summary = new StringBuilder("experiment,presentation,count_per_team,repeat,backend,samples,first_step,last_step,final_alive,hits,result,creation_ms,native_p50_ms,native_p95_ms,query_mean_ms,logic_mean_ms,extract_mean_ms,view_mean_ms,total_p50_ms,total_p95_ms\n");
                var samples = new Sample[1800];
                var repetitions = smoke ? 1 : 5;

                for (var experiment = 0; experiment < 2; experiment++)
                {
                    var fixedLoad = experiment == 1;
                    var sizes = smoke ? new[] { 25 } : fixedLoad ? new[] { 25, 100, 250, 1500, 2500 } : new[] { 25, 100, 250 };

                    for (var presentation = 0; presentation < 2; presentation++)
                    foreach (var size in sizes)
                    {
                        var input = new BattleInput(new BattleSettings(size, size, 12345));

                        for (var repeat = 0; repeat < repetitions; repeat++)
                        for (var order = 0; order < 2; order++)
                        {
                            var core = (repeat + order) % 2 == 1;
                            var name = (fixedLoad ? "FixedLoad" : "Battle") + "_"
                                + (presentation == 0 ? "NoView" : "Mesh") + "_" + size + "_"
                                + (repeat + 1) + "_" + (core ? "Core" : "Legacy");
                            context.Host.name = "README benchmark: " + name;
                            File.WriteAllText(Path.Combine(context.Output, "progress.txt"), context.CompletedRuns + " completed; running " + name);

                            var started = Stopwatch.GetTimestamp();
                            context.Battle = BattleComparisonEntryPoint.CreateBattle(input, core, fixedLoad);
                            var creation = Milliseconds(started);
                            var battle = context.Battle;
                            if (battle.Physics.BodyCount != input.Count + 4 || battle.Physics.ShapeCount != input.Count + 4)
                                throw new InvalidOperationException("Initial body/shape count mismatch: " + name);

                            if (presentation == 1)
                                context.View = new BattleWorldView(battle.Units, context.Soldier, context.Orc);

                            File.WriteAllText(Path.Combine(context.Output, name + ".config.txt"), battle.Physics.Configuration);
                            yield return null;

                            var warmup = fixedLoad ? 120 : 0;
                            var limit = fixedLoad ? 600 : 1800;
                            var count = 0;
                            for (var i = 0; i < warmup + limit && battle.Result == BattleSimulation.Outcome.None; i++)
                            {
                                var totalStart = Stopwatch.GetTimestamp();
                                battle.Step();
                                var viewStart = Stopwatch.GetTimestamp();
                                if (context.View != null)
                                    context.View.Refresh(battle.Units, battle.StepIndex);
                                var view = context.View == null ? 0 : Milliseconds(viewStart);
                                var total = Milliseconds(totalStart);

                                if (i >= warmup)
                                {
                                    samples[count++] = new Sample
                                    {
                                        Step = battle.StepIndex,
                                        Native = battle.NativeMilliseconds,
                                        Query = battle.QueryMilliseconds,
                                        Logic = battle.LogicMilliseconds,
                                        Extract = battle.ExtractMilliseconds,
                                        View = view,
                                        Total = total
                                    };
                                }

                                if ((i + 1) % 8 == 0)
                                    yield return null;
                            }

                            var alive = battle.SoldierAlive + battle.OrcAlive;
                            if (fixedLoad && (count != 600 || alive != input.Count))
                                throw new InvalidOperationException("Fixed load changed: " + name);
                            if (battle.Physics.BodyCount != alive + 4 || battle.Physics.ShapeCount != alive + 4)
                                throw new InvalidOperationException("Final body/shape count mismatch: " + name);

                            var raw = new StringBuilder("step,native_ms,query_ms,logic_ms,extract_ms,view_ms,total_ms\n");
                            var native = new double[count];
                            var totals = new double[count];
                            double querySum = 0, logicSum = 0, extractSum = 0, viewSum = 0;
                            for (var i = 0; i < count; i++)
                            {
                                var sample = samples[i];
                                native[i] = sample.Native;
                                totals[i] = sample.Total;
                                querySum += sample.Query;
                                logicSum += sample.Logic;
                                extractSum += sample.Extract;
                                viewSum += sample.View;
                                raw.AppendLine(string.Join(",", sample.Step, Number(sample.Native), Number(sample.Query),
                                    Number(sample.Logic), Number(sample.Extract), Number(sample.View), Number(sample.Total)));
                            }

                            Array.Sort(native);
                            Array.Sort(totals);
                            summary.AppendLine(string.Join(",", fixedLoad ? "FixedLoad" : "Battle", presentation == 0 ? "NoView" : "Mesh",
                                size, repeat + 1, core ? "Core" : "Legacy", count, samples[0].Step, samples[count - 1].Step,
                                alive, battle.HitCount, battle.Result, Number(creation), Number(Percentile(native, 0.5)),
                                Number(Percentile(native, 0.95)), Number(querySum / count), Number(logicSum / count),
                                Number(extractSum / count), Number(viewSum / count), Number(Percentile(totals, 0.5)), Number(Percentile(totals, 0.95))));

                            Release(context);
                            yield return null;
                            yield return null;
                            CheckReleased();
                            if (Physics2D.simulationMode != initialMode)
                                throw new InvalidOperationException("Legacy simulation mode was not restored.");

                            // 計測窓の外でのみファイルを保存する。
                            File.WriteAllText(Path.Combine(context.Output, name + ".csv"), raw.ToString());
                            File.WriteAllText(Path.Combine(context.Output, "runs.csv"), summary.ToString());
                            context.CompletedRuns++;
                        }
                    }
                }

                context.Success = true;
            }
            finally
            {
                Release(context);
                Application.targetFrameRate = cap;
                QualitySettings.vSyncCount = vsync;
                UnityEngine.Profiling.Profiler.enabled = profiler;
                if (context.Success)
                {
                    File.WriteAllText(Path.Combine(context.Output, "completed.json"), "{\"completed\":true,\"runs\":"
                        + context.CompletedRuns + ",\"cleanupCheckedAfterEveryRun\":true,\"settingsRestored\":true}");
                    context.Host.name = "README benchmark completed";
                }
            }
        }

        /// <summary>
        /// 所有する表示と物理worldを順番に解放します。
        /// [KO] 소유한 표시와 물리 world를 순서대로 해제합니다.
        /// [EN] Releases owned presentation and physics resources in order.
        /// </summary>
        private static void Release(Context context)
        {
            context.View?.Dispose();
            context.View = null;
            context.Battle?.Dispose();
            context.Battle = null;
        }

        /// <summary>
        /// 前回のbodyや追加worldが次回へ残っていないことを確認します。
        /// [KO] 이전 body나 추가 world가 다음 실행에 남지 않았는지 확인합니다.
        /// [EN] Checks that no previous bodies or additional worlds remain.
        /// </summary>
        private static void CheckReleased()
        {
            if (UnityEngine.Object.FindObjectsByType<Rigidbody2D>().Length != 0
                || Unity.U2D.Physics.PhysicsWorld.worldCount != 1)
                throw new InvalidOperationException("Previous physics resources are still alive.");
        }

        private static double Milliseconds(long started)
            => (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        private static string Number(double value)
            => value.ToString("F6", CultureInfo.InvariantCulture);

        private static double Percentile(double[] sorted, double fraction)
            => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * fraction) - 1)];
    }
}
