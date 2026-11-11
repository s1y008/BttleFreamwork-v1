# -*- coding: utf-8 -*-
"""
方案 F 落地（2026-09-27，用户拍板「只做决策 1 + 2，战斗路径绝对 0 GC」）：
  决策 1：SkillEffectContext(class) → SkillCastContext(readonly struct) + in 参数   ⇒ 零 GC
  决策 2：Skill.HitTarget 由 public 收成 private（已扫描确认外部零调用点）
  附带（为达成「绝对 0 GC」，命中路径上其余 4 处分配源一并处理）：
    ③ Skill.cs 三处 Physics.OverlapSphere → OverlapSphereNonAlloc + 共用静态缓冲区
    ④ CombatUnitController.TriggerSegmentHit 的无条件 Debug.Log 收进 #if UNITY_EDITOR（真机也分配）

纪律：
  - 每块 old 命中数必须等于 expected，否则整体中止
  - 幂等：new 串已存在则跳过
  - 写回后重读复验（复验忽略注释行，避免自己写的注释造成误报）
  - 改动前物理备份
"""
import io
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))     # .../Assets
SCRIPTS = os.path.join(ROOT, "Scripts", "BattleFreamwork")
TAG = "skillcastctx"

FILES = {
    "isef":   os.path.join(SCRIPTS, "SkillEffect", "ISkillEffect.cs"),
    "imd":    os.path.join(SCRIPTS, "SkillEffect", "Effects", "ImmedDamageEffect.cs"),
    "addbuf": os.path.join(SCRIPTS, "SkillEffect", "Effects", "AddBuffEffect.cs"),
    "sneer":  os.path.join(SCRIPTS, "SkillEffect", "Effects", "SneerEffect.cs"),
    "hure":   os.path.join(SCRIPTS, "SkillEffect", "Effects", "HureEffect.cs"),
    "skill":  os.path.join(SCRIPTS, "Skill.cs"),
    "ctrl":   os.path.join(SCRIPTS, "Controller", "CombatUnitController.cs"),
    "probe":  os.path.join(ROOT, "Editor", "BuffDiagnostics", "SkillEffectTargetProbe.cs"),
}

BUFF_HDR = (
    "        // ★ 2026-09-27（方案 F）：NonAlloc + 共用静态缓冲区 —— 战斗路径零 GC。\n"
    "        //   ⚠ 必须按返回的命中数遍历，按缓冲区 Length 走会把上一次的陈旧对象当成命中。\n"
)

OVERLAP_HELPER = (
    "    /// <summary>球形索敌缓冲区容量（超出容量的目标会被 Unity **静默丢弃**，见 OverlapTargets）</summary>\n"
    "    private const int TARGET_BUFFER_SIZE = 64;\n"
    "    private static readonly Collider[] s_TargetBuffer = new Collider[TARGET_BUFFER_SIZE];\n"
    "    private static bool s_BufferOverflowWarned;\n"
    "\n"
    "    /// <summary>\n"
    "    /// 球形检测：结果写入共用缓冲区，返回**实际命中数**（&lt;= TARGET_BUFFER_SIZE）。\n"
    "    /// ★ 2026-09-27（方案 F）：战斗路径零 GC —— 不用 Physics.OverlapSphere（它每次 new 一个 Collider[]）。\n"
    "    /// ⚠ 静默丢弃违反工程铁律 5，故容量满时一次性 LogError 指名。此提示只报一次。\n"
    "    /// </summary>\n"
    "    private static int OverlapTargets(Vector3 center, float radius, int layer)\n"
    "    {\n"
    "        int count = Physics.OverlapSphereNonAlloc(center, radius, s_TargetBuffer, layer);\n"
    "        if (count >= TARGET_BUFFER_SIZE && !s_BufferOverflowWarned)\n"
    "        {\n"
    "            s_BufferOverflowWarned = true;\n"
    "            Debug.LogError(\"[Skill] 球形命中缓冲区已满（容量 \" + TARGET_BUFFER_SIZE\n"
    "                + \"，半径 \" + radius + \"）：超出容量的目标会被 Unity **静默丢弃**，\"\n"
    "                + \"表现为某些单位打不到 —— 请调大 TARGET_BUFFER_SIZE。此提示只报一次。\");\n"
    "        }\n"
    "        return count;\n"
    "    }\n"
    "\n"
)

