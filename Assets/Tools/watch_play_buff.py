# -*- coding: utf-8 -*-
"""只看 Editor.log 尾部（本次 Play 产生的日志），统计 Buff 链路证据。"""
import io, re, collections, sys

LOG = r"C:\Users\Administrator\AppData\Local\Unity\Editor\Editor.log"
OUT = r"E:\Project\BttleFreamwork-v1\Assets\_play_watch.txt"
TAIL = int(sys.argv[1]) if len(sys.argv) > 1 else 60000

lines = io.open(LOG, "r", encoding="utf-8", errors="replace").read().split("\n")
tail = lines[-TAIL:] if len(lines) > TAIL else lines

buf = ["Editor.log 总行数=%d，尾部窗口=%d 行" % (len(lines), len(tail))]

MARKS = ["[AddBuff]", "[BuffTick]", "[HitEffectCalc]", "[SegEffect]", "[HitReact]",
         "[BuffSystem]", "[DoT]", "ValidateBuffBindings", "[UnitView]", "[JsonConfig]"]
buf.append("")
buf.append("=== 尾部窗口内的标记计数 ===")
for mk in MARKS:
    buf.append("  %-24s %d" % (mk, sum(1 for l in tail if mk in l)))

buf.append("")
buf.append("=== [AddBuff] 全部去重 ===")
seen = []
for l in tail:
    if "[AddBuff]" in l:
        s = re.sub(r"\d+", "N", l.strip()[:160])
        if s not in seen:
            seen.append(s)
if seen:
    for s in seen:
        buf.append("   " + s)
else:
    buf.append("   （无）")

buf.append("")
buf.append("=== [BuffTick] 全部去重（上限 25） ===")
seen2 = []
for l in tail:
    if "[BuffTick]" in l:
        s = re.sub(r"\d+", "N", l.strip()[:160])
        if s not in seen2:
            seen2.append(s)
        if len(seen2) >= 25:
            break
if seen2:
    for s in seen2:
        buf.append("   " + s)
else:
    buf.append("   （无）")

buf.append("")
buf.append("=== [BuffSystem] 错误（若空说明挂 Buff 无失败） ===")
bs = [l.strip()[:200] for l in tail if "[BuffSystem]" in l]
if bs:
    for s in bs[:10]:
        buf.append("   " + s)
else:
    buf.append("   （无）")

buf.append("")
buf.append("=== 尾部 Error 去重（上限 15） ===")
errs = [l for l in tail if "[SkillData]" in l or "[UnitView]" in l or "Exception" in l]
cnt = collections.Counter(re.sub(r"\d+", "N", e.strip()[:120]) for e in errs)
for k, v in cnt.most_common(15):
    buf.append("  x%-4d %s" % (v, k))

io.open(OUT, "w", encoding="utf-8").write("\n".join(buf))
print("\n".join(buf))
