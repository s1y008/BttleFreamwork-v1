# -*- coding: utf-8 -*-
"""
P1①：AttackState.OnExit 补一次收尾 —— 被打断时也要清空当前技能。

【问题】状态转换表允许 Attack → Stun / Silence / Knockback / Dead，
        这几条路径只调用 AttackState.OnExit()，不会经过 FinishCast()
        ⇒ m_CurSelectedSkill 残留 ⇒ 玩家从硬控恢复回 Idle 后，
        AutoFightUpdate 每帧「技能非空 + 有目标」⇒ 没按 A 也自动再放一次技能。

【改法】OnExit 开头：若本次施法还没收过尾（_castFinished == false），
        补一次 Owner.OnSkillCastFinished()。只回调、不切状态。

【脚本纪律】
  ① 二进制读写，保留 BOM 与 CRLF；
  ② 插入型替换 ⇒ 幂等判据用「标记已存在即跳过」（不能用 old 命中 0 判断）；
  ③ old 命中数必须 == 1；
  ④ 写回后立即重读复验，再延迟数秒复验一次（Edit 曾有静默回退前科）。
"""
import os
import time

TARGET = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\StateMachine\CombatStates.cs"
BACKUP = TARGET + ".bak.2026-09-29-castinterrupt"

MARKER = "// ★ 2026-09-29 P1①：被打断时也必须走收尾出口。"

HEAD = "    public override void OnExit()\n    {\n"
TAIL = "        // ★ v5 P17 fail-fast：正常退出的前提是"
OLD = HEAD + TAIL

INSERT = (
    "        // ★ 2026-09-29 P1①：被打断时也必须走收尾出口。\n"
    "        //   转换表允许 Attack → Stun / Silence / Knockback / Dead，这几条路径**只调用 OnExit**，\n"
    "        //   不会经过 FinishCast() ⇒ m_CurSelectedSkill 残留 ⇒ 玩家从硬控恢复回 Idle 后，\n"
    "        //   AutoFightUpdate 每帧「技能非空 + 有目标」⇒ **没按 A 也自动再放一次技能**。\n"
    "        //\n"
    "        //   ⚠ 这里只回调、不切状态：本方法由 ChangeState 在切换过程中同步调用，\n"
    "        //      此刻再 RequestState 属于嵌套切换（状态机不支持），Idle 由打断方自己负责进入。\n"
    "        //\n"
    "        //   ⚠ 下方那条 P17 fail-fast 在本分支（_castFinished == false = 被打断）会照常触发，\n"
    "        //      此时「特效没播完」是打断的正常结果，不是配置错 —— 看到它请先确认是不是被打断。\n"
    "        if (!_castFinished)\n"
    "        {\n"
    "            _castFinished = true;\n"
    "            Owner.OnSkillCastFinished();\n"
    "        }\n"
    "\n"
)

NEW = HEAD + INSERT + TAIL


def read_text(path):
    with open(path, "rb") as f:
        raw = f.read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    if bom:
        raw = raw[3:]
    text = raw.decode("utf-8")
    crlf = "\r\n" in text
    text = text.replace("\r\n", "\n")
    return text, bom, crlf


def write_text(path, text, bom, crlf):
    if crlf:
        text = text.replace("\n", "\r\n")
    data = text.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    with open(path, "wb") as f:
        f.write(data)


def verify():
    text, _, _ = read_text(TARGET)
    n_marker = text.count(MARKER)
    n_new = text.count(NEW)
    n_old = text.count(OLD)
    ok = (n_marker == 1) and (n_new == 1) and (n_old == 0)
    print("   复验 marker=%d new=%d old=%d -> %s" % (n_marker, n_new, n_old, "OK" if ok else "FAIL"))
    return ok


def main():
    text, bom, crlf = read_text(TARGET)
    print("   读取 %d 字符 BOM=%s CRLF=%s" % (len(text), bom, crlf))

    # 幂等：标记已存在即跳过（插入型替换不能用 old 命中 0 判断）
    if text.count(MARKER) > 0:
        print("   SKIP：改动已存在，无需重复应用")
        return verify()

    n_old = text.count(OLD)
    if n_old != 1:
        print("   FAIL：old 命中 %d 次（期望 1），中止" % n_old)
        raise SystemExit(1)

    if not os.path.exists(BACKUP):
        with open(TARGET, "rb") as f:
            data = f.read()
        with open(BACKUP, "wb") as f:
            f.write(data)
        print("   备份 → %s" % os.path.basename(BACKUP))
    else:
        print("   备份已存在，跳过")

    text = text.replace(OLD, NEW, 1)
    write_text(TARGET, text, bom, crlf)
    print("   已写回，立即复验：")
    if not verify():
        raise SystemExit(1)

    print("   延迟 3 秒后再复验一次（防静默回退）：")
    time.sleep(3)
    if not verify():
        raise SystemExit(1)
    print("   DONE")


if __name__ == "__main__":
    main()