# (key, [(old, new, expected), ...])
PLAN = [
    ("isef", [
        ("    /// <summary>执行一次该效果（ctx 内含攻击者 / 受击者 / 技能 / 配置 / 段号）</summary>\n"
         "    void Execute(SkillEffectContext ctx);",
         "    /// <summary>执行一次该效果</summary>\n"
         "    /// <param name=\"cast\">本次段结算的施法上下文（每段构造一次：攻击者 / 技能 / 配置 / 段号）</param>\n"
         "    /// <param name=\"target\">本次命中的目标（每个目标传一次）</param>\n"
         "    void Execute(in SkillCastContext cast, IHitReceiver target);",
         1),
    ]),

    ("imd", [
        ("    public void Execute(SkillEffectContext ctx)\n"
         "    {\n"
         "        int hitEffectId = ctx.Skill.GetHitEffectIdForSegment(ctx.SegIdx);",
         "    public void Execute(in SkillCastContext cast, IHitReceiver target)\n"
         "    {\n"
         "        int hitEffectId = cast.Skill.GetHitEffectIdForSegment(cast.SegIdx);",
         1),
        ("技能{ctx.BaseData.ID} 第{ctx.SegIdx}段 → 受击特效 id={hitEffectId} 目标={(ctx.Target != null ? ctx.Target.name : \"null\")}",
         "技能{cast.BaseData.ID} 第{cast.SegIdx}段 → 受击特效 id={hitEffectId} 目标={(target != null ? target.name : \"null\")}",
         1),
        ("        if (ctx.Target == null)",
         "        if (target == null)", 1),
        ("        ctx.Target.OnDamaged(ctx.Caster, ctx.Skill.GetSkillOneDamage(), hitEffectId);",
         "        target.OnDamaged(cast.Caster, cast.Skill.GetSkillOneDamage(), hitEffectId);", 1),
    ]),

    ("addbuf", [
        ("/// ★★ 用户 2026-09-24 拍板：作用于 <c>ctx.Target</c>（本次命中的目标），**不是** m_SkillTarget。",
         "/// ★★ 用户 2026-09-24 拍板：作用于 <c>Execute</c> 的 <c>target</c> 参数（本次命中的目标），**不是** m_SkillTarget。",
         1),
        ("    public void Execute(SkillEffectContext ctx)",
         "    public void Execute(in SkillCastContext cast, IHitReceiver target)", 1),
        ("        if (ctx.Target == null)",
         "        if (target == null)", 1),
        ("        for (int i = 0; i < ctx.BaseData.BuffID.Count; i++)",
         "        for (int i = 0; i < cast.BaseData.BuffID.Count; i++)", 1),
        ("            ctx.Target.OnAddBuff(ctx.Caster, ctx.Skill, ctx.BaseData.BuffID[i]);",
         "            target.OnAddBuff(cast.Caster, cast.Skill, cast.BaseData.BuffID[i]);", 1),
    ]),

    ("sneer", [
        ("/// ★ 与 AddBuff 同源，同样已拍板走「顺手修」（见方案 4.6.2）：作用于 <c>ctx.Target</c>（本次命中的目标），",
         "/// ★ 与 AddBuff 同源，同样已拍板走「顺手修」（见方案 4.6.2）：作用于 <c>Execute</c> 的 <c>target</c> 参数（本次命中的目标），",
         1),
        ("    public void Execute(SkillEffectContext ctx)",
         "    public void Execute(in SkillCastContext cast, IHitReceiver target)", 1),
        ("        if (ctx.Target != null)\n"
         "        {\n"
         "            ctx.Target.OnSneer(ctx.Caster);\n"
         "        }",
         "        if (target != null)\n"
         "        {\n"
         "            target.OnSneer(cast.Caster);\n"
         "        }", 1),
    ]),

    ("hure", [
        ("    public void Execute(SkillEffectContext ctx)",
         "    public void Execute(in SkillCastContext cast, IHitReceiver target)", 1),
        ("        if (ctx.Target == null)",
         "        if (target == null)", 1),
        ("        ctx.Target.OnHurt(ctx.Caster);",
         "        target.OnHurt(cast.Caster);", 1),
    ]),

    ("skill", [
        # A1 插入共用缓冲区 + OverlapTargets 辅助
        ("    private const float SEG_TIMEOUT_SECONDS = 3f; // 兜底:3 秒无新事件强制归零(异常保护)\n",
         "    private const float SEG_TIMEOUT_SECONDS = 3f; // 兜底:3 秒无新事件强制归零(异常保护)\n\n" + OVERLAP_HELPER,
         1),
        # A2 决策 2：HitTarget 收 private + 加 cast 参数
        ("    public void HitTarget(CombatUnitController target)",
         "    /// <summary>\n"
         "    /// ★ 2026-09-27 决策 2：由 public 收成 private —— 全工程外部零调用点（scan_refs 已确认），\n"
         "    ///   命中结算的唯一对外入口是 <c>SkillHit(segIdx)</c>，与 v5 P16 的设计意图一致。\n"
         "    /// </summary>\n"
         "    private void HitTarget(CombatUnitController target, in SkillCastContext cast)",
         1),
        # A3 循环内调用点
        ("            ExecuteEffect(effectType,target);",
         "            ExecuteEffect(effectType, target, in cast);", 1),
        # A4 ExecuteEffect 签名
        ("    private void ExecuteEffect(SkillEffectTypes effectType, CombatUnitController target)",
         "    private void ExecuteEffect(SkillEffectTypes effectType, CombatUnitController target, in SkillCastContext cast)",
         1),
        # A5 删掉 new ctx（本方案的直接目标）
        ("        var ctx = new SkillEffectContext\n"
         "        {\n"
         "            Caster   = m_SkillCaseter,\n"
         "            Target   = target,\n"
         "            Skill    = this,\n"
         "            BaseData = BaseData,\n"
         "            SegIdx   = m_SegIdx\n"
         "        };\n"
         "        effect.Execute(ctx);",
         "        // ★ 2026-09-27 方案 F：不再 new 上下文 —— cast 由 SkillHitInternal 每段构造一次传进来。\n"
         "        //   （目标走第二参数；struct 走接口方法不装箱 ⇒ 战斗路径零 GC）\n"
         "        effect.Execute(in cast, target);",
         1),
        # A6 SkillHitInternal 内构造一次 cast
        ("        switch (BaseData. HitType)",
         "        // ★ 2026-09-27（方案 F）：段级施法上下文 —— 本段内恒定不变，全段**只构造这一次**；\n"
         "        //   命中目标变化频率最高，走 Execute 的第二参数，不进本结构。\n"
         "        var cast = new SkillCastContext(m_SkillCaseter, this, BaseData, m_SegIdx);\n"
         "\n"
         "        switch (BaseData. HitType)",
         1),
        # A7 单体分支
        ("                    HitTarget(m_SkillTarget);",
         "                    HitTarget(m_SkillTarget, in cast);", 1),
        # A8 群体分支调用点
        ("                            HitTargetsInCircle(); ",
         "                            HitTargetsInCircle(in cast); ", 1),
        ("                            HitTargetsInFan();",
         "                            HitTargetsInFan(in cast);", 1),
        ("                            HitForwardInEnemy();",
         "                            HitForwardInEnemy(in cast);", 1),
        # A9 三个方法定义加参数
        ("    private void HitTargetsInCircle()",
         "    private void HitTargetsInCircle(in SkillCastContext cast)", 1),
        ("    private void HitTargetsInFan()",
         "    private void HitTargetsInFan(in SkillCastContext cast)", 1),
        ("    private void HitForwardInEnemy()",
         "    private void HitForwardInEnemy(in SkillCastContext cast)", 1),
        # A10 圆形：OverlapSphere → NonAlloc
        ("        Collider[] colliders = Physics.OverlapSphere(m_SkillCaseter.transform.position,BaseData.AttackRadius+ 0.1f, TargetLayerMask);\n"
         "        foreach (Collider c in colliders)\n"
         "        {",
         BUFF_HDR
         + "        int hitCount = OverlapTargets(m_SkillCaseter.transform.position, BaseData.AttackRadius + 0.1f, TargetLayerMask);\n"
           "        for (int i = 0; i < hitCount; i++)\n"
           "        {\n"
           "            Collider c = s_TargetBuffer[i];",
         1),
        # A11 扇形：OverlapSphere → NonAlloc
        ("        Collider[] colliders = Physics.OverlapSphere(m_SkillCaseter.transform.position, BaseData.AttackRadius + 0.1f, TargetLayerMask);\n"
         "        if (colliders.Length<=0)\n"
         "        {\n"
         "            return;\n"
         "        }\n"
         "\n"
         "        foreach (Collider c in colliders)\n"
         "        {",
         BUFF_HDR
         + "        int hitCount = OverlapTargets(m_SkillCaseter.transform.position, BaseData.AttackRadius + 0.1f, TargetLayerMask);\n"
           "        if (hitCount <= 0)\n"
           "        {\n"
           "            return;\n"
           "        }\n"
           "\n"
           "        for (int i = 0; i < hitCount; i++)\n"
           "        {\n"
           "            Collider c = s_TargetBuffer[i];",
         1),
        # A12 正前方：OverlapSphere → NonAlloc
        ("        Collider[] colliders = Physics.OverlapSphere(m_SkillCaseter.transform.position, 20 + 0.1f, TargetLayerMask);\n"
         "        for (int i = 0; i < colliders.Length; i++)",
         BUFF_HDR
         + "        int hitCount = OverlapTargets(m_SkillCaseter.transform.position, 20 + 0.1f, TargetLayerMask);\n"
           "        for (int i = 0; i < hitCount; i++)",
         1),
        ("            CombatUnitController enemy = colliders[i].GetComponent<CombatUnitController>();",
         "            CombatUnitController enemy = s_TargetBuffer[i].GetComponent<CombatUnitController>();", 1),
        # A13/A14 三个 HitTarget 调用点（圆形 605 / 扇形 656 缩进相同，故一次全替换）
        ("HitTarget(unit);",
         "HitTarget(unit, in cast);", 2),
        ("                    HitTarget(enemy);",
         "                    HitTarget(enemy, in cast);", 1),
    ]),

    ("ctrl", [
        ("        Debug.Log($\"[AttackState] skill={skill.SkillId} seg={segIdx} t={(m_View != null ? m_View.StateInfo.normalizedTime : 0f):F3}\");",
         "#if UNITY_EDITOR\n"
         "        // ★ 2026-09-27（方案 F · 零 GC）：Debug.Log 的字符串插值**在真机也会分配**，\n"
         "        //   而本方法是段命中路径的必经入口 ⇒ 收进 UNITY_EDITOR，真机彻底不编译这一段。\n"
         "        Debug.Log($\"[AttackState] skill={skill.SkillId} seg={segIdx} t={(m_View != null ? m_View.StateInfo.normalizedTime : 0f):F3}\");\n"
         "#endif",
         1),
    ]),

    ("probe", [
        ("        var ctx = new SkillEffectContext\n"
         "        {\n"
         "            Caster   = caster,\n"
         "            Target   = target,\n"
         "            Skill    = skill,\n"
         "            BaseData = skill.BaseData,\n"
         "            SegIdx   = 1\n"
         "        };\n"
         "        effect.Execute(ctx);",
         "        var cast = new SkillCastContext(caster, skill, skill.BaseData, 1);\n"
         "        effect.Execute(in cast, target);",
         1),
    ]),
]


