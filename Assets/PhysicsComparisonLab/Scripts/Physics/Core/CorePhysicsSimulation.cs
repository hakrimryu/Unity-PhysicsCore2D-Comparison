using System;
using Unity.Collections;
using Unity.U2D.Physics;
using UnityEngine;
using PhysicsComparisonLab.Battle;
using PhysicsComparisonLab.Simulation;

namespace PhysicsComparisonLab.Core
{
    /// <summary>
    /// 専用Core worldと所有キー付きbodyを管理します。
    /// [KO] 전용 Core world와 소유 키가 있는 body를 관리합니다.
    /// [EN] Manages a dedicated Core world and owner-key protected bodies.
    /// </summary>
    public sealed class CorePhysicsSimulation : IPhysicsSimulation
    {
        private PhysicsWorld _world;
        private readonly PhysicsBody[] _bodies;
        private readonly PhysicsShape[] _shapes;
        private readonly int _key;
        private bool _disposed;
        private readonly BattleInput _input;
        private int _created;
        private bool _wallsCreated;

        /// <inheritdoc />
        public int BodyCount
            => _disposed ? 0 : _world.counters.bodyCount;

        /// <inheritdoc />
        public int ShapeCount
            => _disposed ? 0 : _world.counters.shapeCount;

        /// <inheritdoc />
        public string Configuration
            => $"Core Script; substeps={_world.simulationSubSteps}; workers={_world.simulationWorkers}; sleep={_world.sleepingAllowed}; awake={_world.awakeBodyCount}; contactFrequency={_world.contactFrequency}; contactDamping={_world.contactDamping}; contactSpeed={_world.contactSpeed}; density=1/(pi*r*r); automatic CCD threshold=0.5";

