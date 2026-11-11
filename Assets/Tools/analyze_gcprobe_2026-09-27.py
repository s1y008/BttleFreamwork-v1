# -*- coding: utf-8 -*-
"""按 [GCProbe] 窗口切分日志，关联命中次数，算出「每次命中分配多少字节」"""
import io
import json
import re
import sys

path = sys.argv[1] if len(sys.argv) > 1 else "_l.json"
d = json.load(io.open(path, encoding="utf-8"))
logs = d.get("result", {}).get("logs", [])
print("日志总条数:", len(logs))

msgs = []
for l in logs:
    if isinstance(l, dict):
        msgs.append((l.get("message", ""), l.get("type", "")))
    else:
        msgs.append((str(l), ""))

err = sum(1 for m, t in msgs if t == "Error" or t == "Exception")
print("Error 总数:", err)

# 关键标签计数
TAGS = ["[SegEffect]", "[HitEffectCalc]", "[HitEffect]", "[HitReact]", "[CastCtx]", "[AttackState]"]
total = {k: 0 for k in TAGS}
for m, t in msgs:
    for k in TAGS:
        if k in m:
            total[k] += 1
print("全量标签:", total)

# ── 按 [GCProbe] 切窗口 ──────────────────────────────────────────────
win_re = re.compile(r"\[GCProbe\] win=(\d+) frames=(\d+) total=(\d+) mean=(\d+) peak=(\d+) grand=(\d+)")
windows = []
cur = {"win": 0, "hits": 0, "hiteff": 0, "segeff": 0, "err": 0}
for m, t in msgs:
    g = win_re.search(m)
    if g:
        cur["win"] = int(g.group(1))
        cur["total"] = int(g.group(3))
        cur["mean"] = int(g.group(4))
        cur["peak"] = int(g.group(5))
        cur["grand"] = int(g.group(6))
        windows.append(cur)
        cur = {"win": 0, "hits": 0, "hiteff": 0, "segeff": 0, "err": 0}
        continue
    if "[HitReact]" in m:
        cur["hits"] += 1
    if "[HitEffect]" in m and "[HitEffectCalc]" not in m:
        cur["hiteff"] += 1
    if "[SegEffect]" in m:
        cur["segeff"] += 1
    if t == "Error" or t == "Exception":
        cur["err"] += 1

if not windows:
    print("\n⚠ 没有任何 [GCProbe] 窗口 —— 探针可能没跑起来")
    for m, t in msgs:
        if "GCProbe" in m:
            print("   探针相关:", m[:160])
    sys.exit(0)

print("\n" + "=" * 78)
print("窗口  frames    total(B)   mean(B/帧)  peak(B)   命中  受击特效  段效果  Error")
print("-" * 78)
for w in windows:
    print("  %2d   %4d  %10d  %9d  %8d  %5d  %6d  %6d  %4d"
          % (w["win"], 120, w["total"], w["mean"], w["peak"],
             w["hits"], w["hiteff"], w["segeff"], w["err"]))

# ── 回归：用「命中数 > 0 的窗口」减去「命中数 == 0 的窗口」基线 ──
busy = [w for w in windows if w["hits"] > 0]
idle = [w for w in windows if w["hits"] == 0]
print("\n" + "=" * 78)
if idle:
    idle_mean = sum(w["mean"] for w in idle) / len(idle)
    print("空闲窗口（无命中）%d 个，平均 %.1f B/帧  ← 常驻基线（索敌/Update/协程等）" % (len(idle), idle_mean))
else:
    idle_mean = 0.0
    print("无空闲窗口（全程在战斗）")

if busy:
    busy_mean = sum(w["mean"] for w in busy) / len(busy)
    print("战斗窗口（有命中）%d 个，平均 %.1f B/帧" % (len(busy), busy_mean))
    extra = busy_mean - idle_mean
    print("战斗额外开销 ≈ %.1f B/帧" % extra)
    tot_hits = sum(w["hits"] for w in busy)
    tot_eff = sum(w["hiteff"] for w in busy)
    print("战斗窗口内 命中 %d 次 / 受击特效 %d 次" % (tot_hits, tot_eff))
    if tot_hits:
        print("⇒ 摊到每次命中 ≈ %.0f B（含 Editor 诊断日志，真机无此项）"
              % (sum(w["total"] for w in busy) / tot_hits))
print("=" * 78)
