using System;
using UnityEngine;

namespace PhysicsComparisonLab.Battle
{
    /// <summary>
    /// 初期配置と遅延を一度だけ生成し、両方式で再利用します。
    /// [KO] 초기 배치와 지연을 한 번 생성해 두 방식에서 재사용합니다.
    /// [EN] Generates deployment and delays once for reuse by both backends.
    /// </summary>
    public sealed class BattleInput
    {
        public readonly struct Spawn
        {
            public int Id { get; }
            public int Team { get; }
            public Vector2 Position { get; }
            public int Delay { get; }

            public Spawn(int id, int team, Vector2 position, int delay)
            {
                Id = id;
                Team = team;
                Position = position;
                Delay = delay;
            }
        }

        private readonly Spawn[] _units;

        public BattleSettings Settings { get; }

        /// <summary>
        /// 配置全体と余白を含む共通物理境界の半径です。
        /// [KO] 전체 배치와 여백을 포함하는 공통 물리 경계의 반폭과 반높이입니다.
        /// [EN] Half extents of the shared physics bounds around the full deployment.
        /// </summary>
        public Vector2 HalfExtents { get; private set; } = new Vector2(6.5f, 2.65f);
        public int Count
            => _units.Length;

        public Spawn this[int index]
            => _units[index];

        /// <summary>
        /// seedから重ならない配置と出発遅延を一度生成します。
        /// [KO] seed로 겹치지 않는 배치와 출발 지연을 한 번 생성합니다.
        /// [EN] Generates a non-overlapping deployment and departure delays once from the seed.
        /// </summary>
        public BattleInput(BattleSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _units = new Spawn[settings.Soldiers + settings.Orcs];
            var random = new System.Random(settings.Seed);
            var id = 0;

            for (var team = 0; team < 2; team++)
            {
                var count = team == 0 ? settings.Soldiers : settings.Orcs;
                var rows = Mathf.CeilToInt(Mathf.Sqrt(count));
                var columns = Mathf.CeilToInt((float)count / rows);
                var spacing = Mathf.Max(0.28f, Mathf.Min(0.70f, 4.2f / Mathf.Max(rows - 1, columns - 1, 1)));

                for (var i = 0; i < count; i++, id++)
                {
                    // 前列から後列へ配置し、足元の円が重ならない間隔を保つ。
                    var position = new Vector2
                        ((team == 0 ? -1 : 1) * (1.2f + i / rows * spacing),
                        (i % rows - (rows - 1) / 2f) * spacing);
                    _units[id] = new Spawn(id, team, position, random.Next(13));
                    ExpandBounds(position);
                }
            }
        }

        /// <summary>
        /// 検証用の明示配置も同じ実行経路へ渡します。
        /// [KO] 검증용 명시 배치도 같은 실행 경로에 전달합니다.
        /// [EN] Passes explicit validation layouts through the same execution path.
        /// </summary>
        public BattleInput(BattleSettings settings, Spawn[] units)
        {
            Settings = settings;
            _units = (Spawn[])units.Clone();

            if (_units.Length != settings.Soldiers + settings.Orcs)
                throw new ArgumentException("Spawn count differs from settings.");

            for (var i = 0; i < _units.Length; i++)
            {
                if (_units[i].Id != i || _units[i].Team < 0 || _units[i].Team > 1)
                    throw new ArgumentException("Invalid spawn ID/team.");

                ExpandBounds(_units[i].Position);
            }
        }

        /// <summary>
        /// 固定半径を保ったまま、必要な戦場だけ広げます。
        /// [KO] 고정 반지름을 유지하면서 필요한 전장 크기만 확장합니다.
        /// [EN] Expands the battlefield without shrinking the fixed body radius.
        /// </summary>
        private void ExpandBounds(Vector2 position)
        {
            HalfExtents = Vector2.Max(HalfExtents, new Vector2(Mathf.Abs(position.x) + 0.5f, Mathf.Abs(position.y) + 0.5f));
        }
    }
}