CRLF = {}


def read(p):
    """统一按 LF 处理（便于匹配），并记住原文件是否 CRLF"""
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        s = f.read()
    crlf = "\r\n" in s
    CRLF[p] = crlf
    return s.replace("\r\n", "\n")


def write(p, s):
    """按原文件的换行风格写回（Skill.cs 是 CRLF，不能悄悄改成 LF）"""
    if CRLF.get(p, False):
        s = s.replace("\n", "\r\n")
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)


def backup(p):
    bak = p + ".bak.2026-09-27-" + TAG
    if not os.path.exists(bak):
        shutil.copyfile(p, bak)
        print("    [备份] " + os.path.basename(bak))


def code_lines(s):
    """去掉注释行后的文本（复验用，避免自己写的注释造成误报）"""
    out = []
    for ln in s.split("\n"):
        t = ln.strip()
        if t.startswith("//") or t.startswith("*") or t.startswith("///"):
            continue
        out.append(ln)
    return "\n".join(out)


def main():
    # 1) 全部先校验命中数，任一不符立即中止（不写任何文件）
    print("[1/3] 校验所有替换块的命中数 …")
    for key, blocks in PLAN:
        path = FILES[key]
        txt = read(path)
        for i, (old, new, exp) in enumerate(blocks):
            if new in txt:
                continue          # ★ 幂等必须先看 new：插入型块（new 含 old）否则会被重复插入
            n = txt.count(old)
            if n == exp:
                continue
            print("  ✗ %s 第 %d 块：期望命中 %d 次，实际 %d 次" % (key, i + 1, exp, n))
            print("    old 前 80 字符: %r" % old[:80])
            sys.exit(1)
        print("  ✓ %s (%d 块)" % (key, len(blocks)))

    # 2) 备份 + 应用
    print("[2/3] 备份并应用 …")
    for key, blocks in PLAN:
        path = FILES[key]
        txt = read(path)
        changed = False
        for old, new, exp in blocks:
            if new in txt:
                continue                       # 幂等：已应用（含插入型块）
            n = txt.count(old)
            assert n == exp, "%s 命中数在写入时发生变化（并发？）" % key
            if n != 1:
                txt = txt.replace(old, new)     # n == exp 且 exp > 1
            else:
                txt = txt.replace(old, new, 1)
            changed = True
        if changed:
            backup(path)
            write(path, txt)
            print("    已写入 " + os.path.basename(path))

    # 3) 复验
    print("[3/3] 写回复验 …")
    ok = True
    for key, blocks in PLAN:
        path = FILES[key]
        code = code_lines(read(path))
        raw = read(path)
        for old, new, exp in blocks:
            # ★ 插入型块（new 里本来就有 old）不能判「old 已消失」，那是误报
            if old not in new:
                if old in code and not old.strip().startswith("//"):
                    print("  ✗ %s 仍残留 old: %r" % (key, old[:60]))
                    ok = False
            if new not in raw:
                print("  ✗ %s 找不到 new: %r" % (key, new[:60]))
                ok = False

    sk = read(FILES["skill"])
    sk_code = code_lines(sk)
    if "var ctx = new SkillEffectContext" in sk_code:
        print("  ✗ Skill.cs 仍在 new SkillEffectContext")
        ok = False
    if "Physics.OverlapSphere(" in sk_code:
        print("  ✗ Skill.cs 仍存在 Physics.OverlapSphere（应为 NonAlloc）")
        ok = False
    if "void HitTarget(CombatUnitController target, in SkillCastContext cast)" not in sk_code:
        print("  ✗ Skill.cs HitTarget 签名未改")
        ok = False
    if "public void HitTarget" in sk_code:
        print("  ✗ Skill.cs HitTarget 仍是 public")
        ok = False
    if "new SkillCastContext(m_SkillCaseter, this, BaseData, m_SegIdx)" not in sk_code:
        print("  ✗ Skill.cs 未找到 cast 构造点")
        ok = False

    for k in ("imd", "addbuf", "sneer", "hure"):
        if "Execute(in SkillCastContext cast, IHitReceiver target)" not in read(FILES[k]):
            print("  ✗ %s 效果类签名未改" % k)
            ok = False

    if not ok:
        print("复验失败 → 请从 .bak.2026-09-27-%s 还原后重跑" % TAG)
        sys.exit(1)
    print("全部复验通过 ✓")


if __name__ == "__main__":
    main()
