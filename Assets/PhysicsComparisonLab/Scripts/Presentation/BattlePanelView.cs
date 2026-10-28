using System;
using System.Globalization;
using PhysicsComparisonLab.Battle;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PhysicsComparisonLab.Presentation
{
    /// <summary>
    /// 準備入力と操作イベントを共有し、自分のlistenerだけ解除します。
    /// [KO] 준비 입력과 조작 이벤트를 공유하며 자신의 listener만 해제합니다.
    /// [EN] Shares preparation inputs and controls and removes only its own listeners.
    /// </summary>
    public sealed class BattlePanelView : MonoBehaviour
    {
        [SerializeField] private InputField _soldiers;
        [SerializeField] private InputField _orcs;
        [SerializeField] private InputField _seed;
        [SerializeField] private Button _backend;
        [SerializeField] private Button _start;
        [SerializeField] private Button _reset;
        [SerializeField] private Button _pause;
        [SerializeField] private Button _step;
        [SerializeField] private Button _speed;
        [SerializeField] private Button[] _presets;
        [SerializeField] private Text _status;
        [SerializeField] private Text _description;
        [SerializeField] private Text _validation;
        [SerializeField] private Text _rule;

        // 再有効化でlistenerが重複しないよう、登録したdelegateを保存する。
        private UnityAction[] _presetActions;

        // 入力画面は操作を通知し、実際の戦闘状態はEntryPointが変更する。
        public event Action StartRequested;
        public event Action ResetRequested;
        public event Action BackendRequested;
        public event Action PauseRequested;
        public event Action StepRequested;
        public event Action SpeedRequested;
        public event Action InputChanged;

        public string Soldiers
            => _soldiers.text;

        public string Orcs
            => _orcs.text;

        public string Seed
            => _seed.text;

        /// <summary>
        /// 操作と入力確定を通知し、解除用に加算ボタンのlistenerを保持します。
        /// [KO] 조작과 입력 확정을 알리고 증가 버튼의 listener를 해제용으로 보관합니다.
        /// [EN] Connects control and input-confirmation events and retains increment listeners for removal.
        /// </summary>
        private void OnEnable()
        {
            _start.onClick.AddListener(HandleStart);
            _reset.onClick.AddListener(HandleReset);
            _backend.onClick.AddListener(HandleBackend);
            _pause.onClick.AddListener(HandlePause);
            _step.onClick.AddListener(HandleStep);
            _speed.onClick.AddListener(HandleSpeed);
            _soldiers.onEndEdit.AddListener(HandleInput);
            _orcs.onEndEdit.AddListener(HandleInput);
            _seed.onEndEdit.AddListener(HandleInput);
            _rule.text = $"One hit · Simultaneous hits · Max {BattleSettings.MaximumUnitsPerTeam:N0} / side\nConfirm counts with Enter · Auto-fit camera";
            var increments = new[] { 10, 50, 100 };
            _presetActions = new UnityAction[_presets.Length];

            for (var i = 0; i < increments.Length; i++)
            {
                var increment = increments[i];
                _presetActions[i] = () => IncrementCounts(increment);
                _presets[i].onClick.AddListener(_presetActions[i]);
            }

            _presetActions[3] = () => SetCounts(BattleSettings.MaximumUnitsPerTeam, BattleSettings.MaximumUnitsPerTeam);
            _presets[3].onClick.AddListener(_presetActions[3]);
        }

        /// <summary>
        /// この画面が登録したlistenerだけを解除します。
        /// [KO] 이 화면이 등록한 listener만 해제합니다.
        /// [EN] Removes only listeners registered by this view.
        /// </summary>
        private void OnDisable()
        {
            _start.onClick.RemoveListener(HandleStart);
            _reset.onClick.RemoveListener(HandleReset);
            _backend.onClick.RemoveListener(HandleBackend);
            _pause.onClick.RemoveListener(HandlePause);
            _step.onClick.RemoveListener(HandleStep);
            _speed.onClick.RemoveListener(HandleSpeed);
            _soldiers.onEndEdit.RemoveListener(HandleInput);
            _orcs.onEndEdit.RemoveListener(HandleInput);
            _seed.onEndEdit.RemoveListener(HandleInput);

            if (_presetActions != null)
            {
                for (var i = 0; i < _presets.Length; i++)
                    _presets[i].onClick.RemoveListener(_presetActions[i]);
            }
        }

        /// <summary>
        /// 両入力を更新して変更通知を一度だけ送ります。
        /// [KO] 두 입력을 갱신하고 변경 알림을 한 번만 보냅니다.
        /// [EN] Updates both count inputs and raises one change notification.
        /// </summary>
        public void SetCounts(int soldiers, int orcs)
        {
            _soldiers.SetTextWithoutNotify(soldiers.ToString());
            _orcs.SetTextWithoutNotify(orcs.ToString());
            InputChanged?.Invoke();
        }

        /// <summary>
        /// 両軍の現在数に加算し、共通上限で止めます。
        /// [KO] 양 진영의 현재 인원에 더하고 공통 상한에서 멈춥니다.
        /// [EN] Adds to each army's current count and stops at the shared limit.
        /// </summary>
        private void IncrementCounts(int amount)
        {
            var maximum = BattleSettings.MaximumUnitsPerTeam;

            if (!int.TryParse(Soldiers, NumberStyles.None, CultureInfo.InvariantCulture, out var soldiers)
                || !int.TryParse(Orcs, NumberStyles.None, CultureInfo.InvariantCulture, out var orcs)
                || soldiers < 1
                || soldiers > maximum
                || orcs < 1
                || orcs > maximum)
            {
                return;
            }

            SetCounts(soldiers + Math.Min(amount, maximum - soldiers), orcs + Math.Min(amount, maximum - orcs));
        }

        /// <summary>
        /// 実行状態に合わせて操作可否、ボタン名、結果と入力エラーを更新します。
        /// [KO] 실행 상태에 맞춰 조작 가능 여부, 버튼명, 결과와 입력 오류를 갱신합니다.
        /// [EN] Updates control availability, button labels, results, and input errors for the current session state.
        /// </summary>
        public void Display(
            bool ready,
            bool valid,
            bool paused,
            bool finished,
            bool busy,
            bool core,
            int speed,
            string status,
            string error,
            bool canCancelBuild = false)
        {
            _soldiers.interactable = _orcs.interactable = _seed.interactable = ready && !busy;
            _backend.interactable = ready && !busy;
            _start.interactable = ready && valid && !busy;
            _reset.interactable = !busy || canCancelBuild;
            _reset.GetComponentInChildren<Text>().text = canCancelBuild ? "Cancel setup" : "Reset";
            _pause.interactable = !ready && !finished && !busy;
            _step.interactable = paused && !finished && !busy;
            _speed.interactable = !busy;

            foreach (var preset in _presets)
                preset.interactable = ready && !busy;

            // 操作可否の反映後、現在のbackendと実行状態をラベルへ表示する。
            _backend.GetComponentInChildren<Text>().text = core ? "PhysicsCore2D ▾" : "Physics2D ▾";
            _pause.GetComponentInChildren<Text>().text = paused ? "Resume" : "Pause";
            _speed.GetComponentInChildren<Text>().text = speed + "×";
            _status.text = status;
            _validation.text = error;
            _description.text = core
                ? "Core · PhysicsWorld / PhysicsBody / Shape · Both armies"
                : "Legacy · Rigidbody2D / CircleCollider2D / Physics2D · Both armies";
        }

        private void HandleStart()
            => StartRequested?.Invoke();

        private void HandleReset()
            => ResetRequested?.Invoke();

        private void HandleBackend()
            => BackendRequested?.Invoke();

        private void HandlePause()
            => PauseRequested?.Invoke();

        private void HandleStep()
            => StepRequested?.Invoke();

        private void HandleSpeed()
            => SpeedRequested?.Invoke();

        private void HandleInput(string value)
            => InputChanged?.Invoke();
    }
}
