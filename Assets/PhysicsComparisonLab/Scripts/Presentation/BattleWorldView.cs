using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PhysicsComparisonLab.Battle;

namespace PhysicsComparisonLab.Presentation
{
    /// <summary>
    /// 共通フレームを一つのmeshへまとめ、ユニットごとの表示Objectを作りません。
    /// [KO] 공통 프레임을 mesh 하나로 묶어 유닛마다 표시 객체를 만들지 않습니다.
    /// [EN] Batches shared sprite frames into one mesh without per-unit view objects.
    /// </summary>
    public sealed class BattleWorldView : IDisposable
    {
        private readonly struct FrameGeometry
        {
            public readonly Vector4 Uv;
            public readonly Vector2 Size;
            public readonly Vector2 Pivot;

            public FrameGeometry(Sprite sprite)
            {
                var rect = sprite.rect;
                Uv = new Vector4
                    (rect.xMin / sprite.texture.width,
                    rect.yMin / sprite.texture.height,
                    rect.xMax / sprite.texture.width,
                    rect.yMax / sprite.texture.height);

                // PPU64の元画像を表示だけ2倍にする。物理半径や足元座標は変えない。
                Size = rect.size * (2 / sprite.pixelsPerUnit);
                Pivot = sprite.pivot * (2 / sprite.pixelsPerUnit);
            }
        }

        private readonly GameObject _root;
        private readonly Mesh _mesh;
        private readonly Material[] _materials = new Material[8];
        private readonly Vector3[] _vertices;
        private readonly Vector2[] _uv;
        private readonly int[][] _indices = new int[8][];
        private readonly int[] _counts = new int[8];
        private readonly bool[] _flip;
        private readonly UnitAnimationSet[] _animations;
        private readonly Dictionary<Sprite, FrameGeometry> _geometry = new Dictionary<Sprite, FrameGeometry>();
        private bool _disposed;

        public int ViewCount { get; private set; }

        /// <summary>
        /// 入力数に合わせた固定bufferと八つの画像materialを準備します。
        /// [KO] 입력 수에 맞춘 고정 buffer와 이미지 material 여덟 개를 준비합니다.
        /// [EN] Prepares fixed buffers and eight sheet materials for the input size.
        /// </summary>
        public BattleWorldView(UnitState[] units, UnitAnimationSet soldier, UnitAnimationSet orc)
        {
            _animations = new[] { soldier, orc };
            _vertices = new Vector3[units.Length * 4];
            _uv = new Vector2[_vertices.Length];
            _flip = new bool[units.Length];
            var shader = Resources.Load<Shader>("BattleUnits");

            if (shader == null)
                throw new InvalidOperationException("BattleUnits shader is missing.");

            _root = new GameObject("BattleUnitViews", typeof(MeshFilter), typeof(MeshRenderer));
            _mesh = new Mesh
            {
                name = "Shared battle sprites",
                indexFormat = IndexFormat.UInt32,
                subMeshCount = 8
            };
            _mesh.MarkDynamic();
            _root.GetComponent<MeshFilter>().sharedMesh = _mesh;

            for (var group = 0; group < 8; group++)
            {
                var animation = _animations[group / 4];
                var action = (UnitState.Action)(group % 4);
                var first = animation.Frame(action, 0);
                _materials[group] = new Material(shader) { name = first.texture.name, mainTexture = first.texture };
                _indices[group] = new int[units.Length * 6];

                for (var frame = 0; frame < 8; frame++)
                {
                    var sprite = animation.Frame(action, frame * 0.1f);

                    if (!_geometry.ContainsKey(sprite))
                        _geometry.Add(sprite, new FrameGeometry(sprite));
                }
            }

            var renderer = _root.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = _materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            for (var i = 0; i < units.Length; i++)
                _flip[i] = units[i].Team == 1;

            Refresh(units, 0);
        }

