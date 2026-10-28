using System;
using UnityEngine;
using PhysicsComparisonLab.Battle;
using PhysicsComparisonLab.Simulation;

namespace PhysicsComparisonLab.Legacy
{
    /// <summary>
    /// 自身のGameObjectと手動Physics2D実行を所有します。
    /// [KO] 자체 GameObject와 수동 Physics2D 실행을 소유합니다.
    /// [EN] Owns its GameObjects and manual Physics2D stepping.
    /// </summary>
    public sealed class LegacyPhysicsSimulation : IPhysicsSimulation
    {
        private readonly Rigidbody2D[] _bodies;
        private readonly CircleCollider2D[] _shapes;
        private readonly Collider2D[] _query;
        private readonly GameObject _root;
        private readonly PhysicsMaterial2D _material;
        private readonly SimulationMode2D _previousMode;
        private readonly ContactFilter2D _filter = ContactFilter2D.noFilter;
        private int _alive;
        private bool _disposed;
        private readonly BattleInput _input;
        private int _created;
        private bool _wallsCreated;

        /// <inheritdoc />
        public int BodyCount
            => _disposed ? 0 : _alive + 4;

        /// <inheritdoc />
        public int ShapeCount
            => BodyCount;

        /// <inheritdoc />
        public string Configuration
            => $"Physics2D Script; velocity iterations={Physics2D.velocityIterations}; position iterations={Physics2D.positionIterations}; discrete; sleep=StartAwake; mass=1";

        /// <summary>
        /// 自動stepを止め、両軍と四境界を所有rootに作ります。
        /// [KO] 자동 step을 멈추고 소유 root에 양 군과 네 경계를 만듭니다.
        /// [EN] Disables automatic stepping and creates both armies and four bounds under the owned root.
        /// </summary>
        public LegacyPhysicsSimulation(BattleInput input, bool deferred = false)
        {
            _input = input;
            _previousMode = Physics2D.simulationMode;
            _bodies = new Rigidbody2D[input.Count];
            _shapes = new CircleCollider2D[input.Count];
            _query = new Collider2D[input.Count + 5];
            _root = new GameObject("LegacyPhysicsBodies");
            _material = new PhysicsMaterial2D("Battle frictionless") { friction = 0, bounciness = 0 };

            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;

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

            for (var i = _created; i < end; i++)
            {
                var go = NewObject("Body_" + i, input[i].Position);
                var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Dynamic;
                body.gravityScale = 0;
                body.mass = 1;
                body.linearDamping = 0;
                body.angularDamping = 0;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.interpolation = RigidbodyInterpolation2D.None;
                body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
                body.sleepMode = RigidbodySleepMode2D.StartAwake;
                var shape = go.AddComponent<CircleCollider2D>();
                shape.radius = BattleSettings.Radius;
                shape.sharedMaterial = _material;
                _bodies[i] = body;
                _shapes[i] = shape;
                _alive++;
                _created++;
            }

            // 全員の生成が終わったbatchだけ、共通境界を一度追加する。
            if (_created != input.Count)
                return false;

            CreateWall(new Vector2(-input.HalfExtents.x - 0.1f, 0), new Vector2(0.2f, input.HalfExtents.y * 2 + 0.4f));
            CreateWall(new Vector2(input.HalfExtents.x + 0.1f, 0), new Vector2(0.2f, input.HalfExtents.y * 2 + 0.4f));
            CreateWall(new Vector2(0, -input.HalfExtents.y - 0.1f), new Vector2(input.HalfExtents.x * 2, 0.2f));
            CreateWall(new Vector2(0, input.HalfExtents.y + 0.1f), new Vector2(input.HalfExtents.x * 2, 0.2f));
            Physics2D.SyncTransforms();
            _wallsCreated = true;

            return true;
        }

        /// <summary>
        /// Legacy専用rootの下に物理オブジェクトを作ります。
        /// [KO] Legacy 전용 root 아래 물리 객체를 만듭니다.
        /// [EN] Creates a physics object under the Legacy root.
        /// </summary>
        private GameObject NewObject(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = position;

            return go;
        }

        /// <summary>
        /// 所有する物理世界へ静的な境界を追加します。
        /// [KO] 소유 물리 세계에 정적 경계를 추가합니다.
        /// [EN] Adds a static boundary to the owned physics environment.
        /// </summary>
        private void CreateWall(Vector2 position, Vector2 size)
        {
            var go = NewObject("Boundary", position);
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
            box.sharedMaterial = _material;
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
                throw new InvalidOperationException("Invalid Legacy step.");

            if (!Physics2D.Simulate(seconds))
                throw new InvalidOperationException("Physics2D rejected manual simulation.");
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
            var count = Physics2D.OverlapCircle(_bodies[attacker].position, range, _filter, _query);

            if (count == _query.Length)
                throw new InvalidOperationException("Legacy overlap buffer saturated.");

            for (var i = 0; i < count; i++)
            {
                if (_query[i] == _shapes[target])
                    return true;
            }

            return false;
        }

        /// <inheritdoc />
        public void Remove(int id)
        {
            if (_bodies[id] == null)
                return;

            _bodies[id].simulated = false;
            DestroyOwned(_bodies[id].gameObject);
            _bodies[id] = null;
            _shapes[id] = null;
            _alive--;
        }

        /// <summary>
        /// 即座に物理参加を止め、終了フレームで表示外オブジェクトを破棄します。
        /// [KO] 즉시 물리 참여를 중단하고 프레임 끝에 비표시 객체를 제거합니다.
        /// [EN] Stops physics participation immediately and destroys objects at frame end.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (_root != null)
                {
                    _root.SetActive(false);
                    DestroyOwned(_root);
                }

                if (_material != null)
                    DestroyOwned(_material);
            }
            finally
            {
                Physics2D.simulationMode = _previousMode;
            }
        }

        /// <summary>
        /// 実行中は遅延破棄し、Editor検証中は即時破棄します。
        /// [KO] 실행 중에는 지연 파괴하고 Editor 검증 중에는 즉시 파괴합니다.
        /// [EN] Uses deferred destruction in Play mode and immediate destruction in Editor validation.
        /// </summary>
        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
