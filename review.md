# 代码审查：v5 P15 受击表现接通（Superpowers Phase 6）

> 日期：2026-09-18　审查范围：A 段（GBL.controller 改造）+ B 段（代码接线）
> 审查依据：工程既定边界（受击表现不进 FSM / 画面端唯一出口 UnitView / CSV 是唯一真相源）

---

## 结论

| 级别 | 数量 | 处理 |
|---|---|---|
| **Critical** | 0 | — |
| **Major** | 0（本次改动）／ 1（既有缺陷，非本次引入） | 既有缺陷单独登记，不在本次改 |
| **Minor** | 4 | 已记录，其中 1 项需用户拍板 |

**审查通过。** 四条硬约束逐条核验如下。

---

## 硬约束核验

### ① 受击表现不进 FSM —— ✅ 通过

`ApplyHurt` 全程没有 `AddTransition` 级别的新状态，也没有新增 `CombatState` 枚举值。
「打断」用的是转换表里**本来就存在**的 `Attack → Idle`
（`CombatStateMachine.InitDefaultTransitions` 第 58 行），
`AttackState.OnExit` 清引用、`IdleState.OnEnter` 做 `NavStop + ResetSkillId`，收尾完整。

对照 9-17 的定论：瞬时表现做成状态会摧毁 `AttackState` 的 8 个跨帧字段 → 本次未触碰。

### ② 顿帧只作用于受击方 —— ✅ 通过

`HitStopRoutine` 只对 `m_Owner`（受击方）调 `m_View.SetAnimatorSpeed(0f)`。
攻击方的 `AttackState.OnUpdate` 读的是自己 Animator 的 `normalizedTime`，不受影响。
实测 `[AttackState] skill=10003 seg=1/2/3` 三段时序与术前一致（0.30 / 0.60 / 0.90），无卡段。

### ③ 画面端唯一出口仍是 UnitView —— ✅ 通过

新增的两处画面操作 `PlayHit` / `SetAnimatorSpeed` 都在 `UnitView`；
`UnitHitReaction` 只做编排，Controller 与状态类**未新增任何一处直接摸 Animator 的代码**。
全量确认：`Assets/Scripts/BattleFreamwork/Controller/CombatUnitController.cs` 与
`StateMachine/CombatStates.cs` 本次零改动。

### ④ CSV 是唯一真相源 —— ✅ 通过（按用户拍板收窄）

效果 22 的触发完全由 CSV 列 13 驱动，代码里没有「第几段该做什么」的硬编码。
⚠ 唯一一处**刻意**的硬编码是 `Skill.OnHure` 里的 `HIT_TYPE_LIGHT` ——
因为 CSV 当前没有「受击强度」列，用户拍板「按现状配置来、不发明启发式」。
已在方法注释中标明「将来若新增强度列，此处是唯一替换点」。

---

## Minor（4 项）

### M1　打断是**对称**的，玩家被敌人打中也会被打断 —— ✅ 已关闭（2026-09-18 下午）

**处理：整机制回退。**用户拍板「打断对称的事情，以及对应的代码先恢复不增加这个打断的机制」，
`ApplyHurt` 里的 `RequestState(Idle)` 分支已删除，注释改为说明「为什么这里永远不该做打断」。

回退比"加个阵营判断"更合适的理由：受击表现管线的职责是回答「怎么让它看起来发生了」，
而「这一下要不要打断」是**玩法规则**（霸体 / 韧性），它的天然位置是 FSM 或 Buff 侧的显式判定。
把规则藏在通用表现管线里，等于让每个将来阅读受击代码的人都必须重新发现一次对称性。

### M2　顿帧期间 `AnimatorSpeed == 0` 会让 `ApplyKnockback` 直接拒绝

`ApplyKnockback` 有一条防御：`!m_View.HasAnimator || m_View.AnimatorSpeed == 0f` 时忽略击退
（沿用原 `OnHitOut` 的语义，本意是"剧情暂停时不接受位移"）。
若某技能同一段里**先配 22 再配击退类效果**，顿帧还没结束，`ApplyKnockback` 会被这条防御吃掉。

