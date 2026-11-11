# -*- coding: utf-8 -*-
"""
2026-09-28 删除 SkillData.csv 第 25 列（1-based，下标 24，HitEffectIds）后的代码同步。

CSV 已由 migrate_drop_hiteffectids_col25.py 从 27 列删到 26 列，其后两列下标整体前移：
    CastEffectTimings  旧 idx25 -> 新 idx24（1-based 第 25 列）
    SegCount           旧 idx26 -> 新 idx25（1-based 第 26 列）

本脚本做两件事：
  1. SkillData.cs：下标 25→24 / 26→25，并清理所有指向已删列 24 的注释
  2. Skill.cs：更新受击特效来源注释

安全约束（踩坑总结）：
  1. CRLF：读进来统一转 \n，写回统一还原 \r\n
  2. 幂等：new 子串已存在则跳过该条（防重复执行插两遍）
  3. 断言：每条 old 子串必须在**指定行**内出现且只出现一次，否则中止并提示
  4. 写回后重读复验：所有 new 子串必须存在，所有 old 子串（删除类除外）必须不存在
"""
import os
import sys

ROOT = r"E:\Project\BttleFreamwork-v1\Assets"
SKILLDATA = os.path.join(ROOT, r"Scripts\BattleFreamwork\ConfigData\SkillData.cs")
SKILL = os.path.join(ROOT, r"Scripts\BattleFreamwork\Skill.cs")

# (行号1-based, old子串, new子串)
EDITS_SKILLDATA = [
    # 87-89：受击特效来源注释 —— 列 24 已整列删除
    (87, "此前受击特效走列 24（表格第 25 列，字段 HitEffectIds 已删），",
         "此前受击特效走列 24（表格第 25 列 HitEffectIds）。"),
    (88, "而本列自 v5 P10 起是「已无读取者的遗留字段」。现统一收敛到本列，列 24 停止读取。",
         "该列已于 2026-09-28 整列删除（CSV 总列数 27 → 26，其后各列下标整体前移一位），受击特效统一收敛到本列。"),
    (89, "（CSV 列结构本身未动 —— 删列会让后面列的下标整体前移，属策划手工操作范围。）",
         "（⚠ 定位列号一律以**当前 CSV 表头**为准：本列现在是 0-based 14 / 1-based 第 15 列。）"),

    # 125：历史注释里的列 24 是 2026-09-15 当时的编号，加防误导说明
    (125, "此前还有一个语义完全相同的 CastEffectIds 列(列 24),已整列删除;",
          "此前还有一个语义完全相同的 CastEffectIds 列(**2026-09-15 当时的**列 24),已整列删除;"),

    # 145：SegCount 列号 27 -> 26
    (145, "（int，CSV 列 27，1-based 语义：1 = 单段，3 = 三段）",
          "（int，CSV 列 26，1-based 语义：1 = 单段，3 = 三段）"),

    # 620/628/629：受击特效列号 24 -> 14
    (620, "10003 的出手特效（列 22）与受击特效（列 24）第三段",
          "10003 的出手特效（列 22）与受击特效（列 14）第三段"),
    (628, "① 列 22 与列 24 出现**相同 id**",
          "① 列 22 与列 14 出现**相同 id**"),
    (629, "② 列 22 / 列 24 引用了",
          "② 列 22 / 列 14 引用了"),

    # 726：解析处注释
    (726, "（原列 24 HitEffectIds 已停止读取；CSV 列结构未动，删列需同步后面所有下标）",
          "（原列 24 HitEffectIds 已于 2026-09-28 整列删除；其后两列下标已同步前移：25→24、26→25）"),

    # 746-748：CastEffectTimings 下标 25 -> 24（删掉上方 744/745 两行后行号前移 2）
    (744, "// ★ v5 P5:出手特效独立时间轴(列 25)。留空 → null → 一件特效都不播",
          "// ★ v5 P5:出手特效独立时间轴(列 24)。留空 → null → 一件特效都不播"),
    (745, "CastEffectTimings = proArray.Length > 25 && !string.IsNullOrEmpty(proArray[25])",
          "CastEffectTimings = proArray.Length > 24 && !string.IsNullOrEmpty(proArray[24])"),
    (746, "? CommonHelper.Str2FloatList(proArray[25]).ToArray()",
          "? CommonHelper.Str2FloatList(proArray[24]).ToArray()"),

    # 751-752：SegCount 下标 26 -> 25
    (749, "// ★ v5 P6:伤害总段数(列 26,int)。老 CSV 没有这一列 → 0 → 该技能不分段(保持旧行为)",
          "// ★ v5 P6:伤害总段数(列 25,int)。老 CSV 没有这一列 → 0 → 该技能不分段(保持旧行为)"),
    (750, "SegCount = proArray.Length > 26 ? CommonHelper.Str2Int(proArray[26]) : 0,",
          "SegCount = proArray.Length > 25 ? CommonHelper.Str2Int(proArray[25]) : 0,"),

    # 768：特效 id 契约注释里的受击列号 24 -> 14
    (766, "特效 id 契约：出手(列22)与受击(列24)不得撞车，",
          "特效 id 契约：出手(列22)与受击(列14)不得撞车，"),
]

