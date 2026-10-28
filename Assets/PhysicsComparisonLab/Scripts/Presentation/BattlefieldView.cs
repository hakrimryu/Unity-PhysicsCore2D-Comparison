using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.U2D;
using PhysicsComparisonLab.Battle;

namespace PhysicsComparisonLab.Presentation
{
    /// <summary>
    /// 全軍をUIの内側へ収め、同じTile資産で必要な地面を拡張します。
    /// [KO] 전체 병력을 UI 안쪽에 맞추고 같은 Tile 에셋으로 필요한 지면을 확장합니다.
    /// [EN] Fits both armies inside the UI margins and expands terrain with the same Tile assets.
    /// </summary>
    public sealed class BattlefieldView : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Tilemap _ground;
        [SerializeField] private TileBase[] _grass;
        private readonly Dictionary<Vector3Int, TileBase> _paintedChanges = new Dictionary<Vector3Int, TileBase>();
        private UnitState[] _units;
        private Vector2 _field;
        private float _timer;
        private int _halfWidth;
        private int _halfHeight;
        private int _screenWidth;
        private int _screenHeight;

        public float CameraSize
            => _camera.orthographicSize;

        public BoundsInt GroundBounds
            => _ground.cellBounds;

        /// <summary>
        /// 初期パターンとの差だけ保存し、手描き修正を実行中の拡張にも残します。
        /// [KO] 초기 패턴과의 차이만 저장해 수동으로 칠한 수정을 실행 중 확장에도 유지합니다.
        /// [EN] Retains painted changes relative to the original pattern across runtime resizing.
        /// </summary>
        private void Awake()
        {
            var bounds = _ground.cellBounds;
            bounds.xMin = Mathf.Min(bounds.xMin, -7);
            bounds.xMax = Mathf.Max(bounds.xMax, 7);
            bounds.yMin = Mathf.Min(bounds.yMin, -3);
            bounds.yMax = Mathf.Max(bounds.yMax, 4);

            foreach (var cell in bounds.allPositionsWithin)
            {
                TileBase expected = null;

                if (cell.x >= -7 && cell.x <= 6 && cell.y >= -3 && cell.y <= 3)
                {
                    var column =
                        cell.x == -7 ? 0 :
                        cell.x == 6 ? 2 :
                        1;
                    var row =
                        cell.y == 3 ? 0 :
                        cell.y == -3 ? 2 :
                        1;
                    expected = _grass[row * 3 + column];
                }

                var actual = _ground.GetTile(cell);

                if (actual != expected)
                    _paintedChanges[cell] = actual;
            }

            // 広域表示では整数拡大だけでは全員を収められない。Point filterは維持する。
            var pixel = _camera.GetComponent<PixelPerfectCamera>();

            if (pixel != null)
                pixel.enabled = false;
        }

        /// <summary>
        /// 新しい配置へ即座に合わせ、前回の広い地面を縮め直します。
        /// [KO] 새 배치에 즉시 맞추고 이전의 넓은 지면을 다시 줄입니다.
        /// [EN] Immediately fits a new deployment and shrinks terrain left by the previous run.
        /// </summary>
        public void Configure(BattleInput input, UnitState[] units)
        {
            _units = units;
            _field = input.HalfExtents;
            _halfWidth = _halfHeight = 0;
            Fit(true);
        }

        private void LateUpdate()
        {
            _timer += Time.unscaledDeltaTime;

            if (_units != null && (_timer >= 0.25f || _screenWidth != Screen.width || _screenHeight != Screen.height))
            {
                _timer = 0;
                Fit(false);
            }
        }

        /// <summary>
        /// 生存者の足元範囲とsprite余白に、上下UIの占有率を加えます。
        /// [KO] 생존자의 발 위치 범위와 sprite 여백에 상하 UI 점유율을 반영합니다.
        /// [EN] Fits survivor foot bounds plus sprite padding within the top and bottom UI margins.
        /// </summary>
        private void Fit(bool immediate)
        {
            var minimum = Vector2.positiveInfinity;
            var maximum = Vector2.negativeInfinity;
            var any = false;

            foreach (var unit in _units)
            {
                if (!unit.Alive)
                    continue;

                minimum = Vector2.Min(minimum, unit.Position);
                maximum = Vector2.Max(maximum, unit.Position);
                any = true;
            }

            if (!any)
                return;

            var center = (minimum + maximum) * 0.5f + new Vector2(0, 0.2f);
            var half = (maximum - minimum) * 0.5f + new Vector2(1.1f, 1.1f);
            var aspect = _camera.aspect;
            var size = Mathf.Max(2.1f, Mathf.Max(half.y / 0.66f, half.x / (aspect * 0.96f)));
            _camera.orthographicSize = immediate || size > _camera.orthographicSize ? size : Mathf.Lerp(_camera.orthographicSize, size, 0.2f);
            _camera.transform.position = new Vector3(center.x, center.y, -10);
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            var halfWidth = Mathf.CeilToInt(Mathf.Max(_field.x + 1, Mathf.Abs(center.x) + _camera.orthographicSize * aspect)) + 1;
            var halfHeight = Mathf.CeilToInt(Mathf.Max(_field.y + 1, Mathf.Abs(center.y) + _camera.orthographicSize)) + 1;

            if (halfWidth > _halfWidth || halfHeight > _halfHeight)
                Paint(Mathf.Max(halfWidth, _halfWidth), Mathf.Max(halfHeight, _halfHeight));
        }

        /// <summary>
        /// まとめてTileを書き、保存済み手描き変更を最後に重ねます。
        /// [KO] Tile을 일괄 기록하고 저장된 수동 변경을 마지막에 덮어씁니다.
        /// [EN] Writes tiles in a batch and reapplies preserved manual changes last.
        /// </summary>
        private void Paint(int halfWidth, int halfHeight)
        {
            _halfWidth = halfWidth;
            _halfHeight = halfHeight;
            var bounds = new BoundsInt(-halfWidth, -halfHeight, 0, halfWidth * 2, halfHeight * 2, 1);
            var tiles = new TileBase[bounds.size.x * bounds.size.y];
            var index = 0;

            for (var y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (var x = bounds.xMin; x < bounds.xMax; x++)
                {
                    var row =
                        y == bounds.yMax - 1 ? 0 :
                        y == bounds.yMin ? 2 :
                        1;
                    var column =
                        x == bounds.xMin ? 0 :
                        x == bounds.xMax - 1 ? 2 :
                        1;
                    tiles[index++] = _grass[row * 3 + column];
                }
            }

            _ground.ClearAllTiles();
            _ground.SetTilesBlock(bounds, tiles);

            foreach (var painted in _paintedChanges)
                _ground.SetTile(painted.Key, painted.Value);

            _ground.CompressBounds();
        }
    }
}