当前**不可达**：`Skill.OnAirHit / OnHitOut / OnPlayerAirHit` 仍是空桩，
`ApplyKnockback` 经 P13 全量扫描确认外部零调用。属潜在顺序陷阱，登记待观察。
将来接通击退时，应把「顿帧冻结」与「剧情暂停冻结」用**两个不同标志**区分开，而不是共用 `speed == 0`。

### M3　`HIT_TYPE_HEAVY / FLOAT / DOWN / DIZZY / FROZEN` 目前无消费者

这 5 个常量是 HitType 协议的预留档位，动画层只建了 `HitType=1` 的分支，传它们不会有对应动作。
已在 `BattleConst` 注释里写明「切勿以为传这些值就能出效果」。
保留理由：加一档受击 = 加常量 + 加一条 Entry 分支 + 加一条回 idle，有唯一的地方照着做。

### M4　未接入的 9 段受击动画仍是无入边状态

`HeavyHit / FloatHit / Down / Dizzy / Frozen / DownContiHit / DownFloat / DownHH / DownLH / HitDown`
继续闲置。本次按「宁缺毋滥」没有为它们建分支 —— 没有触发路径的状态就是死状态。
`FloatHit / Down` 虽在 `idle` 的无条件出边里被引用，但那是既有脏边，不是本次设计。

---

## Major（既有缺陷，非本次引入，单独立项）

### ★ 敌人 `Die` 参数是 **Bool**，`UnitView.PlayDeath()` 却用 `SetTrigger("Die")` —— ✅ 已修复（2026-09-18 下午）

原判定：P13 迁移时是「原样搬迁」，`SetTrigger("Die")` 在迁移前就存在，**属既有问题**。

实际修复后才发现，这只是三个病根之一 —— 单独改这一处死亡动画仍然不会播：

| # | 病根 | 修复 |
|---|---|---|
| ① | `Die` 是 Bool（`m_Type: 4`），代码却用 `SetTrigger`，对 Bool 设触发器**既不生效也不报错** | `UnitView.WriteDeathParam()`：查参数真实类型后分派（Bool→`SetBool(true)` / Trigger→`SetTrigger` / 不存在只告警一次） |
| ② | `idle → Die` 是**无条件**边却排在 idle 转换列表**末位**，前面三条同样无条件的边把它抢掉 ⇒ **死亡根本没有入口** | 给它补 `[Die == true]`，并新增 `Any State → Die [Die==true]` 作真入口（AnyState 列表首位 + `canTransitionToSelf=false`） |
| ③ | `Die → idle` 有 `ExitTime` 回边，播完会被拉回待机 | 补 `[Die == false]` |

⚠ **②③ 必须成对处理**：只加入口不拦回边，会立刻变成「死亡 → 回待机 → 再被拉去死亡」的无限循环。
这是本次改动里最反直觉的一步，已在 `GBLAnimatorBuilder.FixDeathLink` 的注释里写明。

> 附：复盘原判里的一处不准确 —— 当时写「玩家 NYT 的 Die 是 Trigger」，实际
> **NYT.controller 根本没有 Die 参数**（只有 `Atk` / `Run` / `SkillId`），
> 所以在这里必须做「参数不存在」的分支，且只能告警一次，否则每次玩家死亡都刷屏。

---

## 资源改动的可回滚性 —— ✅

- 备份：`GBL.controller.bak.2026-09-18-anim`（23159 字节，与原文件一致）。
- 脚本幂等：二次执行 10 条日志全部为「已存在，跳过」，不会翻倍插入。
- 未手写 YAML：全部走 `UnityEditor.Animations`，fileID 由 Unity 分配。

## 协程安全性 —— ✅

`HitStopRoutine` 由 `m_Owner.StartCoroutine` 启动，挂在受击方的 MonoBehaviour 上：
单位销毁 / 场景切换时协程被 Unity 自动终止，**不会访问已销毁对象**。
顿帧结束只做一次 `SetAnimatorSpeed(1f)`，无状态残留。
⚠ 唯一遗留：若将来引入战斗单位**对象池复用**，需在回收时补一句 `SetAnimatorSpeed(1f)`，
否则回收时正好处于顿帧窗口的单位会以 `speed = 0` 复活。当前是 Instantiate/Destroy，不涉及。

