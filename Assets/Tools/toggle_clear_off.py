# -*- coding: utf-8 -*-
"""
【临时 A/B 对照脚手架，验证完必须还原】toggle_clear_off.py on|off

on  = 停掉 WarrorController 里的清空（模拟「修复前」），用于证明无限连击确实是
      m_CurSelectedSkill 未清空导致的，而不是「本来就不会连击」。
off = 还原清空（修复后）。
"""
import io, sys

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Controller\WarrorController.cs"


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


ON = "        // [AB-TEST] m_CurSelectedSkill = null;"
OFF = "        m_CurSelectedSkill = null;"

mode = sys.argv[1] if len(sys.argv) > 1 else "off"
txt, crlf, bom = read()

if mode == "on":
    if ON in txt:
        print("跳过（已停用清空）")
    else:
        n = txt.count(OFF)
        if n != 1:
            print("失败：清空语句命中 %d 次（期望 1）" % n)
            sys.exit(1)
        txt = txt.replace(OFF, ON, 1)
        write(txt, crlf, bom)
        print("✔ 已停用清空（= 修复前状态）")
else:
    if OFF in txt and ON not in txt:
        print("跳过（已是清空状态）")
    else:
        n = txt.count(ON)
        if n != 1:
            print("失败：停用行命中 %d 次（期望 1）" % n)
            sys.exit(1)
        txt = txt.replace(ON, OFF, 1)
        write(txt, crlf, bom)
        print("✔ 已还原清空（= 修复后状态）")

back, _, _ = read()
print("  当前状态:", "停用(A组)" if ON in back else "启用(B组)")
