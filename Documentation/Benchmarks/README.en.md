[English](README.en.md) · [日本語](README.ja.md) · [한국어](README.md)

# Editor CPU comparison

On 2026-10-28, I compared the processing times of the two physics engines in the Unity Editor. This document explains the measurement conditions and calculations behind the tables and graphs in the [project README](../../README.md).

## What was compared

As a battle progresses, units die and the workload falls. Because death timing differs between engines, the speed comparison uses fixed-load experiments that disable deaths and keep the population constant. Regular battles, with deaths and outcomes enabled, were measured separately to capture behavior during combat.

| Experiment | Units per side | Visual updates | Repeats | Runs |
|---|---|---|---|---:|
| Regular battle (`Battle`) | 25 / 100 / 250 | None / mesh updates included | 5 per condition × two engines | 60 |
| Fixed load (`FixedLoad`) | 25 / 100 / 250 / 1,500 / 2,500 | None / mesh updates included | 5 per condition × two engines | 100 |
| Total | | | | **160** |

Regular battles were recorded from the first step until an outcome or step 1,800. Fixed-load runs disabled only deaths and outcome handling, while movement, enemy searches, contacts, and attack-range queries continued. After 120 warmup steps, 600 samples were collected from steps 121–720.

`NoView` still runs the battle logic; it does not isolate physics computation alone. `Mesh` additionally calls the shared `BattleWorldView.Refresh` every step to update unit positions and animations. This measures the time to update mesh data, excluding GPU rendering time.

## Keeping conditions consistent

Both engines received the same `BattleInput` for each combination. The seed was 12345, with a fixed simulation interval of 1/60 s. Execution alternated between Legacy→Core and Core→Legacy, although 5 repeats meant the two orders were not used equally often. Population sizes were tested in ascending order rather than randomized.

Before measurement, each engine ran a 25 vs. 25 battle for 120 steps to warm up JIT compilation and mesh updates. These warmup battles and 8 pilot runs were excluded from the results. Measurement processed up to 8 steps per rendered frame without waiting for real time, so it should be distinguished from the game's 1×/2× speeds and real-time frame performance.

The normal entry component was disabled to prevent the regular battle from running alongside the experiment. UI and automatic camera tracking were also disabled. The scene background remained, but the number of units visible on screen was not controlled as a comparison condition. Sample arrays were allocated beforehand, with string processing and file writes outside the timed sections. This did not eliminate allocations within the battle code or engines.

After each run, visual and physics objects were released, followed by a wait of two frames. Checks then confirmed that no Rigidbody2D objects remained, only the default Core world remained, and the previous physics simulation mode had been restored. Body and shape counts at the start and end of each run were also checked against the number of surviving units plus the 4 boundaries.

Analysis, tests, and graph generation on the same PC took place after measurement. Progress files were read occasionally during the experiment, however, and this was not a dedicated environment with all other OS activity controlled.

## Environment and physics settings

The actual environment is recorded in [environment.txt](2026-10-28/environment.txt).

| Item | Setting |
|---|---|
| Unity | 6000.5.8f1, Built-in, Mono Editor |
| OS and CPU | Windows 11, Ryzen 7 7800X3D, 16 logical processors |
| Memory and GPU | 64,681 MB RAM, RTX 5070 Ti |
| Display | Direct3D12, 1920×1080 |
| Runtime options | VSync 0, frame limit -1, Profiler recording off |

Each run's `*.config.txt` records its physics settings. Core used Script mode, substeps 4, workers 16, and allowed sleeping. Contact settings were `contactFrequency=30`, `contactDamping=10`, and `contactSpeed=3`, with circular-shape density set to `1/(πr²)`. The automatic CCD threshold of 0.5 was recorded as a description of the implementation's setting.

The legacy implementation also used Script mode, with velocity iterations 8, position iterations 3, Discrete collision detection, StartAwake, and mass 1. Solver accuracy was not matched between the engines, and worker counts and substeps were not varied. The measured difference therefore cannot be attributed separately to factors such as multithreading or data layout.

## Reading the measurements

`Stopwatch` measured elapsed time from function call to return. Physics simulation, attack queries, shared logic, position readback, and visual updates were recorded separately. The CSV columns cover the following work:

| CSV column | Timed work |
|---|---|
| `native_ms` | The engine's synchronous `Simulate` call |
| `query_ms` | Attack-range queries and candidate validation, including creation and release of Core's result arrays |
| `logic_ms` | Shared logic such as AI, target selection, velocity application, and death handling |
| `extract_ms` | Reading physics-object positions into the shared unit array |
| `view_ms` | Updating position and animation data in the shared mesh; 0 in `NoView` |
| `total_ms` | `Battle.Step` plus optional mesh updates, including some instrumentation overhead |
| `creation_ms` | Creating the physics implementation and `BattleSimulation`, excluding mesh creation and ground updates |

These values are not the sum of CPU time across worker threads. Whole-frame time, GC allocations, GPU, and memory usage were not collected. A `view_ms` of 0 in `NoView` means that visual updates were not called; it does not mean there were no allocations. CSV values are stored to 6 decimal places in milliseconds, which does not imply equivalent measurement accuracy.

## How the tables and graphs were aggregated

Each run's p50 and p95 use the nearest-rank method: sort the samples and select index `ceil(n×p)-1`. The [aggregate CSV](2026-10-28/aggregate.csv) and [full statistics](2026-10-28/tables.en.md) report the median of the 5 per-run statistics. The “mean” columns likewise show the median of the 5 individual run means.

The first graph shows the median of the 5 run medians. Its vertical axis is logarithmic, and error bars mark the minimum and maximum of those 5 values. They do not show confidence intervals or the range of all samples.

The stacked stage-cost graph uses a different aggregation: it takes the arithmetic mean of each stage's per-run means across the 5 repeats, then stacks the stages. It does not add medians, so its values can differ from the statistics tables. Work such as enemy searches concentrated in certain steps can raise means and p95 above the median, but no function-level investigation of slow-step causes was performed.

## Code and result files

[EditorComparison.cs](EditorComparison.cs) contains the measurement code. It was kept outside `Assets` and compiled and executed in memory through Unity CLI, without adding temporary measurement features to saved scenes or game code. [analyze.py](analyze.py) reads the CSVs, verifies statistics, and generates tables and graphs. Both graphs credit **hakrimryu**.

The analysis checked 32 combinations repeated 5 times: 160 runs and 90,579 samples in total. It verified consecutive sample steps and stable populations under fixed load, then recalculated physics and total-processing p50 and p95. The [analysis verification record](2026-10-28/analysis-verification.json) contains these results and the analysis tool versions.

The [completion record](2026-10-28/completed.json) records resource cleanup and setting restoration after each run. The [source hashes at measurement time](2026-10-28/source-sha256.json) contain SHA256 hashes of the code, scene, packages, settings, and measurement tools.

## Interpreting the results

These results show reduced physics processing time for large battles in this Editor scene. At 5,000 units, Core's total p95 including mesh updates was still about 21.30 ms, so they do not establish that the whole game runs at a stable 60 FPS. Standalone Player performance, speedups on other CPUs, and memory savings were not measured.

Repeatable battle outcomes are a separate question from speed. Core produced the same outcomes across repeats in this session, but that does not guarantee cross-platform determinism. Some regular battles on the legacy system varied in their final step and survivor count. Fixed-load runs also did not enforce identical contact distributions between engines.
