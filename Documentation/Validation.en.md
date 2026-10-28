[English](Validation.en.md) · [日本語](Validation.ja.md) · [한국어](Validation.md)

# Editor functional validation

Battle behavior was checked on 2026-10-28 using Unity 6000.5.8f1, Built-in, and the Windows Editor. The following records what was actually verified through automated tests and separate runs.

[Back to the project README](../README.md)

## Automated tests

EditMode tests checked the battle rules, while PlayMode tests checked combat and controls in the scene. Both suites passed with no failures or skipped tests.

| Suite | Result | Main coverage |
|---|---|---|
| EditMode `BattleContractTests` | **25/25 passed** | Input and placement, attack rules, simultaneous hits, enemy searches, animation data, and movement, queries, and cleanup through the actual physics APIs |
| PlayMode `BattleSessionTests` | **5/5 passed** | Battle completion, pause and single-step controls, engine switching and restart, buttons and UI, and cleanup after large battles |

Individual test names, results, and durations are in [test-results.json](test-results.json). The implementations are in the [Tests folder](../Assets/PhysicsComparisonLab/Tests/).

PlayMode ran 1:1, 1:2, 25:25, 100:100, and 250:250 battles on both engines, checking that each reached an outcome and finished its death animations. It also checked that positions and step counts remained unchanged while paused and that the single-step button advanced exactly once. After 10 engine switches and restarts, checks confirmed that bodies, worlds, visual objects, and button event handlers had not accumulated.

Input and layout were checked as well. The +10/+50/+100/Max buttons respected the 2,500-unit limit per side, and invalid input was rejected. The right panel did not cover the battlefield or input area, and its Soldier and Orc survivor counts matched the actual battle state.

The large-scale automated test ran **1,500 vs. 1,500 for 120 steps on each engine**. Although its name contains `Maximum`, it does not validate a complete battle at the current limit of 2,500 vs. 2,500.

## Captured runs

Five unedited 1920×1080 PNG screenshots were captured from the Editor Game View. Capture states and file hashes are recorded in [screenshots.json](screenshots.json).

| Screen | Recorded state |
|---|---|
| Setup | 25:25, step 0 |
| Legacy Physics2D | Started at 250:250; paused at step 120; 231/229 alive |
| PhysicsCore2D | Started at 250:250; paused at step 120; 229/228 alive |
| Large Core battle | Started at 2,500:2,500; paused at step 120; 2,437/2,435 alive; 4,876 bodies and 4,876 shapes |
| Core result | 25:25 at real-time 1× speed; Soldier victory at step 280; 4/0 alive |

The displayed count of 5,000 in the large battle includes units still playing death animations. Physics bodies include only living units and the 4 boundaries, so the counts differ. FPS on paused or finished screens does not represent performance during combat.

All 160 runs of the separate [Editor CPU measurements](Benchmarks/README.en.md) completed. The 5,000-unit experiments on both engines disabled deaths, ran 120 warmup steps, and then collected 600 steps per run. Stable populations and resource cleanup were verified. This does not validate battle completion with deaths enabled at 5,000 units.

## Final state and scope

At the end of validation, compilation and the Console both had 0 errors and 0 warnings. Play mode was stopped, the scene had no unsaved changes, and all physics objects except the default Core world were removed, along with temporary measurement objects. The legacy simulation mode was restored to `FixedUpdate`. Details are in the [final-state record](final-state.json).

The capture and measurement work did not change runtime C#, scenes, assets, or packages. Hashes of the existing 190 files in `Assets`, `Packages`, and `ProjectSettings` matched before and after the work. Temporary captured images were moved into the documentation folder and cleaned up from their temporary location.

No new Player build, standalone Player performance, other platforms, network determinism, differences between worker/substep settings, or memory, GC, and GPU behavior were validated. These results are limited to the Editor runs described above.
