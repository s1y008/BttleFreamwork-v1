# -*- coding: utf-8 -*-
"""
统计 SkillData.csv 列 13（SkillEffectTypes）的「每段效果个数」分布。

用途：评估「把 Skill.cs:290 的 new SkillEffectContext 提到效果循环外」
     能减少多少次构造（一次命中 1 个 ctx vs 每个效果 1 个 ctx）。

只读，不写任何资产。
"""
import os
import collections
import io

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # .../Assets
CSV = os.path.join(ROOT, "Resources", "Config", "SkillData.csv")
COL_EFFECT = 13
COL_SEGCOUNT = 26
COL_ID = 0

def main():
    with io.open(CSV, "r", encoding="utf-8-sig") as f:
        lines = [ln.rstrip("\r\n") for ln in f if ln.strip()]

    header = lines[0].split(",")
    print("CSV 列数 =", len(header))
    print("列 13 =", header[COL_EFFECT], "| 列 26 =", header[COL_SEGCOUNT])
    print("-" * 60)

    per_seg = []          # 每个非空段的效果个数
    segcounts = collections.Counter()
    rows_with_effect = 0
    max_per_seg = 0
    max_detail = None

    for ln in lines[1:]:
        cols = ln.split(",")
        if len(cols) <= COL_EFFECT:
            continue
        raw = cols[COL_EFFECT].strip()
        if not raw:
            continue
        rows_with_effect += 1
        try:
            seg = int(cols[COL_SEGCOUNT]) if len(cols) > COL_SEGCOUNT else 0
        except ValueError:
            seg = 0
        segcounts[seg] += 1

        for group in raw.split("|"):
            g = group.strip()
            if not g:
                continue
            n = len([x for x in g.split(";") if x.strip() != ""])
            if n <= 0:
                continue
            per_seg.append(n)
            if n > max_per_seg:
                max_per_seg = n
                max_detail = (cols[COL_ID], raw)

    dist = collections.Counter(per_seg)
    total = len(per_seg)
    print("配了效果的技能行数 =", rows_with_effect)
    print("非空「段」总数     =", total)
    print("-" * 60)
    print("每段效果个数分布：")
    for k in sorted(dist):
        print("   每段 %d 个效果 : %4d 段  (%.1f%%)" % (k, dist[k], 100.0 * dist[k] / total))
    avg = sum(per_seg) * 1.0 / total
    print("-" * 60)
    print("平均每段效果数 = %.2f" % avg)
    print("⇒ 把 ctx 提到循环外，构造次数可从「每段 %.2f 次」降到「每段 1 次」" % avg)
    print("  （降幅 %.0f%%）" % (100.0 * (1 - 1.0 / avg)))
    print("最多的一段：技能 %s  raw=%s  效果数=%d" % (max_detail[0], max_detail[1], max_per_seg))
    print("-" * 60)
    print("SegCount 分布（配了效果的技能）：")
    for k in sorted(segcounts):
        print("   SegCount=%d : %d 个技能" % (k, segcounts[k]))

if __name__ == "__main__":
    main()
