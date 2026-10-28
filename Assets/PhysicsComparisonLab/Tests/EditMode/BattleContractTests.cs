using System;
using NUnit.Framework;
using UnityEngine;
using PhysicsComparisonLab.Battle;
using PhysicsComparisonLab.Simulation;
using PhysicsComparisonLab.Presentation;

namespace PhysicsComparisonLab.Tests
{
    /// <summary>
    /// clockと一括死亡の境界を物理結果から独立して検査します。
    /// [KO] clock과 일괄 사망 경계를 물리 결과와 독립적으로 검사합니다.
    /// [EN] Tests clock and simultaneous death boundaries independently of solver outcomes.
    /// </summary>
    public sealed class BattleContractTests
    {
        private sealed class FixedPhysics : IPhysicsSimulation
        {
            public readonly Vector2[] Positions;

            public int BodyCount { get; private set; }
            public int ShapeCount
                => BodyCount;

            public string Configuration
                => "Test fixture; no native physics";

            public FixedPhysics(BattleInput input)
            {
                Positions = new Vector2[input.Count];
                BodyCount = input.Count;

                for (var i = 0; i < input.Count; i++)
                    Positions[i] = input[i].Position;
            }

            public bool CreateNextBatch(int maximum)
                => true;

            public void SetVelocity(int id, Vector2 velocity)
            {
            }

            public void Step(float seconds)
            {
            }

            public void CopyPositions(UnitState[] units)
            {
                for (var i = 0; i < units.Length; i++)
                    units[i].Position = Positions[i];
            }

            public bool Overlaps(int attacker, int target, float range)
                => Vector2.Distance(Positions[attacker], Positions[target]) <= range;

            public void Remove(int id)
            {
                BodyCount--;
            }

            public void Dispose()
            {
                BodyCount = 0;
            }
        }

        private static BattleInput Duel(int delay = 0)
            => new BattleInput
                (new BattleSettings(1, 1, 12345),
                new[]
                {
                    new BattleInput.Spawn(0, 0, new Vector2(-0.15f, 0), delay),
                    new BattleInput.Spawn(1, 1, new Vector2(0.15f, 0), delay)
                });

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("abc")]
        [TestCase("-1")]
        [TestCase("1.0")]
        [TestCase("0")]
        [TestCase("above-limit")]
        [TestCase(" 1")]
        public void InvalidInputIsRejected(string value)
        {
            if (value == "above-limit")
                value = (BattleSettings.MaximumUnitsPerTeam + 1).ToString();

            Assert.IsFalse(BattleSettings.TryParse(value, "25", "12345", out _, out _));
            Assert.IsFalse(BattleSettings.TryParse("25", value, "12345", out _, out _));
        }

        [TestCase(1)]
        [TestCase(25)]
        [TestCase(250)]
        public void SpawnIsRepeatableInsideBoundaryWithoutOverlap(int count)
        {
            var input = new BattleInput(new BattleSettings(count, count, 12345));
            var other = new BattleInput(new BattleSettings(count, count, 12345));

            for (var i = 0; i < input.Count; i++)
            {
                Assert.AreEqual(input[i].Position, other[i].Position);
                Assert.AreEqual(input[i].Delay, other[i].Delay);
                Assert.That(Mathf.Abs(input[i].Position.x) + BattleSettings.Radius, Is.LessThan(6.5f));
                Assert.That(Mathf.Abs(input[i].Position.y) + BattleSettings.Radius, Is.LessThan(2.65f));

                for (var j = 0; j < i; j++)
                    Assert.That(
                        Vector2.Distance(input[i].Position, input[j].Position),
                        Is.GreaterThanOrEqualTo(BattleSettings.Radius * 2));
            }
        }

