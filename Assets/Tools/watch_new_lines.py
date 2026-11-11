# -*- coding: utf-8 -*-
"""
只看 Editor.log 中基线行之后的新增内容（本轮 Play 的干净证据）。

用法: python watch_new_lines.py <baselineLine> [tailWindow]
"""
import io, re, collections, sys

LOG = r"C:\Users\Administrator\AppData\Local\Unity\Editor\Editor.log"
OUT = r"E:\Project\BttleFreamwork-v1\Assets\_new_watch.txt"

base = int(sys.argv[1]) if len(sys.argv) > 1 else 0

lines = io.open(LOG, "r", encoding="utf-8", errors="replace").read().split("\n")
new = lines[base:] if base else lines[-60000:]

buf = ["基线行=%d，当前总行=%d，新增=%d 行" % (base, len(lines), len(new))]

MARKS = ["[AddBuff]", "[BuffTick]", "[HitEffectCalc]", "[SegEffect]", "[HitReact]",
         "[BuffSystem]", "[DoT]", "ValidateBuffBindings", "[UnitView]", "Exception"]
buf.append("")
buf.append("=== 新增区段的标记计数 ===")
for mk in MARKS:
    buf.append("  %-24s %d" % (mk, sum(1 for l in new if mk in l)))

buf.append("")
buf.append("=== 新增区段打出的技能 id 分布 ===")
c = collections.Counter()
for l in new:
    if "[HitEffectCalc]" in l or "[SegEffect]" in l:
        m = re.search(r"技能\s*(\d+)", l)
        if m:
            c[m.group(1)] += 1
for k, v in c.most_common():
    buf.append("   技能 %-8s %d" % (k, v))

def dump(tag, limit=30):
    buf.append("")
    buf.append("=== %s （前 %d 条，原文）===" % (tag, limit))
    got = [l.strip() for l in new if tag in l]
    if not got:
        buf.append("   （无）")
        return
    for s in got[:limit]:
        buf.append("   " + s[:200])
    buf.append("   ... 共 %d 条" % len(got))

dump("[AddBuff]")
dump("[BuffTick]")
dump("[BuffSystem]")

buf.append("")
buf.append("=== 新增区段的 Error 去重（前 12）===")
errs = [l for l in new if "[SkillData]" in l or "[UnitView]" in l or "Exception" in l]
cnt = collections.Counter(re.sub(r"\d+", "N", e.strip()[:110]) for e in errs)
for k, v in cnt.most_common(12):
    buf.append("  x%-4d %s" % (v, k))

io.open(OUT, "w", encoding="utf-8").write("\n".join(buf))
print("\n".join(buf))
