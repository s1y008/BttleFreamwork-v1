# -*- coding: utf-8 -*-
"""
【临时验证脚手架，验证完必须移除】toggle_sim_press.py on|off

为什么要它：玩家出手靠 Input.GetKeyDown(KeyCode.A)，自动化按不了键。
临时让「按一次 A」在 Play 首帧自动发生一次，就能观察：
  修复前 = 反复起手（无限连击）；修复后 = 只打一轮（3 段）然后停手。
"""
import io, os, sys

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Controller\WarrorController.cs"

FIELD_OLD = "    private float timer = 3;\n    private float time = 0;\n"
FIELD_NEW = ("    private float timer = 3;\n    private float time = 0;\n"
             "\n"
             "#if UNITY_EDITOR\n"
             "    // ★★ 临时验证脚手架（验证完必须删除）：模拟「按一次 A 键」，只在首帧触发一次。\n"
             "    private bool _simPressOnce = true;\n"
             "#endif\n")

PRESS_OLD = "        if (Input.GetKeyDown(KeyCode.A))\n"
PRESS_NEW = ("        bool pressedA = Input.GetKeyDown(KeyCode.A);\n"
             "#if UNITY_EDITOR\n"
             "        if (_simPressOnce) { pressedA = true; _simPressOnce = false; }\n"
             "#endif\n"
             "        if (pressedA)\n")


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


mode = sys.argv[1] if len(sys.argv) > 1 else "on"
txt, crlf, bom = read()

if mode == "on":
    if PRESS_NEW in txt:
        print("跳过（脚手架已装好）")
    else:
        for old, desc in ((FIELD_OLD, "字段"), (PRESS_OLD, "按键判断")):
            if txt.count(old) != 1:
                print("失败：%s 锚点命中 %d 次（期望 1）" % (desc, txt.count(old)))
                sys.exit(1)
        txt = txt.replace(FIELD_OLD, FIELD_NEW, 1).replace(PRESS_OLD, PRESS_NEW, 1)
        write(txt, crlf, bom)
        print("✔ 脚手架已装上（模拟按一次 A）")
    back, _, _ = read()
    if back.count(PRESS_NEW) != 1 or "_simPressOnce" not in back:
        print("复验失败")
        sys.exit(1)
    print("✔ 复验通过")
elif mode == "off":
    if PRESS_NEW not in txt and "_simPressOnce" not in txt:
        print("跳过（脚手架已移除）")
    else:
        txt = txt.replace(FIELD_NEW, FIELD_OLD, 1).replace(PRESS_NEW, PRESS_OLD, 1)
        write(txt, crlf, bom)
        print("✔ 脚手架已移除")
    back, _, _ = read()
    if "_simPressOnce" in back or "pressedA" in back:
        print("复验失败：仍有残留")
        sys.exit(1)
    print("✔ 复验通过（已干净）")
else:
    print("用法: toggle_sim_press.py on|off")
    sys.exit(1)