---

## 验证证据（非推断）

| 项 | 结果 |
|---|---|
| 编译 | 0 Error / 0 Warning；`Assembly-CSharp.dll` 晚于全部源 .cs |
| `[SegEffect] 效果=[0,22]` ↔ `[HitReact]` | 99 : 99 严格 1:1 |
| 第 1 段 `效果=[0]` | 50 条 → 无 `[HitReact]`（符合「第 1 段不补 Hure」） |
| `[HitEffectCalc]` ↔ `[HitEffect]` | 149 : 149（不回归） |
| `Parameter 'Move' does not exist` | 0 条（术前每帧一条） |
| 动画拓扑 | 参数 3 → 6；两条 Hit 入口在位；Hit 子树不再报无入边 |
| 文档 | 两份主 HTML 同步；`smoke_html.js` 全部 ALL PASS；标签配平 OK |

### 第二轮（下午复发包）

| 项 | 结果 |
|---|---|
| 编译（回退打断 + 死亡修复） | 0 Error / 0 Warning |
| `[Death]` | `GBL(Clone) 写 Die(Bool) = 死亡` —— 类型识别正确 |
| `[DeathDiag]` 采样（0.4s × 8 次） | 全部 `inDie=True`、`Die=True`；结论 **PASS —— 进入并停留在死亡状态** |
| `[HitReact]` ↔ `[SegEffect] 效果=[0,22]` | 51 : 53 —— **差额 2 正是死亡之后的两段**（`ApplyHurt` 首行 `IsDead` 直接 return） |
| `[HitEffectCalc]` ↔ `[HitEffect]` | 78 : 78（不回归） |
| `GBL.controller` 拓扑（解析器复验） | `Any → Die [Die If]`（AnyState 首位）、`idle → Die [Die If]`、`Die → idle [Die IfNot]` 均在位 |
| 文档 | `design.md` 新增 9b；两份主 HTML 新增 5.7 / STEP 04-P15fix；smoke ALL PASS + 标签配平 OK |

⚠ **本机踩坑**：`EditorApplication.update` 在编辑器窗口**未获焦时一次都不回调**，
第一版自检只拿到「选中」日志、采样全空 —— 差点误判成修复失败。
改用运行时探针 `DeathLinkProbe`（MonoBehaviour.Update + `Time.time` 间隔采样）才拿到真实结果。

---

## 第三轮：用户反馈「死亡后没有缓慢下沉」调查（2026-09-18 傍晚）

### 结论：功能未丢失 —— 三个假设全部被实测证伪

用户反馈「现在敌人死亡只有死亡动画、看不到先前缓慢沉入地面」。一度怀疑是 P15-fix 引入，
实测证明下沉全程存活。**列这条的用意不是自证清白，而是把三个错误假设固化下来，避免重犯。**

| 判据 | 实测 | 结论 |
|---|---|---|
| `NavMeshAgent` | **敌人身上没有这个组件** | 假设①「Agent 把单位吸回 NavMesh」证伪 |
| `applyRootMotion` / `rmDeltaY` | `False` / `0.0000` | 假设②「Root Motion 覆盖 position」证伪 |
| `stateInfo.loop` | **False** | 假设③「死亡动画无限重复」证伪 |
| 累计下沉 | **4.500m** → Destroy | 下沉链条完整 |
| `[HitEffectCalc]` ↔ `[HitEffect]` | 124 : 124 | 受击链路无回归 |

下沉时序：`t=4.1s: 0.000` → `t=6.1s: -0.499` → `t=8.1s: -1.317` → `t=10.1s: -1.986` → `t=26.2s: -4.394`。
`Skin()` 协程（`AttackNpcController`）本次**一行未改**，速度必然与改动前一致。

### ⚠ Major：不要再用 `normalizedTime` 判断动画是否循环

实测 `nt` 一路涨到 **21.21**，曾据此判定「死亡动画在无限循环」——**这是错的**。
`AnimatorStateInfo.normalizedTime` 在 non-looping 状态下**同样持续累加**：
`+1.767 / 2s`，而 `2s ÷ 1.133s(片段时长) = 1.765` —— 增速恰好等于 `dt / 时长`，
说明「循环」与「播完定格」两种情况的涨法一模一样，据此判断必错。
**唯一可靠判据是 `AnimatorStateInfo.loop`**（本次实测 `False`）。