        [Test]
        public void HitAtAPlus18KillsBothAndNeverHitsAfterFinish()
        {
            var input = Duel();
            using var battle = new BattleSimulation(input, new FixedPhysics(input));
            battle.Step();
            Assert.AreEqual(1, battle.Units[0].AttackStart);

            for (var i = 0; i < 17; i++)
                battle.Step();

            Assert.AreEqual(0, battle.HitCount);
            battle.Step();
            Assert.AreEqual(19, battle.StepIndex);
            Assert.AreEqual(2, battle.HitCount);
            Assert.AreEqual(BattleSimulation.Outcome.Draw, battle.Result);
            Assert.AreEqual(0, battle.Physics.BodyCount);

            for (var i = 0; i < 50; i++)
                battle.Step();

            Assert.AreEqual(19, battle.StepIndex);
            Assert.AreEqual(2, battle.HitCount);
        }

        [Test]
        public void ReattackIsExactlyAPlus36AndOneHitPerAttack()
        {
            var input = Duel();
            using var battle = new BattleSimulation(input, new FixedPhysics(input), true);

            for (var i = 0; i < 36; i++)
                battle.Step();

            Assert.AreEqual(1, battle.Units[0].AttackStart);
            Assert.AreEqual(2, battle.HitCount);
            battle.Step();
            Assert.AreEqual(37, battle.Units[0].AttackStart);

            for (var i = 0; i < 18; i++)
                battle.Step();

            Assert.AreEqual(4, battle.HitCount);
            Assert.AreEqual(2, battle.Physics.BodyCount);
        }

        [Test]
        public void TargetDepartureMissesAndDoesNotRetargetMidAttack()
        {
            var input = Duel();
            var physics = new FixedPhysics(input);
            using var battle = new BattleSimulation(input, physics);
            battle.Step();
            physics.Positions[1] = new Vector2(5, 0);

            for (var i = 0; i < 18; i++)
                battle.Step();

            Assert.AreEqual(0, battle.HitCount);
            Assert.AreEqual(1, battle.Units[0].Target);
            Assert.IsTrue(battle.Units[0].HitConsumed);
        }

        [Test]
        public void DuplicateHitsKillOnceAndDeadTargetRemainsLockedUntilCycleEnds()
        {
            var input = new BattleInput
                (new BattleSettings(2, 2, 1),
                new[]
                {
                    new BattleInput.Spawn(0, 0, Vector2.zero, 0),
                    new BattleInput.Spawn(1, 0, new Vector2(0.1f, 0), 0),
                    new BattleInput.Spawn(2, 1, new Vector2(0.2f, 0), 0),
                    new BattleInput.Spawn(3, 1, new Vector2(0.35f, 0), 100)
                });
            using var battle = new BattleSimulation(input, new FixedPhysics(input));

            for (var i = 0; i < 19; i++)
                battle.Step();

            Assert.AreEqual(1, battle.SoldierAlive);
            Assert.AreEqual(1, battle.OrcAlive);
            Assert.AreEqual(3, battle.HitCount);
            Assert.AreEqual(2, battle.Units[0].Target);

            for (var i = 0; i < 17; i++)
                battle.Step();

            Assert.AreEqual(2, battle.Units[0].Target);
            Assert.AreEqual(3, battle.HitCount);
            battle.Step();
            Assert.AreEqual(3, battle.Units[0].Target);
            Assert.AreEqual(37, battle.Units[0].AttackStart);
        }

        [Test]
        public void NearestTieUsesSmallestId()
        {
            var input = new BattleInput
                (new BattleSettings(1, 2, 1),
                new[]
                {
                    new BattleInput.Spawn(0, 0, Vector2.zero, 0),
                    new BattleInput.Spawn(1, 1, new Vector2(0.3f, 0), 100),
                    new BattleInput.Spawn(2, 1, new Vector2(-0.3f, 0), 100)
                });
            using var battle = new BattleSimulation(input, new FixedPhysics(input));
            battle.Step();
            Assert.AreEqual(1, battle.Units[0].Target);
        }

