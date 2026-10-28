using System;
using System.Collections;
using UnityEngine;
using PhysicsComparisonLab.Battle;
using PhysicsComparisonLab.Core;
using PhysicsComparisonLab.Legacy;
using PhysicsComparisonLab.Simulation;

namespace PhysicsComparisonLab.Presentation
{
    /// <summary>
    /// シーンの参照、固定clock、表示とbackendの寿命を所有します。
    /// [KO] 씬 참조, 고정 clock, 표시와 backend 수명을 소유합니다.
    /// [EN] Owns scene references, the fixed clock, presentation and backend lifetime.
    /// </summary>
    public sealed class BattleComparisonEntryPoint : MonoBehaviour
    {
        [SerializeField] private BattlePanelView _panel;
        [SerializeField] private BattlefieldView _field;
        [SerializeField] private UnitAnimationSet _soldierAnimation;
        [SerializeField] private UnitAnimationSet _orcAnimation;
        private BattleWorldView _view;
        private BattleInput _input;

        // 実時間の未消費分、結果後の死亡表示、UI更新間隔を別々に保持する。
        private float _accumulator;
        private float _finishingSteps;
        private float _uiTimer;
        private bool _busy;
        private bool _ready = true;
        private string _error = "";

        // 生成完了まではここがbackendを所有し、完了後にBattleへ渡す。
        private IPhysicsSimulation _buildingPhysics;
        private bool _cancelBuild;
        private string _buildStatus = "";

