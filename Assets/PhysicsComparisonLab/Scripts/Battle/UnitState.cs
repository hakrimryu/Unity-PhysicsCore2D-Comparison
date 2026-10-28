using UnityEngine;

namespace PhysicsComparisonLab.Battle
{
    /// <summary>
    /// 物理ハンドルを含まない共通の一兵士状態です。
    /// [KO] 물리 핸들을 포함하지 않는 공통 유닛 상태입니다.
    /// [EN] Shared unit state without backend handles.
    /// </summary>
    public sealed class UnitState
    {
        public enum Action
        {
            Idle,
            Walk,
            Attack,
            Death
        }

        public int Id { get; }
        public int Team { get; }
        public int Delay { get; }
        public bool Alive { get; internal set; } = true;
        public Vector2 Position { get; internal set; }
        public Vector2 Velocity { get; internal set; }

        // Targetは攻撃開始時の相手を保持する。-1は未選択または未開始を表す。
        public int Target { get; internal set; } = -1;
        public int AttackStart { get; internal set; } = -1;
        public int DeathStep { get; internal set; } = -1;
        public bool HitConsumed { get; internal set; }

        // 表示フレームは行動開始stepから選び、物理時計と同時に停止する。
        public Action CurrentAction { get; internal set; }
        public int ActionStart { get; internal set; }

        /// <summary>
        /// 初期入力を実行中の生存・位置状態へ展開します。
        /// [KO] 초기 입력을 실행 중 생존 및 위치 상태로 펼칩니다.
        /// [EN] Initializes live state and position from a spawn record.
        /// </summary>
        public UnitState(BattleInput.Spawn spawn)
        {
            Id = spawn.Id;
            Team = spawn.Team;
            Delay = spawn.Delay;
            Position = spawn.Position;
        }

        /// <summary>
        /// 行動が変わったstepを表示clockの起点にします。
        /// [KO] 행동이 바뀐 step을 표시 clock의 시작점으로 삼습니다.
        /// [EN] Records the action-change step as the presentation clock origin.
        /// </summary>
        internal void SetAction(Action action, int step)
        {
            if (CurrentAction == action)
                return;

            CurrentAction = action;
            ActionStart = step;
        }
    }
}
