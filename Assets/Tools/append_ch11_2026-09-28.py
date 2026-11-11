# -*- coding: utf-8 -*-
"""把「状态同步适配」追加为方案文档第 11 章。幂等 + 保留原换行符。"""
import io
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # = Assets
DOC = os.path.join(os.path.dirname(ROOT), "战斗框架抽象方案.md")      # 工程根

MARK = "## 11. 【阶段五】状态同步适配"

CHAPTER = """
## 11. 【阶段五】状态同步适配 —— 2026-09-28 启动

> 用户问题：「接入状态同步要改哪些数据、怎么改造、框架当初是否想到了这一点」，
> 以及「能否用一个参数控制是否走网络部分」。

### 11.1 先回答：当初完全没考虑联网

全工程 364 个源码文件 grep `network|netcode|photon|mirror|lockstep|帧同步|状态同步|rollback`
—— **0 条命中**。所有设计都建立在「本机唯一权威」这个隐含前提上。

但框架的分层（View / HitReaction / Controller）比多数单机框架干净，
效果层也已完成策略化与上下文收口 ⇒ 问题不在「结构混乱」，而在**方向反了**：
现在是**动画驱动逻辑**，而状态同步要求**逻辑驱动动画**。

### 11.2 状态同步 vs 帧同步（改造量天差地别）

| 方案 | 要求 | 本框架成本 |
|---|---|---|
| **状态同步**（服务器权威，同步结果） | 逻辑能脱离 Unity 跑 + 状态可序列化 + 表现为逻辑的下游 | **可控**，即本章清单 |
| 帧同步（各端跑同逻辑，只同步输入） | 严格确定性 + 定点数 + 剔除物理与动画 | `float` 遍布伤害/时机/坐标 ⇒ **基本等于重写数值层** |

⇒ 选型必须早定，别等做完再换。

### 11.3 S 级改造清单（不做就连不上）

| # | 问题 | 位置 | 怎么改 |
|---|---|---|---|
| **S1** | **段号推进由动画驱动** | `CombatStates.cs:262` | 引入 `ICastClock`，进度改由可替换时钟提供（**已完成，见 11.5**） |
| **S2** | 身份用 `GetInstanceID()` | `CombatUnitController.cs:661`、`UnitBuffComponent.cs:46`、`CommonHelper.cs:209` | 换 netId + `netId→Controller` 映射。⚠ 叠加既有 bug `new BuffSystemPooled(0,...)`（ownerId 恒 0） |
| **S3** | 随机数用 `UnityEngine.Random` | `CommonHelper.cs:151/163/575` | 换带 seed 的确定性随机，seed 由服务器下发 |
| **S4** | 权威数值挂在 MonoBehaviour | `CurCombatUnit`（HP / IsDead / CantAttack） | 抽纯数据 `BattleUnitState`，可序列化、可快照 |

**A 级**：输入命令化（`MoveBaseController.cs:44`、`WarrorController.cs:123`）、
`Time.deltaTime` → 固定 tick、`Physics.OverlapSphere` → 逻辑半径判定、
`NavMeshAgent`/`CharacterController` → 简化逻辑移动、
`AttackNpcController.cs:53-65` 的 `StartCoroutine(Skin())` → 纯计时器。

### 11.4 双模开关的设计：**单机 = 本地服务器**

> 用户问「能否用一个参数控制是否走网络」。答案是能，但**开关放哪里决定成败**。

| 档 | 做法 | 判定 |
|---|---|---|
| 1 | 逻辑内部散落 `if (isNetwork)` | ❌ 灾难：伤害结算/段号推进写两套，单机路径必腐烂 |
| 2 | 入口顶层分流成两套流程 | ❌ 仍是双份流程 |
| 3 | **依赖注入 + 逻辑单一** | ✅ 开关只决定「绑定哪个实现」 |

**核心设计：单机模式不是「绕过网络层」，而是「跑一个进程内的本地服务器」。**
逻辑永远只有一份，单机天天在测的就是联网要跑的那份。
参数控制的不是「是否启用网络」，而是「传输层用 InProcess 还是 Socket」。

四个可替换边界（逻辑核心内部**不得出现任何模式判断**）：

| 边界 | 单机实现 | 联网实现 |
|---|---|---|
| 时钟源 `ICastClock` | `AnimatorCastClock` | `LogicCastClock` |
| 身份源 | `GetInstanceID` | netId |
| 随机源 | `UnityEngine.Random` | seeded |
| 输出汇 | 直接播表现 | 下发快照 + 插值 |

⚠ 开关只在**启动时绑定一次**，不支持运行期热切换（半途切换会让状态机与网络序号同时错位）。
建议两层：编译期符号（剔除未用分支）+ 启动期配置（选实现）。

### 11.5 ★ S1 已落地：施法时钟 `ICastClock`

**新增** `Assets/Scripts/BattleFreamwork/Net/CastClock.cs`：

```csharp
public interface ICastClock
{
    void Reset();                 // 一轮施法开始（AttackState.OnEnter）
    void Tick(float deltaTime);   // 每帧推进（AnimatorCastClock 为空实现）
    float GetProgress();          // 归一化 0~1 进度
    bool IsCasting();             // 语义等价于原 !Owner.View.IsInIdleState
}
```

**关键发现：全工程只有三处逻辑读 Animator，且可收敛为一个概念「施法进度」：**

| 依赖点 | 改造前 | 改造后 |
|---|---|---|
| 段/特效推进 | `info.normalizedTime` | `_clock.GetProgress()` |
| 起手守卫 `TryEnterPlaying` | `!Owner.View.IsInIdleState` | `_clock.IsCasting()` |
| 结束判定 `TryFinishSkill` | `!Owner.View.IsInIdleState` | `_clock.IsCasting()` |

**★ 幸运点**：`SegTimings`（CSV 列 23）本就是**归一化 0~1 的纯数据时间轴**，
且 `SegCount`（玩法数值）与 `SegTimings`（表现参数）当初已刻意分离
⇒ 换成逻辑时钟后**不需要改动任何一行 CSV**。`CastEffectTimings`（列 25）同理。

**阻塞项已排除**：`_phase` 的推进**不依赖 Unity AnimationEvent**
（全工程 grep `AnimationEvent` 零命中），只有 4 处内部赋值。

**等价性保证**：`AnimatorCastClock` 与改造前逐行等价，
native 调用次数也不变（改造前每帧 3 次：StateInfo 1 + IsInIdleState 2；改造后仍 3 次）。
注入方式 `new AttackState(this, clock)`，默认 `null` 时自动用 `AnimatorCastClock` ⇒ **单机行为零变化**。

### 11.6 实测（stop → 捕获 → play）

| 指标 | 结果 |
|---|---|
| 编译 | **0 新增错误**（errorCS 13 → 13 增量比对） |
| dll | 118272 → **119296**，mtime 晚于源 |
| Error 总数 | **17**（与基准逐类一致） |
| `[SegEffect]` / `[HitEffectCalc]` / `[HitEffect]` | **30 / 30 / 30** 保持 1:1 |
| `[CastCtx]` | **30 == `[SegEffect]` 30** |
| 逐条交叉验证 | `[SegEffect]` 与 `[HitEffectCalc]` 的（技能, 段号）**30 条全等** |

⇒ 改造后**单机行为与改造前完全一致**，S1 目标达成。

### 11.7 后续

1. **S2 身份源** —— netId 由谁分配、如何与 `GetInstanceID` 共存，需先定；
2. **S3 随机源** —— 纯接口注入，风险最低；
3. 引入真正的网络层时，它只是第四个实现类，**不动任何战斗逻辑**。

⚠ **遗留待查**：`LogicCastClock` 需要「技能总时长 `duration`」，
当前 CSV **没有这一列**，需从动画长度取常量或新增列（**属用户手工改配置范畴，AI 不代劳**）。
另：`ani_IdleState="Idle"` 与敌人 GBL 状态名 `idle` 不一致的既有问题仍未修，
会导致敌人 `IsInIdleState` 恒 false（进而 `TryFinishSkill` 永不自动结束）。

**回滚**：`CombatStates.cs.bak.2026-09-28-castclock`；改造脚本 `Tools/apply_castclock_2026-09-28.py`。
"""

MARKER_END = "11.7 后续"


def main():
    if not os.path.exists(DOC):
        print("✗ 文档不存在: %s" % DOC)
        return 1
    with io.open(DOC, "r", encoding="utf-8", newline="") as f:
        raw = f.read()
    crlf = raw.count("\r\n") > 0
    txt = raw.replace("\r\n", "\n")

    if MARK in txt:
        print("· 第 11 章已存在，跳过（幂等）")
        return 0

    if not txt.endswith("\n"):
        txt += "\n"
    txt += CHAPTER

    out = txt.replace("\n", "\r\n") if crlf else txt
    with io.open(DOC, "w", encoding="utf-8", newline="") as f:
        f.write(out)

    # 复验
    with io.open(DOC, "r", encoding="utf-8", newline="") as f:
        back = f.read()
    if MARK not in back:
        print("✗ 复验失败：写入后找不到第 11 章")
        return 2
    print("✓ 第 11 章已追加，行数 %d → %d" % (raw.count("\n") + 1, back.count("\n") + 1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
