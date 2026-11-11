# -*- coding: utf-8 -*-
"""
2026-09-29 用户点名要求（属「AI 不代改配置」铁律的例外：用户明确要求改数据）
  ① SkillData.json  技能 10002「天诛」：三段全部挂流血 Buff
        EffectTypesBySegment: [[0],[0,22],[0,22]] -> [[0,2],[0,2,22],[0,2,22]]
        BuffID:               []                  -> [1003]
  ② BufferData.json  ID=1003 流血「改强 + 可叠加」
        MaxStack 1->3 / Stackable false->true / EffectValue 10.0->12.0 / 描述补实

★ 铁律遵守
  · 破坏性改动前已物理备份（.bak.2026-09-29-addbleed）
  · 二进制读写：保留 BOM / CRLF（SkillData 无 BOM 有 CRLF，BufferData 有 CRLF）
  · 只在「目标对象块内」替换，避免误伤同值字段（如 1004 的 MaxStack 也是 3）
  · 每块 old 命中数 == 1 断言；写回后重读复验
  · 幂等判据 = 「new 已存在即跳过」（不能写「old 命中 0 且 new 存在」，插入型会重复插）
"""
import io
import os
import sys

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Resources", "Config")


def read_text(path):
    with open(path, "rb") as f:
        raw = f.read()
    has_bom = raw[:3] == b"\xef\xbb\xbf"
    txt = raw.decode("utf-8-sig").replace("\r\n", "\n")
    return raw, txt, has_bom


def write_text(path, txt, has_bom):
    out = txt.replace("\n", "\r\n")
    data = out.encode("utf-8")
    if has_bom:
        data = b"\xef\xbb\xbf" + data
    with open(path, "wb") as f:
        f.write(data)


def locate_object(txt, anchor):
    """返回 (start, end)：包含 anchor 的那个顶层数组元素块（不含结尾逗号）"""
    i = txt.find(anchor)
    if i < 0:
        raise SystemExit("找不到锚点: %r" % anchor)
    start = txt.rfind("  {", 0, i)
    if start < 0:
        raise SystemExit("锚点 %r 之前找不到对象起始 '  {'" % anchor)
    end = txt.find("\n  },", i)
    if end < 0:
        raise SystemExit("锚点 %r 之后找不到对象结束 '\\n  },'" % anchor)
    return start, end


def replace_in_block(txt, start, end, pairs, tag):
    """在 [start,end) 块内做替换，每块 old 必须恰好命中 1 次"""
    block = txt[start:end]
    for old, new in pairs:
        cnt = block.count(old)
        if cnt == 1:
            # 幂等：new 已在（说明本脚本跑过）→ 跳过
            if new in block and new != old:
                print("  [%s] 跳过（已存在）: %r" % (tag, old[:40]))
                continue
            block = block.replace(old, new)
        elif cnt == 0:
            if new in block:
                print("  [%s] 跳过（已改过）: %r" % (tag, old[:40]))
                continue
            raise SystemExit("[%s] 命中 0 次: %r" % (tag, old))
        else:
            raise SystemExit("[%s] 命中 %d 次（应恰好 1 次）: %r" % (tag, cnt, old))
    return txt[:start] + block + txt[end:]


def main():
    # ─────────────── ① SkillData.json：技能 10002 ───────────────
    p1 = os.path.join(BASE, "SkillData.json")
    raw1, txt1, bom1 = read_text(p1)
    s, e = locate_object(txt1, '"ID": 10002')

    seg_old = (
        '    "EffectTypesBySegment": [\n'
        "      [\n"
        "        0\n"
        "      ],\n"
        "      [\n"
        "        0,\n"
        "        22\n"
        "      ],\n"
        "      [\n"
        "        0,\n"
        "        22\n"
        "      ]\n"
        "    ],"
    )
    seg_new = (
        '    "EffectTypesBySegment": [\n'
        "      [\n"
        "        0,\n"
        "        2\n"          # ← 效果 2 = AddBuff（流血）
        "      ],\n"
        "      [\n"
        "        0,\n"
        "        2,\n"
        "        22\n"
        "      ],\n"
        "      [\n"
        "        0,\n"
        "        2,\n"
        "        22\n"
        "      ]\n"
        "    ],"
    )
    txt1 = replace_in_block(txt1, s, e, [
        (seg_old, seg_new),
        ('"BuffID": [],', '"BuffID": [\n      1003\n    ],'),
    ], "SkillData/10002")

    write_text(p1, txt1, bom1)

    # ─────────────── ② BufferData.json：ID=1003 流血 ───────────────
    p2 = os.path.join(BASE, "BufferData.json")
    raw2, txt2, bom2 = read_text(p2)
    s2, e2 = locate_object(txt2, '"Id": 1003')

    txt2 = replace_in_block(txt2, s2, e2, [
        ('"Description": "rerw"', '"Description": "每1秒扣12血·可叠3层（天诛三段各挂1层）"'),
        ('"MaxStack": 1', '"MaxStack": 3'),
        ('"EffectValue": 10.0', '"EffectValue": 12.0'),
        ('"Stackable": false', '"Stackable": true'),
    ], "BufferData/1003")

    write_text(p2, txt2, bom2)

    # ─────────────── 复验（重新从磁盘读） ───────────────
    ok = True
    _, t1b, _ = read_text(p1)
    s1b, e1b = locate_object(t1b, '"ID": 10002')
    blk = t1b[s1b:e1b]
    checks1 = [
        (seg_new in blk, "10002.EffectTypesBySegment 三段含效果 2"),
        ('"BuffID": [\n      1003\n    ]' in blk, "10002.BuffID = [1003]"),
    ]
    for cond, msg in checks1:
        print(("  ✔ " if cond else "  ✘ 未生效: ") + msg)
        ok = ok and cond

    _, t2b, _ = read_text(p2)
    s2b, e2b = locate_object(t2b, '"Id": 1003')
    blk2 = t2b[s2b:e2b]
    checks2 = [
        ('"MaxStack": 3' in blk2, "1003.MaxStack = 3"),
        ('"Stackable": true' in blk2, "1003.Stackable = true"),
        ('"EffectValue": 12.0' in blk2, "1003.EffectValue = 12.0"),
    ]
    for cond, msg in checks2:
        print(("  ✔ " if cond else "  ✘ 未生效: ") + msg)
        ok = ok and cond

    # JSON 合法性（防止写坏）
    import json
    for p in (p1, p2):
        try:
            d = json.load(open(p, encoding="utf-8-sig"))
            print("  ✔ JSON 可解析: %s (%d 行)" % (os.path.basename(p), len(d)))
        except Exception as ex:
            print("  ✘ JSON 解析失败: %s -> %s" % (os.path.basename(p), ex))
            ok = False

    # CRLF 保持
    for p in (p1, p2):
        r = open(p, "rb").read()
        c = (b"\r\n" in r) and (r.replace(b"\r\n", b"\n").count(b"\n") == r.count(b"\r\n"))
        print(("  ✔ " if c else "  ✘ ") + "CRLF 完整: " + os.path.basename(p))
        ok = ok and c

    print("\n结论: " + ("全部通过 OK" if ok else "存在失败项 FAILED"))
    sys.exit(0 if ok else 1)


main()
