# -*- coding: utf-8 -*-
"""
临时切换 WarrorController 里玩家自动释放的技能 id（用于验证指定技能的战斗链路）。

用法: python set_debug_skill.py <skillId>
例:   python set_debug_skill.py 10002   # 临时改为放天诛（验证流血）
      python set_debug_skill.py 10003   # 改回原值

铁律：幂等判据为「目标串已存在即跳过」；old 命中数必须 == 1；写回复验。
"""
import io, os, re, sys

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Controller\WarrorController.cs"
# ⚠ 不要用写死的 OLD 常量：从 10002 切回 10003 时 old 命中 0 会误报失败。
#    改为正则匹配任意 GetSkill(数字)，支持任意源 → 任意目标的双向切换。
PAT = re.compile(r"CurWarrior\.GetSkill\((\d+)\)")


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


if len(sys.argv) < 2:
    print("缺少参数：请传入技能 id")
    sys.exit(1)

sid = sys.argv[1].strip()

txt, crlf, bom = read()

NEW = "CurWarrior.GetSkill(%s)" % sid

m = PAT.search(txt)
if m:
    cur = m.group(1)
    if cur == sid:
        print("跳过（已是 %s，幂等）" % sid)
        sys.exit(0)
else:
    print("失败：文件中找不到 CurWarrior.GetSkill(...) 调用")
    sys.exit(1)

n = len(PAT.findall(txt))
if n != 1:
    print("失败：CurWarrior.GetSkill(...) 命中 %d 处（期望 1）—— 请人工确认" % n)
    sys.exit(1)

print("改写前 = CurWarrior.GetSkill(%s)" % cur)
txt = PAT.sub(lambda _: NEW, txt, count=1)
write(txt, crlf, bom)

back, _, _ = read()
m2 = PAT.search(back)
if not m2 or m2.group(1) != sid or back.count(NEW) != 1:
    print("复验失败：目标串出现次数异常")
    sys.exit(1)
print("✔ 已改为 GetSkill(%s) 并通过复验" % sid)
for o in [l.strip() for l in back.split("\n") if "CurWarrior.GetSkill(" in l]:
    print("   → " + o)
