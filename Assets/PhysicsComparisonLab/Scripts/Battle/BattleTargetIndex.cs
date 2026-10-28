using UnityEngine;

namespace PhysicsComparisonLab.Battle
{
    /// <summary>
    /// 各陣営の生存位置を平衡木にまとめ、正確な最近傍を検索します。
    /// [KO] 진영별 생존 위치를 균형 트리로 구성해 정확한 최근접 적을 검색합니다.
    /// [EN] Builds balanced per-team spatial trees for exact nearest-enemy queries.
    /// </summary>
    internal sealed class BattleTargetIndex
    {
        private readonly UnitState[] _units;
        private readonly int[] _ids;
        private readonly Vector2[] _minimum;
        private readonly Vector2[] _maximum;
        private readonly int[] _starts = new int[2];
        private readonly int[] _ends = new int[2];
        private int _builtStep = -1;

        /// <summary>
        /// 最大人数分を一度確保し、step中に配列を増やしません。
        /// [KO] 최대 인원만큼 한 번 할당해 step 중 배열을 늘리지 않습니다.
        /// [EN] Allocates capacity once so steps never grow the buffers.
        /// </summary>
        public BattleTargetIndex(UnitState[] units)
        {
            _units = units;
            _ids = new int[units.Length];
            _minimum = new Vector2[units.Length];
            _maximum = new Vector2[units.Length];
        }

        /// <summary>
        /// 同じstepの最初の検索だけ現在位置から木を作ります。
        /// [KO] 같은 step의 첫 검색에서만 현재 위치로 트리를 만듭니다.
        /// [EN] Rebuilds from current positions only on the first query of a step.
        /// </summary>
        public int FindNearest(UnitState attacker, int step)
        {
            if (_builtStep != step)
            {
                var count = 0;

                for (var team = 0; team < 2; team++)
                {
                    _starts[team] = count;

                    for (var i = 0; i < _units.Length; i++)
                    {
                        if (_units[i].Alive && _units[i].Team == team)
                            _ids[count++] = i;
                    }

                    _ends[team] = count;
                    Build(_starts[team], count, 0);
                }

                _builtStep = step;
            }

            var target = -1;
            var distance = float.PositiveInfinity;
            var enemy = 1 - attacker.Team;
            Search(_starts[enemy], _ends[enemy], attacker.Position, ref target, ref distance);

            return target;
        }

        /// <summary>
        /// 中央値で分割し、枝全体の境界を保存します。
        /// [KO] 중앙값으로 분할하고 각 가지 전체의 경계를 저장합니다.
        /// [EN] Partitions at medians and stores bounding boxes for whole branches.
        /// </summary>
        private void Build(int start, int end, int axis)
        {
            if (start >= end)
                return;

            var middle = (start + end) / 2;
            Select(start, end - 1, middle, axis);
            Build(start, middle, 1 - axis);
            Build(middle + 1, end, 1 - axis);
            var minimum = _units[_ids[middle]].Position;
            var maximum = minimum;

            if (start < middle)
            {
                var child = (start + middle) / 2;
                minimum = Vector2.Min(minimum, _minimum[child]);
                maximum = Vector2.Max(maximum, _maximum[child]);
            }

            if (middle + 1 < end)
            {
                var child = (middle + 1 + end) / 2;
                minimum = Vector2.Min(minimum, _minimum[child]);
                maximum = Vector2.Max(maximum, _maximum[child]);
            }

            _minimum[middle] = minimum;
            _maximum[middle] = maximum;
        }

        /// <summary>
        /// 配列内で選択し、全段階の完全ソートを避けます。
        /// [KO] 배열 안에서 선택해 모든 단계의 전체 정렬을 피합니다.
        /// [EN] Selects in place instead of fully sorting at every tree level.
        /// </summary>
        private void Select(int low, int high, int wanted, int axis)
        {
            while (low < high)
            {
                var pivot = _ids[(low + high) / 2];
                var left = low;
                var right = high;

                while (left <= right)
                {
                    while (Compare(_ids[left], pivot, axis) < 0)
                        left++;

                    while (Compare(_ids[right], pivot, axis) > 0)
                        right--;

                    if (left <= right)
                    {
                        (_ids[left], _ids[right]) = (_ids[right], _ids[left]);
                        left++;
                        right--;
                    }
                }

                if (wanted <= right)
                    high = right;
                else if (wanted >= left)
                    low = left;
                else
                    return;
            }
        }

        /// <summary>
        /// 同じ座標もIDで一意に分割します。
        /// [KO] 같은 좌표도 ID로 유일하게 분할합니다.
        /// [EN] Uses IDs to partition equal coordinates unambiguously.
        /// </summary>
        private int Compare(int left, int right, int axis)
        {
            var a = _units[left].Position;
            var b = _units[right].Position;
            var result = a[axis].CompareTo(b[axis]);

            if (result == 0)
                result = a[1 - axis].CompareTo(b[1 - axis]);

            return result == 0 ? left.CompareTo(right) : result;
        }

        /// <summary>
        /// 近い枝から探索し、同距離の低ID候補も残します。
        /// [KO] 가까운 가지부터 탐색하고 같은 거리의 낮은 ID 후보도 유지합니다.
        /// [EN] Searches nearer branches first while retaining equal-distance lower IDs.
        /// </summary>
        private void Search(int start, int end, Vector2 point, ref int target, ref float closest)
        {
            if (start >= end)
                return;

            var middle = (start + end) / 2;

            if (BoxDistance(middle, point) > closest)
                return;

            var id = _ids[middle];
            var distance = (_units[id].Position - point).sqrMagnitude;

            if (distance < closest || distance == closest && (target < 0 || id < target))
            {
                closest = distance;
                target = id;
            }

            var leftDistance = start < middle ? BoxDistance((start + middle) / 2, point) : float.PositiveInfinity;
            var rightDistance = middle + 1 < end ? BoxDistance((middle + 1 + end) / 2, point) : float.PositiveInfinity;

            if (leftDistance <= rightDistance)
            {
                Search(start, middle, point, ref target, ref closest);
                Search(middle + 1, end, point, ref target, ref closest);
            }
            else
            {
                Search(middle + 1, end, point, ref target, ref closest);
                Search(start, middle, point, ref target, ref closest);
            }
        }

        /// <summary>
        /// 点と枝境界の最短距離の二乗を返します。
        /// [KO] 점과 가지 경계 사이 최단 거리의 제곱을 반환합니다.
        /// [EN] Returns squared minimum distance from a point to a branch bounding box.
        /// </summary>
        private float BoxDistance(int middle, Vector2 point)
        {
            var x = Mathf.Max(Mathf.Max(_minimum[middle].x - point.x, 0), point.x - _maximum[middle].x);
            var y = Mathf.Max(Mathf.Max(_minimum[middle].y - point.y, 0), point.y - _maximum[middle].y);

            return x * x + y * y;
        }
    }
}
