using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using PhysicsComparisonLab.Simulation;

namespace PhysicsComparisonLab.Battle
{
    /// <summary>
    /// 固定stepで行動を決め、同時命中を収集してから死亡を反映します。
    /// [KO] 고정 step에서 행동을 결정하고 동시 명중 수집 후 사망을 반영합니다.
    /// [EN] Decides actions on fixed steps and applies deaths after collecting simultaneous hits.
    /// </summary>
    public sealed class BattleSimulation : IDisposable
    {
        public enum Outcome
        {
            None,
            Soldier,
            Orc,
            Draw,
            Timeout
        }

        private static readonly ProfilerMarker StepMarker = new ProfilerMarker("Battle.NativeStep");
        private static readonly ProfilerMarker QueryMarker = new ProfilerMarker("Battle.Overlap");
        private static readonly ProfilerMarker LogicMarker = new ProfilerMarker("Battle.Logic");

        // このstepの命中候補を集め、全攻撃の判定後にだけAliveへ反映する。
        private readonly bool[] _deaths;
        private readonly bool _benchmark;
        private readonly BattleTargetIndex _targets;

        public BattleInput Input { get; }
        public UnitState[] Units { get; }
        public IPhysicsSimulation Physics { get; }

        // 戦闘clockは固定step単位。表示フレーム数やAnimation Eventとは独立する。
        public int StepIndex { get; private set; }
        public int SoldierAlive { get; private set; }
        public int OrcAlive { get; private set; }
        public Outcome Result { get; private set; }
        public int HitCount { get; private set; }

        // 直前のstepを処理区間ごとに計時する。フレーム全体の費用ではない。
        public double NativeMilliseconds { get; private set; }
        public double QueryMilliseconds { get; private set; }
        public double LogicMilliseconds { get; private set; }
        public double ExtractMilliseconds { get; private set; }

        /// <summary>
        /// 共通入力と所有するbackendから戦闘状態を作ります。
        /// [KO] 공통 입력과 소유 backend로 전투 상태를 만듭니다.
        /// [EN] Creates combat state from shared input and the owned backend.
        /// </summary>
        public BattleSimulation(BattleInput input, IPhysicsSimulation physics, bool benchmark = false)
        {
            Input = input;
            Physics = physics;
            _benchmark = benchmark;
            Units = new UnitState[input.Count];
            _deaths = new bool[input.Count];

            for (var i = 0; i < Units.Length; i++)
                Units[i] = new UnitState(input[i]);

            _targets = new BattleTargetIndex(Units);
            SoldierAlive = input.Settings.Soldiers;
            OrcAlive = input.Settings.Orcs;
        }