# 需要整行删除的（精确行内容，去空白后比较）
DELETE_EXACT_SKILLDATA = [
    "// ★ 受击特效 id(列 24)",
]

EDITS_SKILL = [
    (405, "受击特效来源由 CSV 列 24（1-based 第 25 列 HitEffectIds）",
          "受击特效来源由 CSV 列 24（1-based 第 25 列 HitEffectIds，该列已于 2026-09-28 整列删除）"),
    (406, "改为 **列 14（1-based 第 15 列 HitEffect）**，列 24 停止读取（字段已删）。",
          "改为 **列 14（1-based 第 15 列 HitEffect）** —— 全工程唯一受击特效真相源。"),
]


def load(path):
    with open(path, "rb") as f:
        raw = f.read()
    crlf = b"\r\n" in raw
    txt = raw.decode("utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8")
    txt = txt.replace("\r\n", "\n")
    return txt, crlf


def save(path, txt, crlf):
    out = txt.replace("\n", "\r\n") if crlf else txt
    with open(path, "wb") as f:
        f.write(out.encode("utf-8"))


def apply_edits(path, edits, deletes=None):
    txt, crlf = load(path)
    lines = txt.split("\n")
    changed = 0

    for lineno, old, new in edits:
        idx = lineno - 1
        if idx >= len(lines):
            raise SystemExit("[%s] 行号 %d 越界（文件共 %d 行）" % (os.path.basename(path), lineno, len(lines)))
        line = lines[idx]
        if new in line:
            print("  [SKIP] 第%d行 已是新内容" % lineno)
            continue
        n = line.count(old)
        if n != 1:
            raise SystemExit("[%s] 第%d行 old 命中 %d 次（应为1）: %r" % (os.path.basename(path), lineno, n, line))
        lines[idx] = line.replace(old, new, 1)
        changed += 1
        print("  [OK] 第%d行 已替换" % lineno)

    # 删除整行（含其后紧跟的空行）
    if deletes:
        for pat in deletes:
            hit = [i for i, l in enumerate(lines) if l.strip() == pat]
            if len(hit) == 0:
                print("  [SKIP] 删除目标不存在: %r" % pat)
                continue
            if len(hit) != 1:
                raise SystemExit("删除目标命中 %d 次（应为1）: %r" % (len(hit), pat))
            i = hit[0]
            # 若上一行或下一行是空行，一并删掉，避免留下双空行
            span = [i]
            if i + 1 < len(lines) and lines[i + 1].strip() == "":
                span.append(i + 1)
            for j in sorted(span, reverse=True):
                del lines[j]
            changed += 1
            print("  [OK] 已删除行 %r（含其后空行共 %d 行）" % (pat, len(span)))

    new_txt = "\n".join(lines)
    if changed:
        save(path, new_txt, crlf)

    # ---- 复验：重读磁盘 ----
    txt2, crlf2 = load(path)
    for lineno, old, new in edits:
        if new not in txt2:
            raise SystemExit("[%s] 复验失败：新内容缺失 (第%d行) %r" % (os.path.basename(path), lineno, new))
    for pat in (deletes or []):
        if pat in txt2:
            raise SystemExit("[%s] 复验失败：应删内容仍存在 %r" % (os.path.basename(path), pat))
    # 关键：26 列 CSV 下，下标 26 已越界，必须彻底消失
    #      （注意 proArray[25] 是**新合法**下标 SegCount，不能当残留判定 —— 首版就错在这）
    for banned in ["proArray[26]", "proArray.Length > 26"]:
        if banned in txt2:
            raise SystemExit("[%s] 复验失败：仍残留越界下标 %s" % (os.path.basename(path), banned))
    print("  [VERIFY] OK  改动 %d 处  CRLF=%s" % (changed, crlf2))


print("=== SkillData.cs ===")
apply_edits(SKILLDATA, EDITS_SKILLDATA, DELETE_EXACT_SKILLDATA)
print("=== Skill.cs ===")
apply_edits(SKILL, EDITS_SKILL)
print("ALL DONE")
