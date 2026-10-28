using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;

namespace PhysicsComparisonLab.Presentation
{
    /// <summary>
    /// 常時表示の右側領域で実行中のcounterを低頻度表示します。
    /// [KO] 항상 표시되는 오른쪽 영역에 실행 중 counter를 낮은 빈도로 표시합니다.
    /// [EN] Displays live counters at a bounded refresh rate in the permanent right sidebar.
    /// </summary>
    public sealed class BattlePerformancePanel : MonoBehaviour
    {
        [SerializeField] private Text _readout;
        [SerializeField] private Text _values;
        [SerializeField] private BattleComparisonEntryPoint _entry;
        private readonly string[] _names =
        {
            "CPU Main Thread Frame Time",
            "CPU Render Thread Frame Time",
            "GPU Frame Time",
            "Draw Calls Count",
            "SetPass Calls Count",
            "Triangles Count",
            "Vertices Count",
            "Total Used Memory",
            "GC Used Memory",
            "Gfx Used Memory",
            "GC Allocated In Frame"
        };
        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[11];
        private readonly StringBuilder _text = new StringBuilder(1024);
        private readonly StringBuilder _valueText = new StringBuilder(512);
        private float _elapsed;
        private int _frames;
        private float _frameSeconds;

        private void OnEnable()
        {
            StartCounters();
        }

        private void OnDisable()
        {
            StopCounters();
        }

        /// <summary>
        /// 編集済みレイアウトを変更せず、利用可能なcounterを開始します。
        /// [KO] 편집한 레이아웃을 변경하지 않고 사용 가능한 counter를 시작합니다.
        /// [EN] Starts available counters without changing the authored layout.
        /// </summary>
        private void StartCounters()
        {
            StopCounters();
            _elapsed = _frameSeconds = 0;
            _frames = 0;
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            for (var i = 0; i < _names.Length; i++)
            {
                foreach (var handle in handles)
                {
                    var description = ProfilerRecorderHandle.GetDescription(handle);

                    if (description.Name == _names[i])
                    {
                        _recorders[i] = ProfilerRecorder.StartNew(description.Category, description.Name, 1);
                        break;
                    }
                }
            }

            _readout.text = "Collecting live data…";
            _values.text = "";
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            _frameSeconds += Time.unscaledDeltaTime;
            _frames++;

            if (_elapsed < 0.25f)
                return;

            _text.Clear();
            _valueText.Clear();
            AppendLine("<color=#80D9D0><b>FRAME</b></color>");
            AppendLine("FPS", $"{_frames / _frameSeconds:F1}");
            AppendLine("Frame", $"{_frameSeconds * 1000 / _frames:F2} ms");
            AppendRow("CPU Main", 0, 1000000, " ms", true);
            AppendRow("CPU Render", 1, 1000000, " ms", true);
            AppendRow("GPU", 2, 1000000, " ms", true);
            AppendLine("");

            // 物理欄は最後に実行したstepの値を示し、フレーム平均と混同しない。
            AppendLine("<color=#80D9D0><b>PHYSICS</b></color>");
            var battle = _entry.Battle;
            AppendLine(_entry.IsCore ? "PhysicsCore2D" : "Physics2D");

            if (battle == null)
                AppendLine("Preparing armies");
            else
            {
                AppendLine("Allies (Soldier)", $"{battle.SoldierAlive:N0} / {battle.Input.Settings.Soldiers:N0}");
                AppendLine("Enemies (Orc)", $"{battle.OrcAlive:N0} / {battle.Input.Settings.Orcs:N0}");
                AppendLine("Bodies", $"{battle.Physics.BodyCount:N0}");
                AppendLine("Shapes", $"{battle.Physics.ShapeCount:N0}");
            }

            AppendLine(
                "Step",
                battle == null || battle.StepIndex == 0 ? "Ready" : $"{battle.NativeMilliseconds:F3} ms");
            AppendLine(
                "Query",
                battle == null || battle.StepIndex == 0 ? "Ready" : $"{battle.QueryMilliseconds:F3} ms");

            if (HasData(3) || HasData(4) || HasData(5) || HasData(6))
            {
                AppendLine("");
                AppendLine("<color=#80D9D0><b>RENDER</b></color>");
                AppendRow("Draw Calls", 3);
                AppendRow("SetPass", 4);
                AppendRow("Triangles", 5);
                AppendRow("Vertices", 6);
            }

            if (HasData(7, true) || HasData(8, true) || HasData(9, true) || HasData(10))
            {
                AppendLine("");
                AppendLine("<color=#80D9D0><b>MEMORY</b></color>");
                AppendRow("Total Used", 7, 1048576, " MB", true);
                AppendRow("Managed", 8, 1048576, " MB", true);
                AppendRow("Graphics", 9, 1048576, " MB", true);
                AppendRow("GC / frame", 10, 1024, " KB");
            }

            _text.AppendLine("\n<size=17><color=#A6B5B4>4 updates/sec · Last physics step\nMay include Editor / other windows\nLive diagnostics · Not a benchmark</color></size>");
            _readout.text = _text.ToString();
            _values.text = _valueText.ToString();
            _elapsed = _frameSeconds = 0;
            _frames = 0;
        }

        /// <summary>
        /// 読めないcounterや時間0を有効な測定値として表示しません。
        /// [KO] 읽을 수 없는 counter나 시간 0을 유효한 측정값으로 표시하지 않습니다.
        /// [EN] Does not present unavailable counters or zero timings as valid measurements.
        /// </summary>
        private void AppendRow(
            string label,
            int index,
            double divisor = 1,
            string suffix = "",
            bool requiresPositive = false)
        {
            if (!HasData(index, requiresPositive))
                return;

            var value = _recorders[index].LastValue / divisor;
            AppendLine(label, value.ToString(divisor == 1 ? "N0" : "F2") + suffix);
        }

        /// <summary>
        /// 同じ行に項目名と右揃えの値を追加します。
        /// [KO] 같은 행에 항목명과 오른쪽 정렬 값을 추가합니다.
        /// [EN] Adds a label and its right-aligned value on matching rows.
        /// </summary>
        private void AppendLine(string label, string value = "")
        {
            _text.AppendLine(label);
            _valueText.AppendLine(value);
        }

        /// <summary>
        /// 利用可能でsampleを持つcounterだけを表示対象にします。
        /// [KO] 사용 가능하고 sample이 있는 counter만 표시 대상으로 삼습니다.
        /// [EN] Accepts only available counters with samples for display.
        /// </summary>
        private bool HasData(int index, bool requiresPositive = false)
        {
            var recorder = _recorders[index];

            return recorder.Valid && recorder.Count > 0 && (!requiresPositive || recorder.LastValue > 0);
        }

        /// <summary>
        /// 開始済みrecorderをすべて解放し、再有効化に備えます。
        /// [KO] 시작한 recorder를 모두 해제하고 재활성화에 대비합니다.
        /// [EN] Disposes all started recorders and clears their slots for re-enabling.
        /// </summary>
        private void StopCounters()
        {
            for (var i = 0; i < _recorders.Length; i++)
            {
                _recorders[i].Dispose();
                _recorders[i] = default;
            }
        }
    }
}
