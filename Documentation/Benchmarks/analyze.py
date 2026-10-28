"""Validate the exported samples and reproduce the CSV tables and figures.

Run from any directory with Python 3 and matplotlib:
    python Documentation/Benchmarks/analyze.py
No Unity or project assets are modified.
"""
import csv
import json
import math
import statistics
import sys
from collections import defaultdict
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


ROOT = Path(__file__).resolve().parent
DATA = ROOT / "2026-10-28"
IMAGES = ROOT.parent / "Images"
rows = list(csv.DictReader((DATA / "runs.csv").open(encoding="utf-8-sig")))
assert json.loads((DATA / "completed.json").read_text())["runs"] == len(rows) == 160
keys = ["experiment", "presentation", "count_per_team", "backend"]
metrics = [key for key in rows[0] if key.endswith("_ms")]
groups = defaultdict(list)
sample_count = 0

for row in rows:
    prefix = "_".join(row[key] for key in ["experiment", "presentation", "count_per_team", "repeat", "backend"])
    samples = list(csv.DictReader((DATA / f"{prefix}.csv").open(encoding="utf-8")))
    assert len(samples) == int(row["samples"])
    steps = [int(sample["step"]) for sample in samples]
    assert steps == list(range(int(row["first_step"]), int(row["last_step"]) + 1))
    if row["experiment"] == "FixedLoad":
        assert steps == list(range(121, 721))
        assert int(row["final_alive"]) == int(row["count_per_team"]) * 2
    else:
        assert row["result"] != "None" and steps[0] == 1 and steps[-1] <= 1800
    for metric, raw in [("native", "native_ms"), ("total", "total_ms")]:
        values = sorted(float(sample[raw]) for sample in samples)
        assert all(math.isfinite(value) and value >= 0 for value in values)
        for percentile in [50, 95]:
            actual = values[math.ceil(len(values) * percentile / 100) - 1]
            assert abs(actual - float(row[f"{metric}_p{percentile}_ms"])) <= 0.0000011
    sample_count += len(samples)
    groups[tuple(row[key] for key in keys)].append(row)

aggregates = []
for key, runs in groups.items():
    assert len(runs) == 5 and sorted(int(run["repeat"]) for run in runs) == [1, 2, 3, 4, 5]
    result = dict(zip(keys, key))
    for metric in metrics:
        values = [float(run[metric]) for run in runs]
        result[metric] = statistics.median(values)
        if metric in ["native_p50_ms", "total_p50_ms"]:
            result[metric.replace("_ms", "_min_ms")] = min(values)
            result[metric.replace("_ms", "_max_ms")] = max(values)
    for metric in ["last_step", "final_alive", "hits"]:
        result[metric + "_range"] = f'{min(int(run[metric]) for run in runs)}–{max(int(run[metric]) for run in runs)}'
    aggregates.append(result)

with (DATA / "aggregate.csv").open("w", newline="", encoding="utf-8") as stream:
    writer = csv.DictWriter(stream, fieldnames=list(aggregates[0]))
    writer.writeheader()
    writer.writerows(aggregates)

def get(experiment, presentation, size, backend):
    return next(row for row in aggregates if tuple(row[key] for key in keys) == (experiment, presentation, str(size), backend))


plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 11,
                     "axes.spines.top": False, "axes.spines.right": False})
