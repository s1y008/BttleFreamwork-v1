# -*- coding: utf-8 -*-
"""
修正上一版诊断日志：
- SkillData 的技能主键是 ID（大写），不是 Id；
- IHitReceiver 已内置 name 成员（CombatUnitController 继承 MonoBehaviour 隐式满足），
  无需 target as UnityEngine.Object 转换。
"""
import io, os, sys

ROOT = r"E:\Project\BttleFreamwork-v1\Assets"
F1 = os.path.join(ROOT, "Scripts", "BattleFreamwork", "SkillEffect", "Effects", "AddBuffEffect.cs")

ok = True


def read(path):
    raw = io.open(path, "rb").read()
    return (raw.decode("utf-8-sig" if raw[:3] == b"\xef\xbb\xbf" else "utf-8").replace("\r\n", "\n"),
            b"\r\n" in raw, raw[:3] == b"\xef\xbb\xbf")


def write(path, txt, crlf, bom):
    out = txt.replace("\n", "\r\n") if crlf else txt
    data = out.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    io.open(path, "wb").write(data)


OLD = """            Debug.Log(string.Format(
                "[AddBuff] 技能{0} 第{1}段 → 目标={2} buffId={3}",
                cast.BaseData == null ? 0 : cast.BaseData.Id,
                cast.SegIdx,
                (target as UnityEngine.Object) == null ? "(非Unity对象)" : (target as UnityEngine.Object).name,
                cast.BaseData.BuffID[i]));"""

NEW = """            Debug.Log(string.Format(
                "[AddBuff] 技能{0} 第{1}段 → 目标={2} buffId={3}",
                cast.BaseData == null ? 0 : cast.BaseData.ID,
                cast.SegIdx,
                target.name,
                cast.BaseData.BuffID[i]));"""

txt, crlf, bom = read(F1)
if NEW in txt:
    print("跳过：已是修正版（幂等）")
else:
    n = txt.count(OLD)
    if n != 1:
        print("失败：old 命中 %d 次（期望 1）" % n)
        sys.exit(1)
    txt = txt.replace(OLD, NEW, 1)
    write(F1, txt, crlf, bom)
    back, _, _ = read(F1)
    if back.count(NEW) != 1 or "BaseData.Id" in back or "as UnityEngine.Object" in back:
        print("复验失败")
        sys.exit(1)
    print("✔ 修正并写回成功")

# 顺便确认 UnitBuffComponent 里的成员名都存在
F2 = os.path.join(ROOT, "Scripts", "BattleFreamwork", "Controller", "UnitBuffComponent.cs")
t2, _, _ = read(F2)
print()
print("=== UnitBuffComponent 成员可解析性粗检 ===")
for m in ("tickBuff.StackCount", "tickBuff._BuffData.EffectValue", "tickBuff._BuffData.Id",
          "tickBuff.CasterId", "FindControllerById", "m_Owner.name"):
    print("  %-34s %s" % (m, "OK" if m in t2 else "!! 不在本文件（可能编译失败）"))