        /// <summary>
        /// 専用worldに両軍と四境界を作り、所有キーを保持します。
        /// [KO] 전용 world에 양 군과 네 경계를 만들고 소유 키를 보관합니다.
        /// [EN] Creates both armies and four bounds in a dedicated world and retains its ownership key.
        /// </summary>
        public CorePhysicsSimulation(BattleInput input, bool deferred = false)
        {
            _input = input;
            _bodies = new PhysicsBody[input.Count];
            _shapes = new PhysicsShape[input.Count];
            var definition = PhysicsWorldDefinition.defaultDefinition;
            definition.gravity = Vector2.zero;
            definition.simulationType = PhysicsWorld.SimulationType.Script;
            definition.drawOptions = PhysicsWorld.DrawOptions.Off;
            _world = PhysicsWorld.Create(definition);
            _key = _world.SetOwner(null);

            try
            {
                if (!deferred)
                    CreateNextBatch(input.Count);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 一部のbodyを生成してUIへ制御を返せるようにします。
        /// [KO] body 일부를 생성해 UI에 제어를 돌려줄 수 있도록 합니다.
        /// [EN] Creates a bounded batch so preparation can yield control to the UI.
        /// </summary>
        public bool CreateNextBatch(int maximum)
        {
            if (_disposed || maximum < 1)
                throw new InvalidOperationException("Invalid construction batch.");

            if (_wallsCreated)
                return true;

            var input = _input;
            var end = Math.Min(input.Count, _created + maximum);
            var shapeDefinition = PhysicsShapeDefinition.defaultDefinition;
            shapeDefinition.density = 1 / (Mathf.PI * BattleSettings.Radius * BattleSettings.Radius);
            shapeDefinition.surfaceMaterial.friction = 0;
            shapeDefinition.surfaceMaterial.bounciness = 0;

            for (var i = _created; i < end; i++)
            {
                var bodyDefinition = PhysicsBodyDefinition.defaultDefinition;
                bodyDefinition.type = PhysicsBody.BodyType.Dynamic;
                bodyDefinition.position = input[i].Position;
                bodyDefinition.constraints = PhysicsBody.BodyConstraints.Rotation;
                bodyDefinition.linearDamping = 0;
                bodyDefinition.angularDamping = 0;
                _bodies[i] = _world.CreateBody(bodyDefinition);
                _bodies[i].SetOwner(null, _key);
                _shapes[i] = _bodies[i].CreateShape(CircleGeometry.Create(BattleSettings.Radius), shapeDefinition);
                _created++;
            }

            // 全員の生成が終わったbatchだけ、共通境界を一度追加する。
            if (_created != input.Count)
                return false;

            CreateWall(
                new Vector2(-input.HalfExtents.x - 0.1f, 0),
                new Vector2(0.2f, input.HalfExtents.y * 2 + 0.4f),
                shapeDefinition);
            CreateWall(
                new Vector2(input.HalfExtents.x + 0.1f, 0),
                new Vector2(0.2f, input.HalfExtents.y * 2 + 0.4f),
                shapeDefinition);
            CreateWall(
                new Vector2(0, -input.HalfExtents.y - 0.1f),
                new Vector2(input.HalfExtents.x * 2, 0.2f),
                shapeDefinition);
            CreateWall(
                new Vector2(0, input.HalfExtents.y + 0.1f),
                new Vector2(input.HalfExtents.x * 2, 0.2f),
                shapeDefinition);
            _wallsCreated = true;

            return true;
        }

        /// <summary>
        /// 所有する物理世界へ静的な境界を追加します。
        /// [KO] 소유 물리 세계에 정적 경계를 추가합니다.
        /// [EN] Adds a static boundary to the owned physics environment.
        /// </summary>
        private void CreateWall(Vector2 position, Vector2 size, PhysicsShapeDefinition shapeDefinition)
        {
            var definition = PhysicsBodyDefinition.defaultDefinition;
            definition.type = PhysicsBody.BodyType.Static;
            definition.position = position;
            var body = _world.CreateBody(definition);
            body.SetOwner(null, _key);
            body.CreateShape(PolygonGeometry.CreateBox(size), shapeDefinition);
        }

        /// <inheritdoc />
        public void SetVelocity(int id, Vector2 velocity)
        {
            _bodies[id].linearVelocity = velocity;
        }

        /// <inheritdoc />
        public void Step(float seconds)
        {
            if (_disposed || seconds <= 0 || !float.IsFinite(seconds))
                throw new InvalidOperationException("Invalid Core step.");

            // この版のSimulateは同期呼び出し。戻った後で結果を読み、別のStepを重ねない。
            _world.Simulate(seconds);
        }

        /// <inheritdoc />
        public void CopyPositions(UnitState[] units)
        {
            for (var i = 0; i < units.Length; i++)
            {
                if (units[i].Alive)
                    units[i].Position = _bodies[i].position;
            }
        }

        /// <inheritdoc />
        public bool Overlaps(int attacker, int target, float range)
        {
            // 6.5のworld overlapは可変長NativeArrayを返すため固定長切り捨てはない。
            var hits = _world.OverlapGeometry(
                CircleGeometry.Create(range, _bodies[attacker].position),
                PhysicsQuery.QueryFilter.Everything,
                Allocator.Temp);

            try
            {
                for (var i = 0; i < hits.Length; i++)
                {
                    if (hits[i].shape == _shapes[target])
                        return true;
                }

                return false;
            }
            finally
            {
                if (hits.IsCreated)
                    hits.Dispose();
            }
        }

        /// <inheritdoc />
        public void Remove(int id)
        {
            if (_bodies[id].isValid && !_bodies[id].Destroy(_key))
                throw new InvalidOperationException("Core body removal refused.");
        }

        /// <summary>
        /// 自分が所有する資源だけを解放します。
        /// [KO] 직접 소유한 자원만 해제합니다.
        /// [EN] Releases only resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_world.isValid && !_world.Destroy(_key))
                throw new InvalidOperationException("Core world removal refused.");
        }
    }
}