colors = {"Legacy": "#d27b31", "Core": "#087f8c"}
labels = {"Legacy": "Physics2D", "Core": "PhysicsCore2D"}
sizes = [25, 100, 250, 1500, 2500]
fig, axes = plt.subplots(1, 2, figsize=(13.5, 5.6), layout="constrained")
for axis, metric, title in zip(axes, ["native_p50_ms", "total_p50_ms"],
                               ["Native simulation call", "Battle.Step (without mesh updates)"]):
    for backend in colors:
        values = [get("FixedLoad", "NoView", size, backend)[metric] for size in sizes]
        lows = [get("FixedLoad", "NoView", size, backend)[metric.replace("_ms", "_min_ms")] for size in sizes]
        highs = [get("FixedLoad", "NoView", size, backend)[metric.replace("_ms", "_max_ms")] for size in sizes]
        axis.errorbar(range(5), values, yerr=[[v-lo for v, lo in zip(values, lows)], [hi-v for v, hi in zip(values, highs)]],
                      marker="o", linewidth=2, capsize=4, color=colors[backend], label=labels[backend])
        for x, value in enumerate(values):
            axis.annotate(f"{value:.3f}", (x, value), xytext=(0, 10 if backend == "Legacy" else -18),
                          textcoords="offset points", ha="center", fontsize=9, color=colors[backend])
    axis.set_xticks(range(5), [f"{size:,} + {size:,}" for size in sizes])
    axis.set_yscale("log")
    axis.set_ylim(0.02, 30)
    axis.set_ylabel("Milliseconds per step (log scale; lower is better)")
    axis.set_xlabel("Units: Soldier + Orc")
    axis.set_title(title, fontweight="bold")
    axis.grid(axis="y", which="major", alpha=.2)
    axis.legend(loc="upper left")
fig.suptitle("Fixed-load CPU diagnostic in Unity Editor", fontsize=17, fontweight="bold")
fig.supxlabel("600 samples after 120 warmup steps; median of 5 run medians; whiskers = min/max run median\n"
              "Unpaced fixed dt = 1/60 s. This is not a frame-time or Player benchmark.\n"
              "Source: Physics Comparison Lab | hakrimryu | 2026-10-28", fontsize=10)
fig.savefig(IMAGES / "editor-cpu-comparison.png", dpi=160, facecolor="white", metadata={"Creator": "hakrimryu"})
fig.savefig(IMAGES / "editor-cpu-comparison.svg", facecolor="white", metadata={"Creator": "hakrimryu"})
plt.close(fig)

fig, axis = plt.subplots(figsize=(10.8, 5.4), layout="constrained")
segments = [("native_ms", "Native", "#087f8c"), ("query_ms", "Queries", "#e9a23b"),
            ("logic_ms", "Common logic", "#7465a7"), ("extract_ms", "Readback", "#569d54"),
            ("view_ms", "Mesh refresh", "#cf6675")]
bar_labels, means = [], []
for size in [250, 2500]:
    for backend in ["Legacy", "Core"]:
        selected = [row for row in rows if row["experiment"] == "FixedLoad" and row["presentation"] == "Mesh"
                    and row["count_per_team"] == str(size) and row["backend"] == backend]
        per_run = []
        for row in selected:
            prefix = "_".join(row[key] for key in ["experiment", "presentation", "count_per_team", "repeat", "backend"])
            samples = list(csv.DictReader((DATA / f"{prefix}.csv").open()))
            per_run.append({metric: statistics.mean(float(sample[metric]) for sample in samples) for metric, _, _ in segments})
        means.append({metric: statistics.mean(run[metric] for run in per_run) for metric, _, _ in segments})
        bar_labels.append(f"{size:,} + {size:,}\n{labels[backend]}")
left = [0.0] * 4
for metric, label, color in segments:
    values = [row[metric] for row in means]
    axis.barh(range(4), values, left=left, color=color, label=label, height=.58)
    left = [a+b for a, b in zip(left, values)]
for index, value in enumerate(left):
    axis.text(value + .15, index, f"{value:.2f} ms", va="center", fontsize=11)
axis.set_yticks(range(4), bar_labels)
axis.invert_yaxis()
axis.set_xlim(0, max(left) * 1.2)
axis.set_xlabel("Mean milliseconds per step (5 runs, 600 samples each)")
axis.set_title("Where the CPU time goes — fixed load with mesh updates", fontweight="bold", pad=16)
axis.legend(ncols=5, loc="lower center", bbox_to_anchor=(.5, -0.32), frameon=False)
axis.grid(axis="x", alpha=.15)
fig.supxlabel("Measured stage means are added here; GPU rendering, UI and Editor frame overhead are excluded.\n"
              "Source: Physics Comparison Lab | hakrimryu | 2026-10-28", fontsize=10)