        /// <summary>
        /// A+18の命中とA+36の再攻撃を同じclockで処理します。
        /// [KO] A+18 명중과 A+36 재공격을 같은 clock으로 처리합니다.
        /// [EN] Handles A+18 hits and A+36 reattacks on the same clock.
        /// </summary>
        public void Step()
        {
            if (Result != Outcome.None)
                return;

            // 前stepの位置から全員の行動と目標速度を決める。
            StepIndex++;
            var started = Stopwatch.GetTimestamp();

            using (LogicMarker.Auto())
            {
                for (var i = 0; i < Units.Length; i++)
                {
                    var unit = Units[i];

                    if (!unit.Alive)
                        continue;

                    unit.Velocity = Vector2.zero;

                    if (unit.CurrentAction == UnitState.Action.Attack
                        && StepIndex - unit.AttackStart < BattleSettings.AttackPeriod)
                    {
                        Physics.SetVelocity(i, Vector2.zero);
                        continue;
                    }

                    if (unit.CurrentAction == UnitState.Action.Attack)
                        unit.SetAction(UnitState.Action.Idle, StepIndex);

                    if (StepIndex > unit.Delay)
                    {
                        if (unit.Target < 0 || !Units[unit.Target].Alive || StepIndex % 6 == 0)
                            unit.Target = FindNearest(unit);

                        if (unit.Target >= 0)
                        {
                            var delta = Units[unit.Target].Position - unit.Position;

                            if (delta.sqrMagnitude <= BattleSettings.AttackRange * BattleSettings.AttackRange)
                            {
                                unit.SetAction(UnitState.Action.Attack, StepIndex);
                                unit.AttackStart = StepIndex;
                                unit.HitConsumed = false;
                            }
                            else
                            {
                                unit.SetAction(UnitState.Action.Walk, StepIndex);
                                unit.Velocity = delta.normalized * BattleSettings.Speed;
                            }
                        }
                    }

                    Physics.SetVelocity(i, unit.Velocity);
                }
            }

            LogicMilliseconds = Elapsed(started);
            started = Stopwatch.GetTimestamp();

            // 選択backendだけを一度進め、その完了位置を攻撃判定に使う。
            using (StepMarker.Auto())
                Physics.Step(BattleSettings.StepSeconds);

            NativeMilliseconds = Elapsed(started);
            started = Stopwatch.GetTimestamp();
            Physics.CopyPositions(Units);
            ExtractMilliseconds = Elapsed(started);

            // A+18の候補を全員分集めるまで、標的の生存状態を変えない。
            Array.Clear(_deaths, 0, _deaths.Length);
            started = Stopwatch.GetTimestamp();

            using (QueryMarker.Auto())
            {
                for (var i = 0; i < Units.Length; i++)
                {
                    var attacker = Units[i];

                    if (!attacker.Alive
                        || attacker.CurrentAction != UnitState.Action.Attack
                        || attacker.HitConsumed
                        || StepIndex - attacker.AttackStart != BattleSettings.HitDelay)
                    {
                        continue;
                    }

                    attacker.HitConsumed = true;
                    var target = attacker.Target;

                    if (target >= 0
                        && Units[target].Alive
                        && Units[target].Team != attacker.Team
                        && Physics.Overlaps(i, target, BattleSettings.AttackRange)
                        && (attacker.Position - Units[target].Position).sqrMagnitude <= BattleSettings.AttackRange * BattleSettings.AttackRange)
                    {
                        HitCount++;
                        _deaths[target] = true;
                    }
                }
            }

            QueryMilliseconds = Elapsed(started);
            started = Stopwatch.GetTimestamp();

            if (!_benchmark)
            {
                // 全候補の収集が終わるまでAliveを変えない。相討ちと重複命中をここで統合する。
                for (var i = 0; i < Units.Length; i++)
                {
                    if (!_deaths[i])
                        continue;

                    var unit = Units[i];
                    unit.Alive = false;
                    unit.DeathStep = StepIndex;
                    unit.SetAction(UnitState.Action.Death, StepIndex);
                    unit.Velocity = Vector2.zero;
                    Physics.Remove(i);

                    if (unit.Team == 0)
                        SoldierAlive--;
                    else
                        OrcAlive--;
                }

                if (SoldierAlive == 0 && OrcAlive == 0)
                    Result = Outcome.Draw;
                else if (SoldierAlive == 0)
                    Result = Outcome.Orc;
                else if (OrcAlive == 0)
                    Result = Outcome.Soldier;
                else if (StepIndex >= BattleSettings.MaximumSteps)
                    Result = Outcome.Timeout;
            }

            LogicMilliseconds += Elapsed(started);
        }

        /// <summary>
        /// 生存する敵を距離、同距離ではIDで選びます。
        /// [KO] 생존 적을 거리, 같은 거리에서는 ID로 선택합니다.
        /// [EN] Selects a live enemy by distance, breaking ties by ID.
        /// </summary>
        private int FindNearest(UnitState attacker)
        {
            return _targets.FindNearest(attacker, StepIndex);
        }

        /// <summary>
        /// 高精度clockの差をミリ秒へ変換します。
        /// [KO] 고해상도 clock 차이를 밀리초로 변환합니다.
        /// [EN] Converts the high-resolution clock difference to milliseconds.
        /// </summary>
        private static double Elapsed(long start)
            => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

        /// <summary>
        /// 自分が所有する資源だけを解放します。
        /// [KO] 직접 소유한 자원만 해제합니다.
        /// [EN] Releases only resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            Physics.Dispose();
        }
    }
}