        [TestCase(0, BattleSimulation.Outcome.Soldier)]
        [TestCase(1, BattleSimulation.Outcome.Orc)]
        public void UnopposedFirstHitProducesCorrectWinner(int delayedTeam, BattleSimulation.Outcome expected)
        {
            var input = new BattleInput
                (new BattleSettings(1, 1, 1),
                new[]
                {
                    new BattleInput.Spawn(0, 0, new Vector2(-0.15f, 0), delayedTeam == 1 ? 100 : 0),
                    new BattleInput.Spawn(1, 1, new Vector2(0.15f, 0), delayedTeam == 0 ? 100 : 0)
                });
            using var battle = new BattleSimulation(input, new FixedPhysics(input));

            for (var i = 0; i < 19; i++)
                battle.Step();

            Assert.AreEqual(expected, battle.Result);
        }

        [Test]
        public void UnresolvedBattleTimesOutAt1800()
        {
            var input = new BattleInput(new BattleSettings(1, 1, 12345));
            using var battle = new BattleSimulation(input, new FixedPhysics(input));

            for (var i = 0; i < 1800; i++)
                battle.Step();

            Assert.AreEqual(BattleSimulation.Outcome.Timeout, battle.Result);
            Assert.AreEqual(1800, battle.StepIndex);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeBackendActuallyMovesQueriesAndReleases(bool core)
        {
            var input = new BattleInput(new BattleSettings(1, 1, 12345));
            var worlds = Unity.U2D.Physics.PhysicsWorld.worldCount;
            var mode = Physics2D.simulationMode;

            using (var battle = BattleComparisonEntryPoint.CreateBattle(input, core, false))
            {
                Assert.AreEqual(6, battle.Physics.BodyCount);

                for (var i = 0; i < 60; i++)
                    battle.Step();

                Assert.Greater(battle.Units[0].Position.x, input[0].Position.x);
                Assert.Less(battle.Units[1].Position.x, input[1].Position.x);

                while (battle.Result == BattleSimulation.Outcome.None)
                    battle.Step();

                Assert.Less(battle.Physics.BodyCount, 6);
            }

            Assert.AreEqual(mode, Physics2D.simulationMode);
            Assert.AreEqual(worlds, Unity.U2D.Physics.PhysicsWorld.worldCount);
        }

        /// <summary>
        /// 実際のアセットで攻撃の最終フレームと死亡の600ms保持を検査します。
        /// [KO] 실제 에셋의 공격 마지막 프레임과 사망 600ms 유지를 검사합니다.
        /// [EN] Checks attack clamping and the 600ms death hold using the actual assets.
        /// </summary>
        [TestCase("Soldier")]
        [TestCase("Orc")]
        public void ImportedAnimationKeepsTimingAndFootPivot(string character)
        {
            var ids = UnityEditor.AssetDatabase.FindAssets("t:UnitAnimationSet", new[] { "Assets/PhysicsComparisonLab" });
            UnitAnimationSet animation = null;

            foreach (var id in ids)
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitAnimationSet>(UnityEditor.AssetDatabase.GUIDToAssetPath(id));

                if (asset.name.Contains(character))
                    animation = asset;
            }

            Assert.IsNotNull(animation, character);
            var death0 = animation.Frame(UnitState.Action.Death, 0);
            var death1 = animation.Frame(UnitState.Action.Death, 0.1f);
            var death2 = animation.Frame(UnitState.Action.Death, 0.2f);
            var death3 = animation.Frame(UnitState.Action.Death, 0.3f);
            Assert.AreNotSame(death0, death1);
            Assert.AreNotSame(death1, death2);
            Assert.AreNotSame(death2, death3);
            Assert.AreSame(death3, animation.Frame(UnitState.Action.Death, 0.899f));
            Assert.AreSame(
                animation.Frame(UnitState.Action.Attack, 0.5f),
                animation.Frame(UnitState.Action.Attack, 0.599f));
            Assert.AreSame(animation.Frame(UnitState.Action.Idle, 0), animation.Frame(UnitState.Action.Idle, 0.6f));
            Assert.AreSame(animation.Frame(UnitState.Action.Walk, 0), animation.Frame(UnitState.Action.Walk, 0.8f));

            foreach (var sprite in new[] { death0, death1, death2, death3 })
            {
                Assert.AreEqual(new Vector2(50, 40), sprite.pivot);
                Assert.AreEqual(64, sprite.pixelsPerUnit);
                Assert.AreEqual(new Vector2(100, 100), sprite.rect.size);
                var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(sprite));
                Assert.AreEqual(FilterMode.Point, importer.filterMode);
                Assert.IsFalse(importer.mipmapEnabled);
                Assert.AreEqual(UnityEditor.TextureImporterCompression.Uncompressed, importer.textureCompression);
            }
        }

        /// <summary>
        /// 三千の配置を隣接cellだけで検査し、全組合せ比較を避けます。
        /// [KO] 3천 명 배치를 인접 cell만 검사해 전체 쌍 비교를 피합니다.
        /// [EN] Checks a 3,000-unit deployment through neighboring cells instead of all pairs.
        /// </summary>
        [Test]
        public void MaximumDeploymentIsBoundedRepeatableAndNonOverlapping()
        {
            Assert.IsTrue(BattleSettings.TryParse("1500", "1500", "12345", out var settings, out _));
            var input = new BattleInput(settings);
            var repeated = new BattleInput(settings);
            Assert.AreEqual(3000, input.Count);
            var cells = new System.Collections.Generic.Dictionary<Vector2Int, Vector2>();
            var diameter = BattleSettings.Radius * 2;

            for (var i = 0; i < input.Count; i++)
            {
                var point = input[i].Position;
                Assert.AreEqual(point, repeated[i].Position);
                Assert.AreEqual(input[i].Delay, repeated[i].Delay);
                Assert.Less(Mathf.Abs(point.x) + BattleSettings.Radius, input.HalfExtents.x);
                Assert.Less(Mathf.Abs(point.y) + BattleSettings.Radius, input.HalfExtents.y);
                var cell = Vector2Int.FloorToInt(point / diameter);

                for (var y = -1; y <= 1; y++)
                {
                    for (var x = -1; x <= 1; x++)
                    {
                        if (cells.TryGetValue(cell + new Vector2Int(x, y), out var other))
                            Assert.GreaterOrEqual((point - other).sqrMagnitude, diameter * diameter);
                    }
                }

                cells.Add(cell, point);
            }
        }

        /// <summary>
        /// 空間検索を総当たりの正解と比べ、移動・死亡・同距離を含めます。
        /// [KO] 공간 검색을 전체 탐색 정답과 비교하며 이동, 사망, 같은 거리를 포함합니다.
        /// [EN] Compares spatial queries with exhaustive answers including movement, deaths, and ties.
        /// </summary>
        [Test]
        public void SpatialSearchMatchesExactNearestAcrossMovementAndDeaths()
        {
            var input = new BattleInput(new BattleSettings(250, 250, 12345));
            var units = new UnitState[input.Count];
            var random = new System.Random(8);

            for (var i = 0; i < units.Length; i++)
                units[i] = new UnitState(input[i]);

            var index = new BattleTargetIndex(units);

            for (var step = 0; step < 3; step++)
            {
                for (var i = 0; i < units.Length; i++)
                {
                    units[i].Position = new Vector2(random.Next(-20, 21), random.Next(-20, 21));
                    units[i].Alive = i % 7 != step;
                }

                foreach (var unit in units)
                {
                    if (!unit.Alive)
                        continue;

                    var expected = -1;
                    var closest = float.PositiveInfinity;

                    for (var j = 0; j < units.Length; j++)
                    {
                        if (!units[j].Alive || units[j].Team == unit.Team)
                            continue;

                        var distance = (units[j].Position - unit.Position).sqrMagnitude;

                        if (distance < closest)
                        {
                            closest = distance;
                            expected = j;
                        }
                    }

                    Assert.AreEqual(expected, index.FindNearest(unit, step));
                }
            }
        }
    }
}