fig.savefig(IMAGES / "editor-stage-costs.png", dpi=160, facecolor="white", metadata={"Creator": "hakrimryu"})
fig.savefig(IMAGES / "editor-stage-costs.svg", facecolor="white", metadata={"Creator": "hakrimryu"})
plt.close(fig)

tables = [
    "# Editor 측정 통계표\n",
    "같은 조건으로 5회씩 실행한 결과를 모았습니다. 인원은 진영별 수이며, 생존 수는 전투가 끝났을 때 두 진영에 남은 유닛의 합계입니다. 시간 단위는 한 스텝당 밀리초(ms)로 작을수록 좋습니다.\n",
    "p50은 중앙값, p95는 표본을 빠른 순서로 정렬했을 때 95% 지점의 값입니다. 각 실행에서 통계를 먼저 구한 뒤, 그 값 5개의 중앙값을 아래 표에 적었습니다. ‘평균’ 열도 실행별 평균 5개의 중앙값이며, 종료 스텝과 생존 수의 범위는 5회 실행의 최솟값–최댓값입니다.\n",
    "물리 계산, 공격 조회, 공통 로직, 위치 읽기, mesh 갱신을 구분해 기록했습니다. 서로 다른 통계를 더해 전체 시간을 계산할 수는 없으며, mesh 갱신에는 GPU 렌더링 시간이 포함되지 않습니다. 자세한 조건은 [측정 방법](../README.md)을 참고하세요.\n",
]
experiment_titles = {"FixedLoad": "고정 부하", "Battle": "일반 전투"}
presentation_titles = {"NoView": "표시 갱신 제외", "Mesh": "mesh 갱신 포함"}
for experiment in ["FixedLoad", "Battle"]:
    for presentation in ["NoView", "Mesh"]:
        tables += [f"## {experiment_titles[experiment]} · {presentation_titles[presentation]} ({experiment} / {presentation})\n",
                   "| 진영별 수 | 방식 | 물리 p50 | 물리 p95 | 공격 조회 평균 | 공통 로직 평균 | 위치 읽기 평균 | mesh 갱신 평균 | 전체 p50 | 전체 p95 | 종료 스텝 범위 | 생존 수 범위 |",
                   "|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|"]
        for size in sizes if experiment == "FixedLoad" else sizes[:3]:
            for backend in ["Legacy", "Core"]:
                row = get(experiment, presentation, size, backend)
                fields = [f"{row[metric]:.4f}" for metric in metrics if metric != "creation_ms"]
                tables.append(f"| {size:,} | {labels[backend]} | " + " | ".join(fields)
                              + f" | {row['last_step_range']} | {row['final_alive_range']} |")
        tables.append("")
(DATA / "tables.md").write_text("\n".join(tables), encoding="utf-8")
verification = {
    "runs": len(rows), "samples": sample_count, "groups": len(groups), "validated": True,
    "checks": ["five repeats per group", "continuous sample steps", "fixed-load population",
               "native and total nearest-rank p50/p95 recalculated"],
    "analysisPython": sys.version.split()[0], "matplotlib": matplotlib.__version__,
}
(DATA / "analysis-verification.json").write_text(json.dumps(verification, indent=2), encoding="utf-8")
print(json.dumps(verification, indent=2))
for size in sizes:
    legacy, core = [get("FixedLoad", "NoView", size, backend) for backend in ["Legacy", "Core"]]
    print(size, "native", legacy["native_p50_ms"], core["native_p50_ms"], "total", legacy["total_p50_ms"], core["total_p50_ms"],
          "ratio", legacy["native_p50_ms"] / core["native_p50_ms"])
