# -*- coding: utf-8 -*-
"""
追加 10.11 节到 战斗框架抽象方案.md
纪律：幂等（已在则跳过）、保持 CRLF/LF、写回复验
"""
import io, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))   # = 工程根（.../BttleFreamwork-v1）
TARGET = os.path.join(ROOT, "战斗框架抽象方案.md")
MARK = "### 10.11"

CONTENT = """
### 10.11 【已执行】构造进一步简化：只传 Skill（2026-09-28）

**用户提出**：构造 `SkillCastContext` 时没必要传那么多参数 —— `BaseData`、`SegIdx`（经属性访问器）、
`m_SkillCaseter`（经 `SkillCaseter` 访问器）**本就在 Skill 自己身上**，直接把 Skill 传过去即可。

**结论：采纳，但 `SegIdx` 例外**（见下方 ⚠）。三者危害性不同：

| 成员 | 改成什么 | 依据 |
|---|---|---|
| `Caster` | **转发属性** `Skill.SkillCaseter` | `m_SkillCaseter` 由 `SetSkillCaster` 在起手时写入，结算期间无写入口 ⇒ 转发安全 |
| `BaseData` | **转发属性** `Skill.BaseData` | `m_SkillBaseData` 在 Skill 构造（131 行）时一次性赋值，此后恒定 ⇒ 转发安全 |
| `SegIdx` | **保留字段（构造时取快照）** | `SkillHit(segIdx)` 每次进来都会覆盖它 —— 见下方 ⚠ |

⚠ **为什么 `SegIdx` 不写成转发**：`SegIdx` 是 Skill 上的**可变**字段，`SkillHit(segIdx)` → `SegIdx = segIdx`
→ `SkillHitInternal()` 每次都会覆盖。本结构生命周期 = `SkillHitInternal()` 的同步调用栈；一旦结算期间发生
**重入**（某效果内部又触发一次命中、或将来 Buff Tick 反击），转发读到的就是内层改过的段号，而外层剩下的
效果会拿错段号去取效果列表 —— 只在特定技能组合下偶现，**且不报任何错**，正是铁律 5 禁止的静默降级。
代价仅一个 int 字段（4 字节栈上，`in` 传参本就不拷贝），换一整类隐蔽 bug 的免疫。

**收益（除消除重复外，更重要的是消除一类错误）**：原 4 参数由调用方自由组合，理论上能造出
「Skill 是 A、但 SegIdx 填了 B 的段号」这种**自相矛盾**的上下文；现在真相源收敛到一个 —— Skill 自身。

**改动清单（3 个文件）**：

| # | 文件 | 动作 |
|---|---|---|
| 1 | `SkillEffect/SkillCastContext.cs` | 删 `Caster`/`BaseData` 两个只读字段，改为转发属性；构造函数 `(caster, skill, baseData, segIdx)` → **`(Skill skill)`**，内部 `SegIdx = skill.SegIdx;` |
| 2 | `Skill.cs` 构造点 | `new SkillCastContext(m_SkillCaseter, this, BaseData, m_SegIdx)` → **`new SkillCastContext(this)`** |
| 3 | `Editor/BuffDiagnostics/SkillEffectTargetProbe.cs` | 同上。⚠ 原调用显式传了段号 `1`，而新建的 Skill 实例 `SegIdx` 是 0 ⇒ 改前先 `SetField(skill, "m_SegIdx", 1)` 保持等价（**顺序敏感**：上下文在构造瞬间取快照） |

**4 个效果类零改动** —— 它们用的是 `cast.Caster` / `cast.BaseData` / `cast.Skill` / `cast.SegIdx`，
这四个成员的名字与类型一字未变（前两个从「字段」变成了「属性」，但 C# 成员访问语法相同）。

**验证实锤**（stop → capture → play）：

| 指标 | 结果 |
|---|---|
| 编译 | **0 Error**（增量比对法：前后 `error CS` 均为 13 条，无新增）；dll mtime 09:53 > 源 09:51 |
| Error 总数 | **17**（与基准逐类一致） |
| `[SegEffect]` / `[HitEffectCalc]` / `[HitEffect]` | **27 / 27 / 27**，保持 1:1 |
| `[CastCtx]` | **27 == `[SegEffect]` 27** ⇒ 每段仍恰好构造一次 |
| **★ 交叉验证：快照段号正确性** | `[HitEffectCalc]` 的段号来源正是字段 `cast.SegIdx`，与 `[SegEffect]`（Skill 自己读 `m_SegIdx`）**27 条逐条全等** ⇒ 快照取值正确，未取到错段号 |
| 缓冲区溢出告警 | 0 |

⚠⚠ **本轮踩坑（同类第 4 次，写进 MEMORY 环境坑第 1 条）**：用 Edit 工具改 `Skill.cs`（29 KB 大文件）
后，`grep` 复核显示已生效，但**几分钟后磁盘内容被静默回退成旧版本**，表现为 Unity 持续报
`CS1729: SkillCastContext 不含 4 参数构造函数`。最终改用 **Python 脚本**（`Tools/apply_castsimplify_2026-09-28.py`，
带命中数断言 + 写回复验 + 幂等）才稳定落盘。结论：**改 `.cs` 大文件一律走脚本，不要用 Edit**。

另注：`SkillData.cs(593)` 的 `CS1002`/`CS1003` 在 `Editor.log` 中是**历史残留**（该文件已被改好），
判当前是否新增编译错误必须用「前后 `error CS` 计数比对」，不能看总数。
"""


def main():
    if not os.path.exists(TARGET):
        print("✗ 目标不存在: %s" % TARGET)
        return 1
    with io.open(TARGET, "r", encoding="utf-8", newline="") as f:
        raw = f.read()
    crlf = "\r\n" in raw
    text = raw.replace("\r\n", "\n")

    print("目标: %s  换行: %s" % (TARGET, "CRLF" if crlf else "LF"))

    if MARK in text:
        print("→ 10.11 已存在，跳过（幂等）")
        return 0

    add = CONTENT.replace("\n", "\r\n") if crlf else CONTENT
    if not text.endswith("\n"):
        add = ("\r\n" if crlf else "\n") + add

    with io.open(TARGET, "a", encoding="utf-8", newline="") as f:
        f.write(add)

    # 复验
    with io.open(TARGET, "r", encoding="utf-8", newline="") as f:
        back = f.read().replace("\r\n", "\n")
    ok = MARK in back and "10.11. " not in back
    print("✓ 复验：10.11 已写入" if ok else "✗ 复验失败")
    print("✓ 写入前后行号：%.0f → %.0f" % (text.count("\n"), back.count("\n")))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