        /// <summary>
        /// 全員の位置・フレームを同じmeshへ書き、死亡完了後はindexを除外します。
        /// [KO] 모든 위치와 프레임을 같은 mesh에 쓰고 사망 표시가 끝나면 index에서 제외합니다.
        /// [EN] Writes all positions and frames into one mesh and omits finished death views.
        /// </summary>
        public void Refresh(UnitState[] units, float visualStep)
        {
            Array.Clear(_counts, 0, _counts.Length);
            ViewCount = 0;
            var minimum = Vector2.positiveInfinity;
            var maximum = Vector2.negativeInfinity;

            for (var i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                var elapsed = Mathf.Max(0, visualStep - unit.ActionStart) * BattleSettings.StepSeconds;

                if (!unit.Alive && elapsed >= 0.9f)
                    continue;

                if (Mathf.Abs(unit.Velocity.x) > 0.001f)
                    _flip[i] = unit.Velocity.x < 0;
                else if (unit.Alive && unit.Target >= 0)
                {
                    var delta = units[unit.Target].Position.x - unit.Position.x;

                    if (Mathf.Abs(delta) > 0.001f)
                        _flip[i] = delta < 0;
                }

                var sprite = _animations[unit.Team].Frame(unit.CurrentAction, elapsed);
                var frame = _geometry[sprite];
                var vertex = ViewCount * 4;
                var left = unit.Position.x - (_flip[i] ? frame.Size.x - frame.Pivot.x : frame.Pivot.x);
                var bottom = unit.Position.y - frame.Pivot.y;

                // 深度は足元yから作る。透明pixelはdiscardして背後の兵士を隠さない。
                var depth = unit.Position.y * 0.001f;
                _vertices[vertex] = new Vector3(left, bottom, depth);
                _vertices[vertex + 1] = new Vector3(left + frame.Size.x, bottom, depth);
                _vertices[vertex + 2] = new Vector3(left + frame.Size.x, bottom + frame.Size.y, depth);
                _vertices[vertex + 3] = new Vector3(left, bottom + frame.Size.y, depth);

                // 左右反転はUVへ適用し、物理bodyの位置や形状を反転しない。
                var u0 = _flip[i] ? frame.Uv.z : frame.Uv.x;
                var u1 = _flip[i] ? frame.Uv.x : frame.Uv.z;
                _uv[vertex] = new Vector2(u0, frame.Uv.y);
                _uv[vertex + 1] = new Vector2(u1, frame.Uv.y);
                _uv[vertex + 2] = new Vector2(u1, frame.Uv.w);
                _uv[vertex + 3] = new Vector2(u0, frame.Uv.w);

                // 陣営2種×行動4種で同じtextureをまとめ、quadごとに二つの三角形を登録する。
                var group = unit.Team * 4 + (int)unit.CurrentAction;
                var count = _counts[group];
                var indices = _indices[group];
                indices[count] = vertex;
                indices[count + 1] = vertex + 1;
                indices[count + 2] = vertex + 2;
                indices[count + 3] = vertex;
                indices[count + 4] = vertex + 2;
                indices[count + 5] = vertex + 3;
                _counts[group] += 6;
                ViewCount++;
                minimum = Vector2.Min(minimum, new Vector2(left, bottom));
                maximum = Vector2.Max(maximum, new Vector2(left + frame.Size.x, bottom + frame.Size.y));
            }

            // 頂点数を固定してsubmeshの一時的な範囲外indexを避ける。
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uv);

            for (var group = 0; group < 8; group++)
                _mesh.SetTriangles(_indices[group], 0, _counts[group], group, false);

            _mesh.bounds = ViewCount == 0
                ? new Bounds(Vector3.zero, Vector3.zero)
                : new Bounds((minimum + maximum) / 2, new Vector3(maximum.x - minimum.x, maximum.y - minimum.y, 2));
        }

        /// <summary>
        /// 所有mesh、material、表示rootを解放します。
        /// [KO] 소유 mesh, material과 표시 root를 해제합니다.
        /// [EN] Releases the owned mesh, materials, and view root.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_root != null)
                _root.SetActive(false);

            DestroyOwned(_root);
            DestroyOwned(_mesh);

            foreach (var material in _materials)
                DestroyOwned(material);

            ViewCount = 0;
        }

        /// <summary>
        /// 所有表示資源を実行中は遅延破棄し、Editorでは即時破棄します。
        /// [KO] 소유 표시 자원을 실행 중에는 지연 파괴하고 Editor에서는 즉시 파괴합니다.
        /// [EN] Destroys owned display resources at frame end in Play mode or immediately in the Editor.
        /// </summary>
        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
