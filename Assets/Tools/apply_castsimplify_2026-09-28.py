# -*- coding: utf-8 -*-
"""
Skill.cs 构造点改造：new SkillCastContext(this)
★ 依据 MEMORY 铁律五.1：改 .cs 大文件一律走 Python 脚本（Edit 工具写入被静默回退过）

纪律：
  ① 每块 old 命中数 == 1 断言
  ② 写回后重读复验
  ③ 幂等（新串已在则跳过）
  ④ 保持 CRLF / LF 原样
  ⑤ 复验忽略注释行
"""
import io, os, sys, shutil

ROOT = os.path.dirname(os.path.abspath(__file__))          # = .../Assets/Tools
ASSETS = os.path.dirname(ROOT)                             # = .../Assets
TARGET = os.path.join(ASSETS, "Scripts", "BattleFreamwork", "Skill.cs")

OLD = """        // ★ 2026-09-27（方案 F）：段级施法上下文 —— 本段内恒定不变，全段**只构造这一次**；
        //   命中目标变化频率最高，走 Execute 的第二参数，不进本结构。
        var cast = new SkillCastContext(m_SkillCaseter, this, BaseData, m_SegIdx);
"""

NEW = """        // ★ 2026-09-27（方案 F）：段级施法上下文 —— 本段内恒定不变，全段**只构造这一次**；
        //   命中目标变化频率最高，走 Execute 的第二参数，不进本结构。
        // ★ 2026-09-28：Caster / BaseData / SegIdx 不再由此处逐个传入 —— 它们是本类自己的东西，
        //   SkillCastContext 内部从 Skill 派生（SegIdx 在构造瞬间取快照），真相源只有一个。
        var cast = new SkillCastContext(this);
"""


def read_raw(p):
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        return f.read()


def split_lines_ignore_comments(text):
    """返回忽略注释行（// ///*）后的列表，用于残留判定"""
    out = []
    for ln in text.replace("\r\n", "\n").split("\n"):
        s = ln.strip()
        if s.startswith("//") or s.startswith("*"):
            continue
        out.append(ln)
    return out


def main():
    if not os.path.exists(TARGET):
        print("✗ 目标不存在: %s" % TARGET)
        return 1

    raw = read_raw(TARGET)
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")

    print("目标: %s" % TARGET)
    print("换行: %s   原大小: %d 字节" % ("CRLF" if crlf else "LF", len(raw)))

    n_old = text.count(OLD)
    n_new = text.count(NEW)

    if n_new == 1 and n_old == 0:
        print("→ 已是目标状态，跳过（幂等）")
        ok = True
    elif n_old == 1:
        text = text.replace(OLD, NEW, 1)
        out = text.replace("\n", "\r\n") if crlf else text
        with io.open(TARGET, "w", encoding="utf-8", newline="") as f:
            f.write(out)
        print("→ 已写入")
        ok = True
    else:
        print("✗ 断言失败：old 命中 %d 次（期望 1），new 命中 %d 次" % (n_old, n_new))
        return 1

    # ---- 写回复验 ----
    back = read_raw(TARGET).replace("\r\n", "\n")

    if NEW not in back:
        print("✗ 复验失败：找不到新串")
        ok = False
    else:
        print("✓ 复验：新串存在")

    # 残留判定：4 参数构造形式的非注释行必须为 0
    code_lines = split_lines_ignore_comments(back)
    leftover = [l for l in code_lines if "new SkillCastContext(" in l and "new SkillCastContext(this)" not in l]
    if leftover:
        print("✗ 复验失败：仍有非单参数构造 -> %r" % [x.strip() for x in leftover])
        ok = False
    else:
        print("✓ 复验：无残留的多参数构造")

    hits = [i + 1 for i, l in enumerate(back.split("\n")) if "new SkillCastContext(this)" in l]
    print("✓ 单参数构造所在行: %s" % hits)

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
