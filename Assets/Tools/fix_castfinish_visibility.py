# -*- coding: utf-8 -*-
"""
修正可见性：OnSkillCastFinished 由 AttackState（CombatStateBase 子类）经 Owner 调用，
不是 Controller 的子类 ⇒ protected 不可访问（CS0122）。
按框架惯例改为 public —— 状态类要用的 RequestState / NavStop / DoAutoFight 同为此例。
"""
import io, os, sys

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Controller\CombatUnitController.cs"


def read():
    raw = io.open(PATH, "rb").read()
    return (raw.decode("utf-8-sig" if raw[:3] == b"\xef\xbb\xbf" else "utf-8").replace("\r\n", "\n"),
            b"\r\n" in raw, raw[:3] == b"\xef\xbb\xbf")


def write(txt, crlf, bom):
    out = txt.replace("\n", "\r\n") if crlf else txt
    data = out.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    io.open(PATH, "wb").write(data)


OLD = "    protected virtual void OnSkillCastFinished()"
NEW = "    public virtual void OnSkillCastFinished()"

OLD_NOTE = """    /// ★ 基类默认是**空实现**：敌人靠「持续持有 m_CurSelectedSkill」实现连续攻击，
    ///   在这里清空会打断它的 AI 节奏，所以什么都不做。"""
NEW_NOTE = """    /// ★ 基类默认是**空实现**：敌人靠「持续持有 m_CurSelectedSkill」实现连续攻击，
    ///   在这里清空会打断它的 AI 节奏，所以什么都不做。
    ///
    /// ⚠ 为什么是 public 而不是 protected：调用方 AttackState 是 CombatStateBase 的子类，
    ///   **不是**本类的子类 ⇒ protected 对它不可见（CS0122）。与状态类要用的
    ///   RequestState / NavStop / DoAutoFight 保持一致，都走 public。"""

txt, crlf, bom = read()

if NEW in txt:
    print("跳过（已是 public，幂等）")
else:
    n = txt.count(OLD)
    if n != 1:
        print("失败：OLD 命中 %d 次（期望 1）" % n)
        sys.exit(1)
    txt = txt.replace(OLD, NEW, 1)

    n2 = txt.count(OLD_NOTE)
    if n2 != 1:
        print("失败：注释锚点命中 %d 次（期望 1）" % n2)
        sys.exit(1)
    txt = txt.replace(OLD_NOTE, NEW_NOTE, 1)

    write(txt, crlf, bom)
    print("✔ 已改为 public 并补充可见性说明")

back, _, _ = read()
if back.count(NEW) != 1 or "protected virtual void OnSkillCastFinished" in back:
    print("复验失败")
    sys.exit(1)
print("✔ 复验通过")
