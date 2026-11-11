# -*- coding: utf-8 -*-
"""
2026-09-28 删除 SkillData.csv 第 25 列（HitEffectIds）后的**文档与冒烟断言**同步。

CSV 27 列 -> 26 列，其后两列前移：
    CastEffectTimings  列 25 -> 列 24（idx 24）
    SegCount           列 26 -> 列 25（idx 25）
受击特效统一走列 14（1-based 第 15 列 HitEffect）。

约束：
  1. HTML / JS 均为 CRLF：读进来转 \n，写回还原 \r\n
  2. 幂等：new 已存在则跳过
  3. 每条 old 必须在**指定行**内命中且只命中一次，否则中止
  4. 写回后重读复验：new 全在、old 全不在
"""
import os
import sys

ROOT = r"E:\Project\BttleFreamwork-v1\Assets"
FLOW = os.path.join(ROOT, "战斗框架全链路与数据流转.html")
SEG = os.path.join(ROOT, "战斗框架技术文档_段号驱动与状态机推进.html")
SMOKE = os.path.join(ROOT, r"Tools\smoke_html.js")

EDITS_FLOW = [
    (140, '<span class="chip">技能配置列 <b>27 列</b></span>',
          '<span class="chip">技能配置列 <b>26 列</b></span>'),

    (203, '<td><b>SegCount</b> <span class="tag p1">新</span></td><td>CSV 列 26</td>',
          '<td><b>SegCount</b> <span class="tag p1">新</span></td><td>CSV 列 25</td>'),

    (206, '<td><b>CastEffectTimings</b></td><td>CSV 列 25</td>',
          '<td><b>CastEffectTimings</b></td><td>CSV 列 24</td>'),

    # 受击特效：整行替换 —— 旧列 24 HitEffectIds 已删，改列 14 HitEffect
    (207,
     '<tr><td><b>HitEffectIds</b></td><td>CSV 列 24</td><td><code>10002;10001;10002</code></td><td>受击特效，由<b>攻击方</b> <code>Skill.GetHitEffectIdForSegment(SegIdx)</code> 按段算出后随 <code>OnDamaged</code> 入参传入（<b>v5 P10 前</b>是受击方反查攻击者的 SegIdx）</td></tr>',
     '<tr><td><b>HitEffect</b> <span class="tag p1">唯一来源</span></td><td>CSV 列 14</td><td><code>10017</code></td><td><b>受击特效 id 的唯一来源</b>，由<b>攻击方</b> <code>Skill.GetHitEffectIdForSegment(SegIdx)</code> 按段算出后随 <code>OnDamaged</code> 入参传入（<b>v5 P10 前</b>是受击方反查攻击者的 SegIdx）。<span class="tag p1">2026-09-28</span> 原第 25 列 <code>HitEffectIds</code> 已整列删除，受击特效统一走本列；<b>段数多于特效数时复用最后一个</b> —— 只配 1 个即所有段都播它</td></tr>'),

    (594,
     '["SkillData.csv","27 列 · v5 新增列 25/26（CastEffectTimings / SegCount）；原 SegEffectTypes 列已并入列 13"]',
     '["SkillData.csv","26 列 · 列 24/25 为 CastEffectTimings / SegCount；原第 25 列 HitEffectIds 已于 2026-09-28 整列删除；原 SegEffectTypes 列已并入列 13"]'),

    # 时间轴刻度：受击特效改成列 14 的 10017，三段复用
    (683, '[0.30,"段 1 · 0.30","","HitEffectIds[0] = 10002"],',
          '[0.30,"段 1 · 0.30","","HitEffect = 10017"],'),
    (685, '[0.60,"段 2 · 0.60","","HitEffectIds[1] = 10001"],',
          '[0.60,"段 2 · 0.60","","HitEffect = 10017（复用末位）"],'),
    (687, '[0.90,"段 3 · 0.90","","HitEffectIds[2] = 10002"]];',
          '[0.90,"段 3 · 0.90","","HitEffect = 10017（复用末位）"]];'),
]

