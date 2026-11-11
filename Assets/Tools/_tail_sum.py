# -*- coding: utf-8 -*-
"""本轮 Play 日志分析：只看 baseline 之后的新增行，统计各链路计数并抽样本。"""
import io, re, sys

LOG = r"C:\Users\Administrator\AppData\Local\Unity\Editor\Editor.log"
BASELINE = int(sys.argv[1]) if len(sys.argv) > 1 else 0

KEYS = ["[BuffTick]", "[AddBuff]", "[Combat]", "[HitReact]", "[SegEffect]",
        "[HitEffectCalc]", "[HitEffect]", "Exception", "error CS"]

lines = io.open(LOG, "r", encoding="utf-8", errors="replace").read().splitlines()
new = lines[BASELINE:]

counts = {}
for k in KEYS:
    counts[k] = sum(1 for l in new if k in l)

print("本轮新增 %d 行" % len(new))
print("--- 计数 ---")
for k in KEYS:
    print("  %-18s %d" % (k, counts[k]))

def sample(tag, n=6):
    hits = [l for l in new if tag in l]
    print("--- %s 样本（共 %d） ---" % (tag, len(hits)))
    for l in hits[:n]:
        print("   " + l.strip()[:200])
    tail = hits[-3:] if len(hits) > n else []
    if tail:
        print("   ...尾部:")
        for l in tail:
            print("   " + l.strip()[:200])

for t in ("[AddBuff]", "[BuffTick]", "[Combat]"):
    sample(t)

# 敌人血量轨迹（取 GBL）
print("--- GBL 血量/HP 相关样本 ---")
hits = [l for l in new if "GBL" in l and ("HP=" in l or "受击" in l)]
for l in hits[:8]:
    print("   " + l.strip()[:200])
print("   （共 %d 条）" % len(hits))

# 死亡相关
print("--- 死亡相关 ---")
for l in new:
    if "Dead" in l or "死亡" in l or "OnDead" in l:
        print("   " + l.strip()[:200])
