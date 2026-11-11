# -*- coding: utf-8 -*-
"""
方案 F 验证配套（2026-09-27）：给 SkillCastContext 的构造点加 #if UNITY_EDITOR 计数器。

目的：把「每段只构造一次」从推理变成实测 ——
  Play 后 [CastCtx] 的条数应 == [SegEffect] 的条数（即每段恰好一次）。
配合 readonly struct + in（语言层面保证不装箱、不进堆）⇒ 战斗路径该处零堆分配。

纪律：命中数断言 / 幂等 / 写回复验 / 保留原换行风格。
"""
import io
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))   # .../Assets
SKILL = os.path.join(ROOT, "Scripts", "BattleFreamwork", "Skill.cs")
TAG = "castcounter"

CRLF = {}


def read(p):
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        s = f.read()
    CRLF[p] = "\r\n" in s
    return s.replace("\r\n", "\n")


def write(p, s):
    if CRLF.get(p, False):
        s = s.replace("\n", "\r\n")
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)


BLOCKS = [
    # 1) 计数器字段（放在缓冲区常量旁）
    ("    private const int TARGET_BUFFER_SIZE = 64;\n",
     "    private const int TARGET_BUFFER_SIZE = 64;\n"
     "#if UNITY_EDITOR\n"
     "    /// <summary>方案 F 验证用：SkillCastContext 的构造次数（应与 [SegEffect] 条数相等 ⇒ 每段一次）</summary>\n"
     "    private static int s_CastBuildCount;\n"
     "#endif\n",
     1),
    # 2) 构造点计数 + 日志
    ("        var cast = new SkillCastContext(m_SkillCaseter, this, BaseData, m_SegIdx);\n",
     "        var cast = new SkillCastContext(m_SkillCaseter, this, BaseData, m_SegIdx);\n"
     "#if UNITY_EDITOR\n"
     "        // ★ 验证用（Editor only）：cast 是 struct，构造不进堆；这里只数**次数**，\n"
     "        //   用来实锤「每段只构造一次」—— 条数应等于 [SegEffect]。\n"
     "        s_CastBuildCount++;\n"
     "        Debug.Log($\"[CastCtx] 构造#{s_CastBuildCount} 技能{BaseData.ID} 第{m_SegIdx}段\");\n"
     "#endif\n",
     1),
]


def main():
    txt = read(SKILL)
    print("[1/3] 校验 …")
    for i, (old, new, exp) in enumerate(BLOCKS):
        if new in txt:
            print("  ✓ 第 %d 块已应用（幂等跳过）" % (i + 1))
            continue
        n = txt.count(old)
        if n != exp:
            print("  ✗ 第 %d 块：期望 %d 次，实际 %d 次" % (i + 1, exp, n))
            sys.exit(1)
        print("  ✓ 第 %d 块命中 %d 次" % (i + 1, n))

    print("[2/3] 应用 …")
    changed = False
    for old, new, exp in BLOCKS:
        if new in txt:
            continue
        assert txt.count(old) == exp
        txt = txt.replace(old, new, 1)
        changed = True
    if changed:
        bak = SKILL + ".bak.2026-09-27-" + TAG
        if not os.path.exists(bak):
            shutil.copyfile(SKILL, bak)
            print("    [备份] " + os.path.basename(bak))
        write(SKILL, txt)
        print("    已写入 Skill.cs")
    else:
        print("    无需改动")

    print("[3/3] 复验 …")
    chk = read(SKILL)
    ok = True
    for old, new, exp in BLOCKS:
        if new not in chk:
            print("  ✗ 找不到新块: %r" % new[:60])
            ok = False
    if "s_CastBuildCount++" not in chk:
        print("  ✗ 计数自增未落地")
        ok = False
    if "[CastCtx]" not in chk:
        print("  ✗ [CastCtx] 日志未落地")
        ok = False
    if not ok:
        sys.exit(1)
    print("复验通过 ✓")


if __name__ == "__main__":
    main()