EDITS_SEG = [
    (480, '<span class="com">// ★ 出手特效的独立时间轴 —— CSV 列 25，与 AttackEffects 按下标配对</span>',
          '<span class="com">// ★ 出手特效的独立时间轴 —— CSV 列 24，与 AttackEffects 按下标配对</span>'),
    (484, '<span class="com">// ★ 伤害总段数 —— CSV 列 26，段数的唯一真相源</span>',
          '<span class="com">// ★ 伤害总段数 —— CSV 列 25，段数的唯一真相源</span>'),
    (815, '<b>共 27 列</b>，与段号 / 出手特效相关的列如下',
          '<b>共 26 列</b>，与段号 / 出手特效相关的列如下'),
    (819, 'SkillData.csv · 段号 / 特效相关列（共 27 列）',
          'SkillData.csv · 段号 / 特效相关列（共 26 列）'),
    (822, '<span class="com"># 真实文件共 27 列，此处只列与段号 / 特效相关的 6 列（列号不连续）</span>',
          '<span class="com"># 真实文件共 26 列，此处只列与段号 / 特效相关的 6 列（列号不连续）</span>'),
    (823, '<span class="com">#   [13]SkillEffectTypes  [22]AttackEffectID  [23]SegTimings  [24]HitEffectIds</span>',
          '<span class="com">#   [13]SkillEffectTypes  [14]HitEffect  [22]AttackEffectID  [23]SegTimings</span>'),
    (827, 'SkillId, SkillEffectTypes, AttackEffectID,     SegTimings,   HitEffectIds,      CastEffectTimings,    SegCount',
          'SkillId, SkillEffectTypes, HitEffect,   AttackEffectID,   SegTimings,   CastEffectTimings,   SegCount'),
    (839, '<b>列 26 · 伤害总段数（策划显式配置）</b>',
          '<b>列 25 · 伤害总段数（策划显式配置）</b>'),
    (842, '列 25 · 出手特效的<b>独立时机轴</b>',
          '列 24 · 出手特效的<b>独立时机轴</b>'),
    (843,
     '<tr><td><code>HitEffectIds</code></td><td>List&lt;int&gt;</td><td>列 24 · 受击特效 id，由<b>攻击方</b> <code>Skill.GetHitEffectIdForSegment(SegIdx)</code> 按段算出后随 OnDamaged 入参传入（v5 P10 前是受击方反查攻击者的 SegIdx）</td></tr>',
     '<tr><td><code>HitEffect</code></td><td>List&lt;int&gt;</td><td><b>列 14 · 受击特效 id 的唯一来源</b>，由<b>攻击方</b> <code>Skill.GetHitEffectIdForSegment(SegIdx)</code> 按段算出后随 OnDamaged 入参传入（v5 P10 前是受击方反查攻击者的 SegIdx）。<b>2026-09-28：原第 25 列 <code>HitEffectIds</code> 已整列删除</b>；段数多于特效数时复用最后一个</td></tr>'),
    (849, '<code data-page-node-id="iG0BoVVa1CmUNRgsUqCDH4">HitEffectIds</code> 用分号分隔多个 int',
          '<code data-page-node-id="iG0BoVVa1CmUNRgsUqCDH4">HitEffect</code> 用分号分隔多个 int'),
]

EDITS_SMOKE = [
    (66, "check('配置列已改为 27', /技能配置列 <b>27 列<\\/b>/.test(html));",
         "check('配置列已改为 26', /技能配置列 <b>26 列<\\/b>/.test(html));"),
    (68, "check('CastEffectTimings 列号 25', /<b>CastEffectTimings<\\/b>[\\s\\S]{0,60}CSV 列 25/.test(html));",
         "check('CastEffectTimings 列号 24', /<b>CastEffectTimings<\\/b>[\\s\\S]{0,60}CSV 列 24/.test(html));"),
    (69, "check('HitEffectIds 列号 24', /<b>HitEffectIds<\\/b>[\\s\\S]{0,60}CSV 列 24/.test(html));",
         "check('HitEffect 列号 14', /<b>HitEffect<\\/b>[\\s\\S]{0,60}CSV 列 14/.test(html));"),
    (70, "check('SegCount 列号 26', /<b>SegCount<\\/b>[\\s\\S]{0,80}CSV 列 26/.test(html));",
         "check('SegCount 列号 25', /<b>SegCount<\\/b>[\\s\\S]{0,80}CSV 列 25/.test(html));"),
]


def load(path):
    with open(path, "rb") as f:
        raw = f.read()
    crlf = b"\r\n" in raw
    bom = raw.startswith(b"\xef\xbb\xbf")
    txt = raw.decode("utf-8-sig" if bom else "utf-8").replace("\r\n", "\n")
    return txt, crlf, bom


def save(path, txt, crlf, bom):
    out = txt.replace("\n", "\r\n") if crlf else txt
    data = out.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    with open(path, "wb") as f:
        f.write(data)


def apply(path, edits):
    name = os.path.basename(path)
    txt, crlf, bom = load(path)
    lines = txt.split("\n")
    changed = 0
    for lineno, old, new in edits:
        idx = lineno - 1
        if idx >= len(lines):
            raise SystemExit("[%s] 行号 %d 越界（共 %d 行）" % (name, lineno, len(lines)))
        line = lines[idx]
        if new in line:
            print("  [SKIP] 第%d行 已是新内容" % lineno)
            continue
        n = line.count(old)
        if n != 1:
            raise SystemExit("[%s] 第%d行 old 命中 %d 次（应为1）\n    行内容: %s"
                             % (name, lineno, n, line[:200]))
        lines[idx] = line.replace(old, new, 1)
        changed += 1
        print("  [OK] 第%d行 已替换" % lineno)

    if changed:
        save(path, "\n".join(lines), crlf, bom)

    # 复验
    txt2, _, _ = load(path)
    for lineno, old, new in edits:
        if new not in txt2:
            raise SystemExit("[%s] 复验失败：新内容缺失（第%d行）" % (name, lineno))
    # 注意：新写的说明文字里会**主动提到**"HitEffectIds 已整列删除"，
    #       所以不能简单判 "HitEffectIds" 是否出现 —— 首版就错在这。
    #       只判结构性残留：字段名单列出现 / 数组下标引用 / 旧列号单元格 / 旧总列数
    for banned in ["<b>HitEffectIds</b>", "<code>HitEffectIds</code></td>", "HitEffectIds[",
                   "CSV 列 26</td>", "共 27 列", "27 列</b>", "CSV 列 26 ·"]:
        if banned in txt2:
            raise SystemExit("[%s] 复验失败：仍残留 %r" % (name, banned))
    print("  [VERIFY] OK  改动 %d 处" % changed)


print("=== 战斗框架全链路与数据流转.html ===")
apply(FLOW, EDITS_FLOW)
print("=== 战斗框架技术文档_段号驱动与状态机推进.html ===")
apply(SEG, EDITS_SEG)
print("=== Tools/smoke_html.js ===")
apply(SMOKE, EDITS_SMOKE)
print("ALL DONE")
