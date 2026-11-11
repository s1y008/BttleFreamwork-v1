# -*- coding: utf-8 -*-
"""2026-09-24 修正 CombatUnitController 里两处过时注释（ApplyStun/ApplySilence 已不再按 BuffType 施加）"""
import io

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts"
path = ROOT + r"\BattleFreamwork\Controller\CombatUnitController.cs"

t = io.open(path, encoding="utf-8", newline="").read()
crlf = "\r\n" in t
text = t.replace("\r\n", "\n")

pairs = [
    (
        "    /// 眩晕 —— 实现体已迁往 UnitHitReaction.ApplyStun（内部转 BuffType.Stunned）。",
        "    /// 眩晕 —— 实现体已迁往 UnitHitReaction.ApplyStun"
        "（★2026-09-24：内部不再转 BuffType.Stunned，改按 CSV 的 Tags=HardControl 反查配置行）。",
    ),
    (
        "    /// 沉默 —— 实现体已迁往 UnitHitReaction.ApplySilence（内部转 BuffType.Silenced）。",
        "    /// 沉默 —— 实现体已迁往 UnitHitReaction.ApplySilence"
        "（★2026-09-24：内部不再转 BuffType.Silenced，改按 CSV 的 Tags=Silence 反查配置行）。",
    ),
]

cnt_old = 0
for old, new in pairs:
    n = text.count(old)
    assert n == 1, "命中 %d 次（应为1）: %s" % (n, old[:50])
    text = text.replace(old, new)
    cnt_old += n

if crlf:
    text = text.replace("\n", "\r\n")
io.open(path, "w", encoding="utf-8", newline="").write(text)

back = io.open(path, encoding="utf-8", newline="").read().replace("\r\n", "\n")
for old, new in pairs:
    assert new in back, "写回复验失败: %s" % new[:50]
    assert old not in back, "旧注释仍残留: %s" % old[:50]
print("OK CombatUnitController.cs — 修正 %d 处过时注释" % cnt_old)
