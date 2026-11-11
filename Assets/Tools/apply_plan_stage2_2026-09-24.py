# -*- coding: utf-8 -*-
"""把阶段二 IHitReceiver 的实测结果回写进方案文档 6.2 末尾（幂等）。"""
import io

DOC = r"E:\Project\BttleFreamwork-v1\战斗框架抽象方案.md"


def read(p):
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        return f.read()


def write(p, s):
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)


raw = read(DOC)
crlf = "\r\n" in raw
t = raw.replace("\r\n", "\n") if crlf else raw

ANCHOR = "### 6.3 阶段三（可选，建议延后）：IHitShape / ITargetSelector"
assert t.count(ANCHOR) == 1, "锚点命中 %d 次" % t.count(ANCHOR)

NEW = """#### 6.2.6 阶段二实测结果（2026-09-24 已落地）

**改动清单**（备份标签 `.bak.2026-09-24-ihit`）

| 文件 | 改动 |
|---|---|
| 新建 `Controller/IHitReceiver.cs` | 9 个既有 Facade 签名 + 新增 `OnHurt` + `string name { get; }` |
| `Controller/CombatUnitController.cs` | 类声明加 `, IHitReceiver`；新增 `OnHurt` Facade（第 584 行） |
| `SkillEffect/Effects/HureEffect.cs` | 直调 `Target.HitReaction.ApplyHurt` → `Target.OnHurt(ctx.Caster)` |
| `SkillEffect/SkillEffectContext.cs` | `Target` 类型由 `CombatUnitController` 收窄为 `IHitReceiver` |

**★ 偏离草案的一处（已说明理由）**：6.2.3 草案的接口里没有 `name`。
T4 落地时 `ImmedDamageEffect.cs:20` 的诊断日志要打印 `ctx.Target.name`，必须给接口补这条成员。
关键在于 **`string name { get; }` 由 `MonoBehaviour.name` 隐式满足**（`MoveBaseController : MonoBehaviour`），
实现类因此零改动 —— 换 `string Name { get; }` 就得手写转发；而在效果类里向下转型回具体类，
会把阶段二刚刚解除的依赖重新装回去，直接违背本阶段初衷。三者取其轻。

**验证（编译器 + Play 双实锤）**

1. **编译器替我们验契约**：`CombatUnitController : MoveBaseController, IHitReceiver` 编译零错误
   ⇒ 9 个既有方法的签名与接口**逐字一致**（含 `params object[]` 与两个默认参数），
   隐式实现成立，实现类零改动。这正是把 T2 单独列一步的意义。
2. 编译 0 Error；`Assembly-CSharp.dll` 由 112640 增至 **113152** 字节（接口 + OnHurt 实体）；
   Editor.log 本次编译段 Warning 与前一版**逐条一致，零新增**。
3. **可观测性契约保持 1:1**：`[SegEffect]` 36 / `[HitEffectCalc]` 36 / `[HitEffect]` **36** —— 三链全齐。
   `[HitReact]` 24 = 12 轮 × 技能 10003 的第 2、3 段各一次效果 22（第 1 段只有效果 0），与 CSV 配置逐条吻合。
4. **`[HitEffectCalc] 技能10003 第1段 → 受击特效 id=10001 目标=GBL(Clone)`** ——
   接口化之后仍然取得到单位名，说明上面那处 `name` 的补充是必要的且生效的。
5. **调用栈证明收口完成**（最有说服力的一条）：
   ```
   UnitHitReaction:ApplyHurt  ←  CombatUnitController:OnHurt  ←  HureEffect:Execute
   ```
   中间那层 `OnHurt` 是本轮新增的 Facade。效果类不再认识 `HitReaction` —— 编排组件退回实现内部，
   效果层只认「我要一次受击表现」。
6. 探针 `Tools/Diag/SkillEffectTargetProbe` **四项全绿**（实验 A/B/C/D）。
7. **Play Error 恒 17**，与阶段一基准完全一致（15 条特效死配置 + 2 条动画协议），零新增。

**留给填桩阶段的纪律**（已写进 `IHitReceiver` 与 `SkillEffectContext` 的类注释）：
效果类**取不到** `Target.HitReaction` / `Target.View` 等内部组件。将来填 1/4/5/14/19/21 这些桩时
需要新语义，**给 IHitReceiver 加 Facade 方法**，不要在效果类里向下转型 —— 本轮 `OnHurt` 就是样板。

### 6.3"""

old_tail = "### 6.3"
if "6.2.6 阶段二实测结果" in t:
    print("已存在，跳过（幂等）")
else:
    idx = t.index(ANCHOR)
    t = t[:idx] + NEW + t[idx + len(old_tail):]
    s = t.replace("\n", "\r\n") if crlf else t
    write(DOC, s)
    print("已回写 6.2.6")

# 复验
t2 = read(DOC).replace("\r\n", "\n")
assert "6.2.6 阶段二实测结果" in t2, "回写失败"
assert t2.count("### 6.3 阶段三（可选，建议延后）") == 1, "6.3 标题异常"
print("复验通过；文档行数:", t2.count("\n") + 1)
