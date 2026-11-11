# -*- coding: utf-8 -*-
"""扫描本次 Play 段落内的关键链路日志（Info/Log 级别也能抓到）。"""
import io, collections, re, sys

LOG = r"C:\Users\Administrator\AppData\Local\Unity\Editor\Editor.log"
OUT = r"E:\Project\BttleFreamwork-v1\Assets\_logsana2.txt"

lines = io.open(LOG, "r", encoding="utf-8", errors="replace").read().split("\n")

# 本工程 Editor.log 里可能有多个 Unity 实例段落，用最后一段即可
buf = ["Editor.log 总行数 = %d" % len(lines)]

MARKS = [
    "[JsonConfig]", "[SkillLoad]", "[HitEffectCalc]", "[HitEffect]", "[SegEffect]",
    "[HitReact]", "[CastCtx]", "[AddBuff]", "[BuffAdd]", "[BuffTick]", "[DoT]",
    "[Buff]", "[Skill]", "[Config]", "流血", "1003",
]

buf.append("")
buf.append("=== 全文关键标记计数（含历史） ===")
for mk in MARKS:
    n = sum(1 for l in lines if mk in l)
    buf.append("  %-18s %d" % (mk, n))

# 找本次 Play 起点（最后一次 "EnterPlayMode" / "play mode" 标记）
start = 0
for kw in ("Enter Play Mode", "EnterPlayMode", "- Completed reload", "Initialize engine version"):
    for i in range(len(lines) - 1, -1, -1):
        if kw in lines[i]:
            start = max(start, 0)
            break

# 简单起见：从最后 300000 行里找
tail = lines[-300000:] if len(lines) > 300000 else lines
buf.append("")
buf.append("=== 尾部 %d 行内的关键标记 ===" % len(tail))
for mk in MARKS:
    hits = [l for l in tail if mk in l]
    buf.append("  %-18s %d" % (mk, len(hits)))

buf.append("")
buf.append("=== 尾部 [HitEffectCalc] 样本（前 8 条去重） ===")
seen = []
for l in tail:
    if "[HitEffectCalc]" in l:
        s = re.sub(r"\d+", "N", l.strip()[:150])
        if s not in seen:
            seen.append(s)
        if len(seen) >= 8:
            break
for s in seen:
    buf.append("   " + s)

buf.append("")
buf.append("=== 尾部含 AddBuff / Buff / DoT / 流血 的样本（前 12 条去重） ===")
seen2 = []
for l in tail:
    if any(k in l for k in ("[AddBuff]", "[Buff", "[DoT]", "流血", "OnAddBuff", "ApplyBuff")):
        s = re.sub(r"\d+", "N", l.strip()[:170])
        if s not in seen2:
            seen2.append(s)
        if len(seen2) >= 12:
            break
for s in seen2:
    buf.append("   " + s)

buf.append("")
buf.append("=== 尾部 Error/Exception 去重计数（前 15） ===")
errs = [l for l in tail if "error CS" in l or "Exception" in l]
cnt = collections.Counter(re.sub(r"\d+", "N", e.strip()[:130]) for e in errs)
for k, v in cnt.most_common(15):
    buf.append("  x%-4d %s" % (v, k))

io.open(OUT, "w", encoding="utf-8").write("\n".join(buf))
print("\n".join(buf))
