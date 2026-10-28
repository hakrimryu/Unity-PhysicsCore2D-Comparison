using System;
using System.Globalization;

namespace PhysicsComparisonLab.Battle
{
    /// <summary>
    /// 両方式に共通の固定戦闘条件です。
    /// [KO] 두 방식이 공유하는 고정 전투 조건입니다.
    /// [EN] Fixed battle conditions shared by both backends.
    /// </summary>
    public sealed class BattleSettings
    {
        // 距離はworld unit、速度はunit/秒、攻撃と制限時間は固定step数で扱う。
        public const float StepSeconds = 1f / 60f;
        public const float Radius = 0.12f;
        public const float Speed = 1.2f;
        public const float AttackRange = 0.36f;

        /// <summary>
        /// 攻撃開始から命中までのstep数です。60Hzで18stepは0.3秒です。
        /// [KO] 공격 시작부터 명중까지의 step 수입니다. 60Hz에서 18step은 0.3초입니다.
        /// [EN] Steps from attack start to hit; 18 steps at 60 Hz equals 0.3 seconds.
        /// </summary>
        public const int HitDelay = 18;
        public const int AttackPeriod = 36;
        public const int MaximumSteps = 1800;
        public const int MaximumUnitsPerTeam = 2500;

        public int Soldiers { get; }
        public int Orcs { get; }
        public int Seed { get; }

        /// <summary>
        /// 人数とseedの範囲を検査して実行条件を固定します。
        /// [KO] 인원과 seed 범위를 검사하고 실행 조건을 고정합니다.
        /// [EN] Validates counts and seed and fixes the run settings.
        /// </summary>
        public BattleSettings(int soldiers, int orcs, int seed)
        {
            if (soldiers < 1 || soldiers > MaximumUnitsPerTeam || orcs < 1 || orcs > MaximumUnitsPerTeam || seed < 0)
                throw new ArgumentOutOfRangeException
                    (nameof(soldiers),
                    $"Each army must have 1–{MaximumUnitsPerTeam:N0} units; seed must be non-negative.");

            Soldiers = soldiers;
            Orcs = orcs;
            Seed = seed;
        }

        /// <summary>
        /// 入力を補正せず検証します。
        /// [KO] 입력을 보정하지 않고 검증합니다.
        /// [EN] Validates input without silently correcting it.
        /// </summary>
        public static bool TryParse(
            string soldiers,
            string orcs,
            string seed,
            out BattleSettings settings,
            out string error)
        {
            settings = null;
            error = $"Enter 1–{MaximumUnitsPerTeam:N0} units per army and a seed from 0–2147483647.";

            if (!int.TryParse(soldiers, NumberStyles.None, CultureInfo.InvariantCulture, out var left)
                || !int.TryParse(orcs, NumberStyles.None, CultureInfo.InvariantCulture, out var right)
                || !int.TryParse(seed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSeed)
                || left < 1
                || left > MaximumUnitsPerTeam
                || right < 1
                || right > MaximumUnitsPerTeam)
            {
                return false;
            }

            settings = new BattleSettings(left, right, parsedSeed);
            error = "";

            return true;
        }
    }
}