### ★ 过程中一度把资产改坏，已完全复原并留了回放路径

为「修死亡动画循环」执行了「Die 动画改为不循环」菜单，它给 `Die.FBX.meta`
**显式写入 clipAnimations** ⇒ FBX 内冒出两个片段、Die 状态 Motion 变 **Missing**（不报错）。

- ⚠ **老 meta 备份不能直接覆盖**：2018 版 155 行 vs Unity 2022 重写版 370 行，
  覆盖会让 Unity 重新分配 fileID，同样接不上。
- ✅ 复原：删掉 meta 里那 30 行 clipAnimations 条目 → `ImportAsset(ForceUpdate)`
  → Unity 按默认 take 重建出原 `fileID 7400000`。最终 `Motion：✅ Die`（1.133s）。
- 🍏 已固化为两个菜单：`BattleTools/GBL - 重导入 Die.FBX 并修复引用`、`校验 Die 片段引用`。

### 本轮改动（很克制，且如实标注效力）

| 文件 | 改动 | 对本次症状的效力 |
|---|---|---|
| `MoveBaseController.cs` | +`StopNavigation()`（清零 velocity → isStopped → enabled=false） | — |
| `CombatUnitController.OnDead()` | ClearBuffs **之前**调 `StopNavigation()` | **无**（敌人无 NavMeshAgent） |
| `DeathLinkProbe.cs` | 结论行兜底输出 `loop`；`GetBoneTransform` 包 try/catch | 诊断增强 |

`StopNavigation()` 之所以保留：死亡就该停导航，是标准收尾；且 `OnAnimatorMove` 里本就有
`if (m_NavMeshAgent.enabled)` 闸门，禁用后自动短路，无需再散一处 IsDead 判断。
**但它不是本次症状的解**，将来换上带 NavMeshAgent 的敌人规格时才会真正生效。

### ★ 修掉的自我污染
探针里为交叉验证加了 `GetBoneTransform(HumanBodyBones.Hips)`，
敌人 Avatar 是 **Generic** ⇒ 每帧抛 `InvalidOperationException`，共刷出 **12 条 Error**。
已包 try/catch 降级为 `非Humanoid`。教训：为一个锦上添花的诊断字段把 Error 刷满，
会稀释 Error 本身的含义，得不偿失。

### ★ 既有缺陷（非本次引入，未改，待用户定夺）
每帧抛 NullReference：
`CombatUnitController.Update():925` → `DoAutoFight()` → `AttackNpcController.AutoFightUpdate():99`
`if (IsDead || CanAttack > 0)` → `get_CanAttack():152` `CurCombatUnit.CantAttack`，**CurCombatUnit 为 null**。
同源风险点：`AttackNpcController.OnDead()` 末行 `CanAttack = 1;`（好在它排在 `StartCoroutine(Skin())` 之后，不影响下沉）。

### 第三轮验证证据

| 项 | 结果 |
|---|---|
| 编译 | 0 Error（依次为 Probe 补齐 / 自清理 / loop 兜底 / try-catch 四次重建） |
| `[SinkDiag]` 结论行 | `死亡动画：PASS`；`缓慢下沉：PASS（4.500m）`；**`循环=False`** |
| `[HitEffectCalc]` ↔ `[HitEffect]` | 124 : 124 |
| `[SegEffect] 效果=[0,22]` ↔ `[HitReact]` | 84 : 82（差额 = 死亡之后的两段） |
| 拓扑复验 | 6 参数；`Any→Die [Die If]`、`Die→idle [Die IfNot]`、`Die` 绑定 `GBL@Die.FBX` |
| 引用校验 | `控制器 Die 状态 → Motion：✅ Die`；`fileID 7400000` 已回到原名 |

### ⚠ 本机踩坑（第二轮）
探针是场景对象，上一轮未自然走完就 stop 时**会带着旧 Target 残留**，
下次采样给出「目标是已销毁旧对象」的假结论。已在自检菜单开头加自清理
（`FindObjectsOfType<DeathLinkProbe>()` 全部销毁）。
