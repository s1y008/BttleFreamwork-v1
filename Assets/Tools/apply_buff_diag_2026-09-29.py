# -*- coding: utf-8 -*-
"""
给 Buff 链路补 #if UNITY_EDITOR 诊断日志（AddBuff 命中 / DoT 跳伤），便于实锤验证。

设计约束（本项目铁律）：
- 改 .cs 一律走脚本：每块 old 命中数 == 1 断言；幂等判据为「new 已存在即跳过」；
- 写回后重读复验；CRLF 保真。
"""
import io, os, sys

ROOT = r"E:\Project\BttleFreamwork-v1\Assets"

F1 = os.path.join(ROOT, "Scripts", "BattleFreamwork", "SkillEffect", "Effects", "AddBuffEffect.cs")
F2 = os.path.join(ROOT, "Scripts", "BattleFreamwork", "Controller", "UnitBuffComponent.cs")

ok = True


def read(path):
    raw = io.open(path, "rb").read()
    crlf = b"\r\n" in raw
    txt = raw.decode("utf-8-sig" if raw[:3] == b"\xef\xbb\xbf" else "utf-8")
    return txt.replace("\r\n", "\n"), crlf, (raw[:3] == b"\xef\xbb\xbf")


def write(path, txt, crlf, bom):
    out = txt.replace("\n", "\r\n") if crlf else txt
    data = out.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    io.open(path, "wb").write(data)


def apply(path, blocks, label):
    """blocks: list of (old, new, desc)"""
    global ok
    txt, crlf, bom = read(path)
    changed = False
    print("===== %s =====" % label)
    for old, new, desc in blocks:
        n_old = txt.count(old)
        n_new = txt.count(new)
        if n_new >= 1:
            print("  - 跳过（已存在）: %s" % desc)
            continue
        if n_old != 1:
            print("  ! 失败：old 命中 %d 次（期望 1）: %s" % (n_old, desc))
            ok = False
            continue
        txt = txt.replace(old, new, 1)
        changed = True
        print("  + 已替换: %s" % desc)
    if changed:
        write(path, txt, crlf, bom)
        # 写回复验
        back, _, _ = read(path)
        for old, new, desc in blocks:
            if new not in back:
                print("  ! 复验失败：%s 未落盘" % desc)
                ok = False
            elif back.count(new) != 1:
                print("  ! 复验失败：%s 出现 %d 次（期望 1）" % (desc, back.count(new)))
                ok = False
        print("  ✔ 已写回并通过复验")
    else:
        print("  - 无改动（全部幂等跳过）")
    print()


# ---------- 文件 1：AddBuffEffect ----------
OLD1 = """        // BuffID 为配置表 Id 列表：逐个经 OnAddBuff 查 CSV 并挂到目标的 Buff 系统
        // （OnAddBuff 内部：BuffSystem.AddBuff(buffId, attacker.GetInstanceID())）
        for (int i = 0; i < cast.BaseData.BuffID.Count; i++)
        {
            target.OnAddBuff(cast.Caster, cast.Skill, cast.BaseData.BuffID[i]);
        }"""

NEW1 = """        // BuffID 为配置表 Id 列表：逐个经 OnAddBuff 查表并挂到目标的 Buff 系统
        // （OnAddBuff 内部：BuffSystem.AddBuff(buffId, attacker.GetInstanceID())）
        for (int i = 0; i < cast.BaseData.BuffID.Count; i++)
        {
            target.OnAddBuff(cast.Caster, cast.Skill, cast.BaseData.BuffID[i]);

#if UNITY_EDITOR
            // ★ 诊断：AddBuff 成功路径原本静默无痕（只有失败才 LogError），
            //   导致「配了效果 2 但没挂上」与「挂上了但看不见」无法区分。
            //   与 [HitEffectCalc] 同风格：仅 Editor 可见，真机零开销。
            Debug.Log(string.Format(
                "[AddBuff] 技能{0} 第{1}段 → 目标={2} buffId={3}",
                cast.BaseData == null ? 0 : cast.BaseData.Id,
                cast.SegIdx,
                (target as UnityEngine.Object) == null ? "(非Unity对象)" : (target as UnityEngine.Object).name,
                cast.BaseData.BuffID[i]));
#endif
        }"""

OLD1_NOTE = """/// ⚠ 若 <c>BaseData.BuffID</c> 为空（当前 CSV 里配了效果 2 的技能该列**全为空**），
///   本效果不产生任何作用 —— 属配置问题，不是代码问题。"""

NEW1_NOTE = """/// ⚠ 若 <c>BaseData.BuffID</c> 为空，本效果不产生任何作用 —— 属配置问题，不是代码问题。
///   2026-09-29 起：10002「天诛」已配 BuffID=[1003]（流血）；其余配了效果 2 的技能仍为空，
///   由 SkillDataManager.ValidateBuffBindings 在加载期 LogError 指名（不再静默失效）。"""

apply(F1, [(OLD1_NOTE, NEW1_NOTE, "AddBuffEffect 头部注释更新"),
           (OLD1, NEW1, "AddBuffEffect 诊断日志")], "AddBuffEffect.cs")

# ---------- 文件 2：UnitBuffComponent DoT ----------
OLD2 = """        int damage = (int)(tickBuff._BuffData.EffectValue * tickBuff.StackCount);
        if (damage <= 0)
        {
            return;
        }"""

NEW2 = """        int damage = (int)(tickBuff._BuffData.EffectValue * tickBuff.StackCount);
        if (damage <= 0)
        {
            return;
        }

#if UNITY_EDITOR
        // ★ 诊断：DoT 跳伤此前完全无痕 —— Buff 是否真的在 Tick、叠了几层、跳出多少伤害都看不见。
        Debug.Log(string.Format(
            "[BuffTick] {0} buffId={1} 层数={2} 单跳={3} → 本次伤害={4} 施加者存在={5}",
            m_Owner == null ? "(null)" : m_Owner.name,
            tickBuff._BuffData == null ? 0 : tickBuff._BuffData.Id,
            tickBuff.StackCount,
            tickBuff._BuffData == null ? 0 : tickBuff._BuffData.EffectValue,
            damage,
            FindControllerById(tickBuff.CasterId) != null));
#endif"""

apply(F2, [(OLD2, NEW2, "UnitBuffComponent DoT 诊断日志")], "UnitBuffComponent.cs")

print("=== 总结 ===")
print("全部成功" if ok else "存在失败项，请检查上面标记 ! 的行")
sys.exit(0 if ok else 1)
