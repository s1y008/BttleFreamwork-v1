# -*- coding: utf-8 -*-
"""
阶段二 IHitReceiver 提取（2026-09-24）
T2: CombatUnitController 实现接口 + 新增 OnHurt Facade
T3: HureEffect 从直调 Target.HitReaction 收口到 ctx.Target.OnHurt
T4: SkillEffectContext.Target 类型改为 IHitReceiver
"""
import io, os

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork"
FILES = {
    "ctl": os.path.join(ROOT, "Controller", "CombatUnitController.cs"),
    "hure": os.path.join(ROOT, "SkillEffect", "Effects", "HureEffect.cs"),
    "ctx": os.path.join(ROOT, "SkillEffect", "SkillEffectContext.cs"),
}


def read(p):
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        return f.read()


def write(p, s):
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)


def rep(text, old, new, tag):
    cnt = text.count(old)
    assert cnt == 1, "[%s] 命中 %d 次，期望 1\n--- old ---\n%s" % (tag, cnt, old)
    return text.replace(old, new)


raw, crlf = {}, {}
for k, p in FILES.items():
    t = read(p)
    crlf[k] = "\r\n" in t
    raw[k] = t.replace("\r\n", "\n") if crlf[k] else t
print("换行符：", {k: ("CRLF" if v else "LF") for k, v in crlf.items()})

# ══════════ T2-a：类声明加接口 ══════════
t = raw["ctl"]
t = rep(t,
"public class CombatUnitController : MoveBaseController\n",
"""public class CombatUnitController : MoveBaseController, IHitReceiver
""",
    "ctl:类声明")
raw["ctl"] = t

# ══════════ T2-b：新增 OnHurt Facade ══════════
t = raw["ctl"]
t = rep(t,
"""    // ★ v5 P13 删除 OnHurt(List<int>, ...) —— 方法体为空且全工程零引用（scan_refs.py 全量扫描确认）。
    //   它的注释本来说的是「显示伤害特效以及普通受击动画」，而真正的特效出口早已收敛到
    //   UnitView.PlayEffect；留着只会误导后来者以为这里是受击动画的开关。
""",
"""    // ★ v5 P13 删除 OnHurt(List<int>, ...) —— 方法体为空且全工程零引用（scan_refs.py 全量扫描确认）。
    //   它的注释本来说的是「显示伤害特效以及普通受击动画」，而真正的特效出口早已收敛到
    //   UnitView.PlayEffect；留着只会误导后来者以为这里是受击动画的开关。

    /// <summary>
    /// 受击表现（顿帧 / 普通受击动画）—— 实现体在 <c>UnitHitReaction.ApplyHurt</c>。
    ///
    /// ★ 阶段二新增（2026-09-24）：原本 <c>HureEffect</c> 直接写
    ///   <c>target.HitReaction.ApplyHurt(...)</c>，等于让效果层认识本单位内部的编排组件。
    ///   收口到本 Facade 之后，效果层只依赖 <c>IHitReceiver</c> 契约，
    ///   将来换单位类型不必动效果类（契约纪律见 IHitReceiver 类注释）。
    ///
    /// ⚠ 命名提示：上面那条注释说的是 v5 P13 删掉的空重载 <c>OnHurt(List&lt;int&gt;, ...)</c>，
    ///   与本方法**不是同一样东西**（签名完全不同），别看到同名就以为在走回头路。
    /// </summary>
    /// <param name="attacker">攻击者（内部用于朝向 / 仇恨）</param>
    public virtual void OnHurt(CombatUnitController attacker)
    {
        if (m_HitReaction == null)
        {
            return;
        }
        m_HitReaction.ApplyHurt(attacker);
    }
""",
    "ctl:新增 OnHurt")
raw["ctl"] = t

# ══════════ T3：HureEffect 收口 ══════════
t = raw["hure"]
t = rep(t,
"""        if (ctx.Target == null || ctx.Target.HitReaction == null)
        {
            return;
        }

        ctx.Target.HitReaction.ApplyHurt(ctx.Caster);
""",
"""        // ★ 阶段二（2026-09-24）：不再直访 Target.HitReaction —— 那是实现方的内部编排组件，
        //   没法写进 IHitReceiver 契约。改走新增的 Facade OnHurt，语义等价（判空已下沉到实现内部）。
        if (ctx.Target == null)
        {
            return;
        }

        ctx.Target.OnHurt(ctx.Caster);
""",
    "hure:收口 OnHurt")
raw["hure"] = t

# ══════════ T4：Target 类型改为接口 ══════════
t = raw["ctx"]
t = rep(t,
"""    /// <summary>本次命中的目标（= 改造前 ExecuteEffect 的 target 参数）。所有效果一律用它</summary>
    public CombatUnitController Target;
""",
"""    /// <summary>
    /// 本次命中的目标（= 改造前 ExecuteEffect 的 target 参数）。所有效果一律用它。
    ///
    /// ★ 阶段二（2026-09-24）：类型由 <c>CombatUnitController</c> 收窄为 <c>IHitReceiver</c>
    ///   —— 效果层从此只认「能被这些东西打」的契约，不再依赖具体单位类；
    ///   将来出现第二种受击单位（霸体怪 / 建筑 / 宠物）时效果类一行都不用改。
    ///   ⚠ 因此效果类里**取不到** <c>Target.HitReaction</c> / <c>Target.View</c> 等内部组件，
    ///     需要新语义请给 IHitReceiver 加 Facade 方法，不要在效果类里向下转型。
    ///     本轮即为这条纪律新增了 <c>OnHurt</c>（原 HureEffect 直调组件的那一处已收口）。
    /// </summary>
    public IHitReceiver Target;
""",
    "ctx:Target 接口化")
raw["ctx"] = t

# ══════════ 写回 ══════════
for k, p in FILES.items():
    s = raw[k]
    if crlf[k]:
        s = s.replace("\n", "\r\n")
    write(p, s)

# ══════════ 从磁盘重读复验 ══════════
print("\n=== 复验 ===")
ctl = read(FILES["ctl"]).replace("\r\n", "\n")
assert "MoveBaseController, IHitReceiver" in ctl, "ctl: 接口未挂上"
assert ctl.count("public virtual void OnHurt(CombatUnitController attacker)") == 1, "ctl: OnHurt 数量异常"
assert "m_HitReaction.ApplyHurt(attacker);" in ctl, "ctl: OnHurt 实现体缺失"
print("ctl: 接口 + OnHurt ✓")

hure = read(FILES["hure"]).replace("\r\n", "\n")
assert "HitReaction" not in hure.split("/// <summary>")[0] or True  # 占位，下面严格查
body = "\n".join(l for l in hure.split("\n") if not l.strip().startswith("//") and not l.strip().startswith("///"))
assert "HitReaction" not in body, "hure: 非注释行仍出现 HitReaction"
assert "ctx.Target.OnHurt(ctx.Caster);" in hure, "hure: 未改为 OnHurt"
print("hure: 直访 HitReaction 已清除 ✓")

ctx = read(FILES["ctx"]).replace("\r\n", "\n")
assert "public IHitReceiver Target;" in ctx, "ctx: Target 未改为接口"
assert "public CombatUnitController Target;" not in ctx, "ctx: 旧声明残留"
print("ctx: Target 已是 IHitReceiver ✓")

print("\n全部断言通过")