        public BattleSimulation Battle { get; private set; }
        public bool IsCore { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsReady
            => _ready;

        public bool IsBusy
            => _busy;

        public bool IsFinished
            => Battle != null && Battle.Result != BattleSimulation.Outcome.None && _finishingSteps >= 54;

        public int Speed { get; private set; } = 1;
        public int ViewCount
            => _view?.ViewCount ?? 0;

        public UnitAnimationSet SoldierAnimation
            => _soldierAnimation;

        public UnitAnimationSet OrcAnimation
            => _orcAnimation;

        /// <summary>
        /// 操作イベントを接続して準備を開始し、指定時だけ検証を起動します。
        /// [KO] 조작 이벤트를 연결하고 준비를 시작하며 지정한 경우에만 검증을 실행합니다.
        /// [EN] Connects controls and starts preparation, launching validation only when requested by arguments.
        /// </summary>
        private void Start()
        {
            _panel.StartRequested += StartBattle;
            _panel.ResetRequested += ResetBattle;
            _panel.BackendRequested += ToggleBackend;
            _panel.PauseRequested += TogglePause;
            _panel.StepRequested += SingleStep;
            _panel.SpeedRequested += ToggleSpeed;
            _panel.InputChanged += HandleInputChanged;
            StartCoroutine(Rebuild());

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--validate") >= 0)
                gameObject.AddComponent<PlayerValidation>();

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--validate-large") >= 0)
                gameObject.AddComponent<LargeBattleValidation>();
        }

        /// <summary>
        /// 実時間を固定stepへ変換し、状態表示は毎秒四回に制限します。
        /// [KO] 실제 시간을 고정 step으로 변환하고 상태 표시는 초당 네 번으로 제한합니다.
        /// [EN] Converts elapsed time to fixed steps and limits status updates to four per second.
        /// </summary>
        private void Update()
        {
            if (!_busy && !_ready && !IsPaused && !IsFinished && Battle != null)
            {
                _accumulator += Time.unscaledDeltaTime * Speed;
                var count = 0;

                while (_accumulator >= BattleSettings.StepSeconds && count++ < 8 && !IsFinished)
                {
                    Advance();
                    _accumulator -= BattleSettings.StepSeconds;
                }
            }

            _uiTimer += Time.unscaledDeltaTime;

            if (_uiTimer >= 0.25f)
            {
                _uiTimer = 0;
                RefreshPanel();
            }
        }

        /// <summary>
        /// 準備済み入力を固定して戦闘clockを開始します。
        /// [KO] 준비된 입력을 고정하고 전투 clock을 시작합니다.
        /// [EN] Starts the battle clock with the prepared immutable input.
        /// </summary>
        public void StartBattle()
        {
            if (_busy || !_ready || Battle == null || !TrySettings(out _))
                return;

            _ready = false;
            IsPaused = false;
            _accumulator = 0;
            RefreshPanel();
        }

        /// <summary>
        /// 戦闘と死亡表示のclockを一緒に停止・再開します。
        /// [KO] 전투와 사망 표시 clock을 함께 정지하거나 재개합니다.
        /// [EN] Pauses or resumes both combat and death presentation clocks.
        /// </summary>
        public void TogglePause()
        {
            if (_busy || _ready || IsFinished)
                return;

            IsPaused = !IsPaused;
            RefreshPanel();
        }

        /// <summary>
        /// 停止中だけ固定時間を一回進めます。
        /// [KO] 정지 중에만 고정 시간을 한 번 진행합니다.
        /// [EN] Advances one fixed step only while paused.
        /// </summary>
        public void SingleStep()
        {
            if (!_busy && IsPaused && !_ready && !IsFinished)
                Advance();

            RefreshPanel();
        }

        /// <summary>
        /// 物理dtを変えず、消費する実時間の倍率を切り替えます。
        /// [KO] 물리 dt를 유지하며 실제 시간 소비 배율을 전환합니다.
        /// [EN] Switches elapsed-time consumption without changing physics dt.
        /// </summary>
        public void ToggleSpeed()
        {
            Speed = Speed == 1 ? 2 : 1;
            RefreshPanel();
        }

        /// <summary>
        /// 現在の資源を解放して同じ入力から準備し直します。
        /// [KO] 현재 자원을 해제하고 같은 입력으로 다시 준비합니다.
        /// [EN] Releases the current session and prepares the same input again.
        /// </summary>
        public void ResetBattle()
        {
            if (_busy && _buildingPhysics != null)
                _cancelBuild = true;
            else if (!_busy)
                StartCoroutine(Rebuild());
        }

        /// <summary>
        /// 準備中だけ全戦場の物理実装を交換します。
        /// [KO] 준비 중에만 전장 전체의 물리 구현을 전환합니다.
        /// [EN] Replaces the physics backend for the whole battlefield while ready.
        /// </summary>
        public void ToggleBackend()
        {
            if (_busy || !_ready)
                return;

            IsCore = !IsCore;
            StartCoroutine(Rebuild());
        }

        /// <summary>
        /// 準備中にbackendと人数を変更し、入力通知から再生成します。
        /// [KO] 준비 중 backend와 인원을 변경하고 입력 알림을 통해 다시 생성합니다.
        /// [EN] Changes backend and counts while ready, rebuilding through the input notification.
        /// </summary>
        public void Configure(int soldiers, int orcs, bool core)
        {
            if (_busy || !_ready)
                throw new InvalidOperationException("Session must be ready before configuration.");

            IsCore = core;
            _panel.SetCounts(soldiers, orcs);
        }

        /// <summary>
        /// 準備中の入力変更から配置を再生成します。
        /// [KO] 준비 중 입력 변경에 맞춰 배치를 다시 생성합니다.
        /// [EN] Rebuilds the deployment after input changes while ready.
        /// </summary>
        private void HandleInputChanged()
        {
            if (_ready && !_busy)
                StartCoroutine(Rebuild());
        }

        /// <summary>
        /// 旧オブジェクトの遅延破棄を一フレーム待ってから次の実行を作ります。
        /// [KO] 이전 객체의 지연 파괴를 한 프레임 기다린 뒤 다음 실행을 만듭니다.
        /// [EN] Waits one frame for deferred destruction before creating the next run.
        /// </summary>
        private IEnumerator Rebuild()
        {
            // 前回の遅延破棄が終わってから、新しい物理オブジェクトを作る。
            _busy = true;
            _ready = true;
            _cancelBuild = false;
            IsPaused = false;
            _accumulator = 0;
            _finishingSteps = 0;
            Release();
            yield return null;

            if (TrySettings(out var settings))
            {
                try
                {
                    if (_input == null
                        || _input.Settings.Soldiers != settings.Soldiers
                        || _input.Settings.Orcs != settings.Orcs
                        || _input.Settings.Seed != settings.Seed)
                    {
                        _input = new BattleInput(settings);
                    }

                    _buildingPhysics = IsCore ? new CorePhysicsSimulation(_input, true) : new LegacyPhysicsSimulation(_input, true);
                }
                catch (Exception exception)
                {
                    Release();
                    _error = exception.Message;
                    Debug.LogException(exception);
                }

                var complete = false;

                while (_buildingPhysics != null && !complete && !_cancelBuild)
                {
                    try
                    {
                        complete = _buildingPhysics.CreateNextBatch(2048);
                        _buildStatus = $"Preparing {Mathf.Min(_buildingPhysics.BodyCount, _input.Count):N0} / {_input.Count:N0} units";
                    }
                    catch (Exception exception)
                    {
                        Release();
                        _error = exception.Message;
                        Debug.LogException(exception);
                    }

                    RefreshPanel();

                    if (!complete)
                        yield return null;
                }

                if (_cancelBuild)
                {
                    Release();
                    _error = "Setup cancelled. Change the counts or press Reset to prepare again.";
                    yield return null;
                }
                else if (_buildingPhysics != null)
                {
                    try
                    {
                        // 生成完了したbackendの所有権を戦闘へ移し、表示を接続する。
                        Battle = new BattleSimulation(_input, _buildingPhysics);
                        _buildingPhysics = null;
                        _view = new BattleWorldView(Battle.Units, _soldierAnimation, _orcAnimation);
                        _field.Configure(_input, Battle.Units);
                    }
                    catch (Exception exception)
                    {
                        Release();
                        _error = exception.Message;
                        Debug.LogException(exception);
                    }
                }
            }

            _busy = false;
            RefreshPanel();
        }

        /// <summary>
        /// 物理実装を一つ生成し、初期化失敗時にも解放します。
        /// [KO] 물리 구현 하나를 생성하고 초기화 실패 시에도 해제합니다.
        /// [EN] Creates one physics backend and releases it if battle initialization fails.
        /// </summary>
        public static BattleSimulation CreateBattle(BattleInput input, bool core, bool benchmark)
        {
            IPhysicsSimulation physics = core ? new CorePhysicsSimulation(input) : new LegacyPhysicsSimulation(input);

            try
            {
                return new BattleSimulation(input, physics, benchmark);
            }
            catch
            {
                physics.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 結果前は物理を進め、結果後は残りの死亡表示だけ進めます。
        /// [KO] 결과 전에는 물리를, 결과 후에는 남은 사망 표시만 진행합니다.
        /// [EN] Advances physics before the result and only death presentation afterward.
        /// </summary>
        private void Advance()
        {
            try
            {
                if (Battle.Result == BattleSimulation.Outcome.None)
                    Battle.Step();
                else
                    _finishingSteps++;

                _view.Refresh(Battle.Units, Battle.StepIndex + _finishingSteps);
            }
            catch (Exception exception)
            {
                IsPaused = true;
                _error = exception.Message;
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 入力を検証し、表示するエラーを更新します。
        /// [KO] 입력을 검사하고 표시할 오류를 갱신합니다.
        /// [EN] Validates inputs and updates the visible error.
        /// </summary>
        private bool TrySettings(out BattleSettings settings)
        {
            var valid = BattleSettings.TryParse(_panel.Soldiers, _panel.Orcs, _panel.Seed, out settings, out var error);

            if (!valid)
                _error = error;
            else
                _error = "";

            return valid;
        }

        /// <summary>
        /// 現在の状態と生存数を共通UIへ渡します。
        /// [KO] 현재 상태와 생존 수를 공통 UI에 전달합니다.
        /// [EN] Publishes the current state and survivor counts to the shared UI.
        /// </summary>
        private void RefreshPanel()
        {
            var valid = BattleSettings.TryParse(_panel.Soldiers, _panel.Orcs, _panel.Seed, out var displayedSettings, out var validation);
            var state =
                _busy ? _buildStatus :
                _ready ? "Ready · Set army sizes and press Start" :
                IsPaused ? "Pause" :
                "Battle in progress";

            if (Battle != null && Battle.Result != BattleSimulation.Outcome.None)
            {
                var result =
                    Battle.Result == BattleSimulation.Outcome.Draw ? "Draw · Mutual elimination" :
                    Battle.Result == BattleSimulation.Outcome.Soldier ? "Allies win" :
                    Battle.Result == BattleSimulation.Outcome.Orc ? "Enemies win" :
                    "Time limit · Unresolved";
                state = result + (IsFinished ? " · Finished" : " · Finishing death animations");
            }

            var detail = Battle == null
                ? state
                : $"{state}\nAllies {Battle.SoldierAlive:N0} alive / {_input.Settings.Soldiers - Battle.SoldierAlive:N0} lost    ·    Enemies {Battle.OrcAlive:N0} alive / {_input.Settings.Orcs - Battle.OrcAlive:N0} lost\nBattle {Battle.StepIndex / 60f:F2}s · {Battle.StepIndex:N0} step / 1,800";
            var matches = valid
                && _input != null
                && Battle != null
                && displayedSettings.Soldiers == _input.Settings.Soldiers
                && displayedSettings.Orcs == _input.Settings.Orcs
                && displayedSettings.Seed == _input.Settings.Seed;
            _panel.Display(
                _ready,
                matches,
                IsPaused,
                IsFinished,
                _busy,
                IsCore,
                Speed,
                detail,
                valid ? _error : validation,
                _buildingPhysics != null);
        }

        /// <summary>
        /// 表示と物理の所有参照を解放して再実行に備えます。
        /// [KO] 표시와 물리의 소유 참조를 해제해 재실행을 준비합니다.
        /// [EN] Releases owned presentation and physics references for another run.
        /// </summary>
        private void Release()
        {
            try
            {
                _view?.Dispose();
            }
            finally
            {
                _view = null;
                _buildingPhysics?.Dispose();
                _buildingPhysics = null;
                Battle?.Dispose();
                Battle = null;
            }
        }

        /// <summary>
        /// 自分の操作イベントを解除し、未完成の生成資源も解放します。
        /// [KO] 직접 연결한 조작 이벤트를 해제하고 생성 중인 자원도 정리합니다.
        /// [EN] Disconnects owned control handlers and releases resources, including unfinished construction.
        /// </summary>
        private void OnDestroy()
        {
            if (_panel != null)
            {
                _panel.StartRequested -= StartBattle;
                _panel.ResetRequested -= ResetBattle;
                _panel.BackendRequested -= ToggleBackend;
                _panel.PauseRequested -= TogglePause;
                _panel.StepRequested -= SingleStep;
                _panel.SpeedRequested -= ToggleSpeed;
                _panel.InputChanged -= HandleInputChanged;
            }

            Release();
        }
    }
}
