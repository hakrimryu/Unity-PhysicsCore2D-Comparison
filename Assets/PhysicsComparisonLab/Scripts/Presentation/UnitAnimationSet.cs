using UnityEngine;
using PhysicsComparisonLab.Battle;

namespace PhysicsComparisonLab.Presentation
{
    /// <summary>
    /// 提供PNGのフレームと時間を保存します。
    /// [KO] 제공 PNG의 프레임과 시간을 저장합니다.
    /// [EN] Stores frames and durations from the supplied PNG sheets.
    /// </summary>
    public sealed class UnitAnimationSet : ScriptableObject
    {
        [SerializeField] private Sprite[] _idle;
        [SerializeField] private Sprite[] _walk;
        [SerializeField] private Sprite[] _attack;
        [SerializeField] private Sprite[] _death;
        [SerializeField] private float[] _deathDurations = { 0.1f, 0.1f, 0.1f, 0.6f };
        [SerializeField] private float _frameDuration = 0.1f;

        /// <summary>
        /// 描画時間だけでフレームを選び、戦闘判定は変更しません。
        /// [KO] 표시 시간으로만 프레임을 선택하며 전투 판정을 바꾸지 않습니다.
        /// [EN] Selects a visual frame by time without changing combat.
        /// </summary>
        public Sprite Frame(UnitState.Action action, float elapsed)
        {
            if (action == UnitState.Action.Death)
            {
                for (var i = 0; i < _death.Length; i++)
                {
                    if (elapsed < _deathDurations[i])
                        return _death[i];

                    elapsed -= _deathDurations[i];
                }

                return _death[_death.Length - 1];
            }

            var frames =
                action == UnitState.Action.Attack ? _attack :
                action == UnitState.Action.Walk ? _walk :
                _idle;
            var index = Mathf.FloorToInt((elapsed + 0.00001f) / _frameDuration);

            return frames[action == UnitState.Action.Attack ? Mathf.Min(index, frames.Length - 1) : index % frames.Length];
        }
    }
}
