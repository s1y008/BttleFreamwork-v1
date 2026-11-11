# 设计文档：技能 10003 释放时的敌人表现完善

> Superpowers Phase 1（Brainstorming）+ Phase 2（Design Validation）产物
> 日期：2026-09-18 　工程：`BttleFreamwork-v1` 　状态：**已拍板 → 已实施 → 已验证（2026-09-18）**
> 实施结果见第 10 节；原第 7 节「待拍板决策」已按用户答复定稿。

---

## 0. 需求复述与目标边界

**原话**：「对当前框架在释放的技能 10003 全部完善敌人的表现」

拆解为可验证目标：玩家释放技能 **10003（三连斩）** 时，敌人在**每一段命中**上都要有与其配置相符的、看得见的受击表现。

**不在本次范围**：玩家侧动画、伤害数值公式、UI 血条/飘字、音效。

---

## 1. 现状勘察（全部为实测，非记忆）

### 1.1 配置层：10003 的真实数据

`Assets/Resources/Config/SkillData.csv`（27 列，0-based）

| 列 | 字段 | 值 |
|---|---|---|
| 1 | Name | **三连斩** |
| 13 | SkillEffectTypes | **`0\|0;22\|0;22`** |
| 23 | SegTimings | `0.3;0.6;0.9` |
| 24 | HitEffectIds | `10002;10001;10002` |
| 25 | CastEffectTimings | `0.05;0.28;0.58;0.88` |
| 26 | SegCount | `3` |
| 18/19 | BackDis / BackTime | **空** |
| 20/21 | AirHeight / AirTime | **空** |

按框架规则（`|` 分段、`;` 分效）展开：

```
第 1 段 = [0]        → 仅掉血
第 2 段 = [0, 22]    → 掉血 + 效果 22
第 3 段 = [0, 22]    → 掉血 + 效果 22
```

### 1.2 效果 22 是什么

`Skill.cs` 的 `SkillEffectTypes` 枚举：**`Hure = 22　// 受击`**（枚举末位）。

### 1.3 效果 22 的实现状态 —— 本次的根因

```csharp
// Skill.cs:362-364   —— switch 分支存在
case SkillEffectTypes.Hure:
    OnHure(m_SkillTarget);
    break;

// Skill.cs:387-390   —— 方法体为空
private void OnHure(CombatUnitController target)
{

}
```

**结论：第 2、3 段配了「受击」，但受击效果是一个空桩 ⇒ 敌人除了掉血和火花之外没有任何反应。**

### 1.4 顺带发现的一处隐患（同在 Hure 分支）

`ExecuteEffect(SkillEffectTypes effectType, CombatUnitController target)` 已经把命中目标作为参数传入，
但 Hure 分支调用的是 `OnHure(m_SkillTarget)` —— **用的是施法者身上缓存的目标字段，不是本次命中的 target**。

- 10003 当前是单目标（`SkillHitTypes=1`），两者恰好相同，暂不暴露问题；
- 一旦该技能改成扇形/圆形多目标（`HitTargetsInFan()` / `HitTargetsInCircle()` 会为每个目标调 `HitTarget`），
  所有受击表现都会打到 `m_SkillTarget` 一个人身上。

⇒ 本次一并修正为 `OnHure(target)`，改动只有几个字符，但消除的是一个真实的正确性缺陷。

### 1.5 动画层：敌人 Animator 的真相（决定方案可行性的关键）

> ⚠ **本节于 2026-09-18 第二轮重写**。第一轮把状态机误判为「17 个扁平状态」，
> 实际是**三层嵌套**——受击动画并非散落的孤儿，而是**整齐地收在一个名为 `Hit` 的子状态机里**。
> 这个订正直接改变了方案：不是「绕开缺失的转换」，而是「把整棵子树接进主流程」。

资源路径（用户提供）：

| 用途 | 路径 |
|---|---|
| 敌人模型与动画片段 | `Assets/ArtResources/Model/Enemy/哥布林NEW（小）`（**24 个 `@Xxx.FBX`） |
| 敌人动画状态机 | `Assets/ArtResources/AnimatorController/Enemy/GBL.controller` |
| 使用该控制器的预制体 | `Assets/Resources/Model/Enemy/GBL.prefab`（已实测确认） |

用重写后的 `Assets/Tools/parse_animator.py`（支持子状态机 + guid 反查动画名）解析得到真实拓扑：

```
[SM] Base Layer                     参数：Die(Trigger) / Run(Trigger) / SkillId(Int)
  ├─ idle  <默认>  clip=@idle.FBX
  │     → Skill20014   [SkillId Equals 20014]
  │     → Run_F        [无条件]
  │     → Down         [无条件]   ← 隐患
  │     → FloatHit     [无条件]   ← 隐患
  │     → Die          [无条件]
  ├─ Run_F        clip=@Run_F.FBX        → idle [ExitTime]
  ├─ Die          clip=@Die.FBX          → idle [ExitTime]
  ├─ [SM] Skill
  │     └─ Skill20014  clip=@Att01.FBX   → idle [ExitTime]
  └─ [SM] Hit   ★整棵子树无入边
        ├─ ContiHit  <默认>   clip=@ContiHit.FBX
        ├─ HeavyHit           clip=@HeavyHit.FBX
        ├─ FloatHit           clip=@FloatHit.FBX     → idle [ExitTime]
        ├─ Down               clip=@Down.FBX         → DownStand
        ├─ DownStand          clip=@DownStand.FBX    → idle [ExitTime]
        ├─ Dizzy / Frozen / HitDown / ForceDown / DownContiHit
        └─ DownFloat / DownHH / DownLH
```

**由此得出的四个硬结论**：

1. **受击动画不是孤儿，是「整棵子树断了入口」。** 13 个受击状态齐全且绑定了正确的片段，
   但**没有任何转换指向 `Hit` 子状态机** —— 所以整棵子树永远进不去。
2. **受击动画资源还有富余。** 24 个 FBX 中，`Hit` 子状态机只用了 13 个；
   **`AirHit` / `HitL` / `HitR` / `LightHit` 完全没被任何状态使用**（另 `Run_B/L/R`、`Skin` 也未用）。
   ⇒ 三连斩「轻—轻—重」的层次感可以直接用现成的 `LightHit` + `HeavyHit`，**不需要任何新美术资源**。
3. **参数只有 `SkillId` / `Run` / `Die`，既没有受击参数，也没有 `Move`。**
   后者正是每帧刷屏的 `Parameter 'Move' does not exist` 来源（`NavStop` 每帧 `SetBool("Move", false)`）——
   既有问题，非本次引入，可顺手修掉。
4. **另发现一处既有隐患**：`idle` 上挂着 3 条**无条件**出边（`Run_F` / `Down` / `FloatHit`）。
   多条无条件转换并存时 Unity 按列表顺序取第一条满足的，这是「敌人行为莫名漂移」的潜在来源。
   本次**不主动改它**（超出任务范围），但建立 `Hit` 转换时要避开与它们抢优先级。

### 1.6 表现编排层现状

`UnitHitReaction.ApplyHit`（View/UnitHitReaction.cs:75-110）当前四步：

```
判空/免疫 → 扣血 → 致死则 RequestState(Dead) → 播受击特效 → 记仇恨
```

**没有受击动作、没有顿帧、没有位移。**

`UnitView` 现有出口：`PlayEffect` / `SetSkillId` / `ResetSkillId` / `SetMoveAnim` / `SetRunAnim` /
`PlayDeath` / `StopMoveAnim` / `StateInfo` / `IsInIdleState` / `IsIdleToUseSkill` / `HasAnimator` /
`AnimatorSpeed`（**只读**）。**没有受击出口，也没有速度 setter。**

### 1.7 位移参数的解析状态

`SkillData`（BattleFreamwork/ConfigData/SkillData.cs）：
- `BackDis` / `BackTime` **已解析**（列 18/19）—— 但 10003 这两列**为空**
- `AirHeight` / `AirTime` **根本没有被解析**（列 20/21 无对应属性）

---

## 2. 缺口清单

| # | 缺口 | 影响 | 依赖 |
|---|---|---|---|
| G1 | `OnHure` 空实现 | 第 2、3 段受击无反应（根因） | 无 |
| G2 | Hure 分支用 `m_SkillTarget` 而非 `target` | 多目标时会打错人 | 无 |
| G3 | **控制器无受击参数，且 `Hit` 子状态机无入边** | 13 个受击动画全部无法触发 | 资源（可脚本化） |
| G3b | `Hit` 子状态机缺 `LightHit` / `AirHit` 状态 | 无法区分轻/重/浮空受击 | 资源（片段已存在） |
| G3c | 控制器缺 `Move` 参数 | 每帧一条 `Parameter 'Move' does not exist` 告警 | 资源（顺带） |
| G4 | `UnitView` 无受击出口 / 无速度 setter | 表现层没有落点 | 无 |
| G5 | 无顿帧机制 | 打击感弱 | 无 |
| G6 | 10003 的 BackDis/BackTime 为空 | 无击退位移 | CSV |
| G7 | **第 1 段未配 Hure** | 三连斩第一刀敌人完全无反应 | 配置决策 |

---

## 3. 设计约束（不可违反）

1. **受击表现不进 FSM。** 9-17 已定论：瞬时表现（扣血/特效/动作）做成状态会摧毁 `AttackState` 的
   8 个跨帧字段，导致多段技能断段，且状态互斥与 Buff 并存的等式不成立。
   受击动作/顿帧由 `UnitHitReaction` 直接编排，**不新增状态、不加转换边**。
2. **顿帧只作用于受击方。** 玩家 `AttackState` 靠 `normalizedTime` 推进段号；
   若对**攻击方**自身冻结 Animator，10003 的三段推进会直接卡死。
3. **画面端唯一出口仍是 `UnitView`。** Controller 与状态机不得直接碰 Animator（P13 三层分工）。
4. **CSV 是敌人表现的唯一真相源。** 表现由列 13 驱动，代码不得硬编码「第几段该做什么」。

---

## 4. 方案分层

> 相对第一轮的**重大调整**：既然要在状态机里正式制作状态与转换，
> 就不再需要 `Animator.Play` 绕路——改走**标准参数驱动**（`SetTrigger` / `SetInteger` + 转换条件）。
> 这是更规范、可维护、且能在 Unity Inspector 里直接观察调试的形态。

### A 段 —— 动画状态机改造（GBL.controller）

改动**全部脚本化**：写一个 `Assets/Editor/` 下的编辑器脚本，用 `UnityEditor.Animations` 官方 API 改，
再经 UnitySkills 的 `editor_execute_menu` 执行。**不手写 YAML**（fileID 分配与双向引用极易出错）。

**A1 新增参数（3 个）**

| 参数 | 类型 | 用途 |
|---|---|---|
| `Hit` | Trigger | 受击触发。与 `SkillId` 同为「事件型」参数，语义清晰 |
| `HitType` | Int | 受击类型：0=无 / 1=轻 / 2=重 / 3=浮空 / 4=击倒 / 5=眩晕 / 6=冰冻 |
| `Move` | Bool | 补上，`NavStop` 每帧 `SetBool("Move", false)` 不再告警 |

> 为什么用 `Hit`+`HitType` 而不是给每种受击各加一个 Trigger（Hit1/Hit2/...）：
> 6 个 Trigger 会产生「同时触发多个」的竞态，且无法表达「当前处于哪种受击」。
> 一个 Int 可直接查询、可强制归零，与框架里 `SkillId` 的用法保持一致。

**A2 新增状态（放进 `Hit` 子状态机，用现成未使用的片段）**

| 新状态 | 绑定片段 | 用途 |
|---|---|---|
| `LightHit` | `R_Monster_Gebuling@LightHit.FBX` | 三连斩第 1、2 刀的轻受击 |
| `AirHit` | `R_Monster_Gebuling@AirHit.FBX` | 浮空受击（对应 `OnAirHit` / `OnPlayerAirHit`） |

**A3 新增转换**

```
进入（父层 → 子状态机）
  Any State → Hit(SM)            [Hit]          ← 唯一入口，一条即可

分支（Hit 子状态机内部，按 HitType 分流）
  Entry → LightHit   [HitType Equals 1]
  Entry → HeavyHit   [HitType Equals 2]
  Entry → FloatHit   [HitType Equals 3]
  Entry → Down       [HitType Equals 4]
  Entry → Dizzy      [HitType Equals 5]
  Entry → Frozen     [HitType Equals 6]
  Entry → ContiHit   [默认，兜底]

回归（播完回常态）
  LightHit → idle    [ExitTime=1]
  HeavyHit → idle    [ExitTime=1]
  AirHit   → idle    [ExitTime=1]
  Dizzy    → idle    [ExitTime=1]
  Frozen   → idle    [ExitTime=1]
  （FloatHit → idle、Down → DownStand → idle 已存在，不动）
```

**为什么入口只有一条、分支放在子状态机内部**：若从父层直接连到 6 个具体状态，
就要建 6 条跨层转换，且每加一种受击都要回父层改。收敛成「一条入口 + 内部按 `HitType` 分流」后，
将来新增受击类型只需在 `Hit` 内部加一条 Entry 分支，**父层零改动**。

**A4 顺带建议（可选，需你同意）**：删掉 `idle → Down`、`idle → FloatHit` 这两条**无条件**出边。
它们与新建的受击转换语义重叠，且是「敌人莫名倒地/浮空」的隐患来源。
**默认不动**，因为改变的是既有行为，需独立验证。

### B 段 —— 代码侧接线（与 A 段配套）

| 项 | 做法 |
|---|---|
| 画面端出口 | `UnitView.PlayHit(int hitType)` → `SetInteger("HitType", t)` + `SetTrigger("Hit")`，播前 `HasState` 判存在 |
| 速度 setter | `UnitView.SetAnimatorSpeed(float)`（顿帧用，`PlayDeath` 首行恢复为 1） |
| 编排 | `UnitHitReaction.ApplyHurt(attacker, hitType)`：顿帧 → 播动作 |
| 接线 | `Skill.OnHure(target)` → `target.HitReaction.ApplyHurt(...)`，并修正为传入的 `target` |
| 数值来源 | 受击类型由 **CSV 列 13** 驱动，代码不硬编码「第几段轻/重」 |

### C 段 —— 数据驱动位移（依赖 CSV，缓办）

补 10003 的 `BackDis` / `BackTime`，`ApplyHurt` 读取后做位移。
`AirHeight`/`AirTime` 需先在 `SkillData` 补属性解析才能用于浮空表现 —— 属独立小项。

### 回退方案

若 A 段执行后你希望**不依赖资源改动**（例如要给多个敌人换控制器），
B 段可降级为 `Animator.Play(stateName)` 直播（第一轮 L1 方案）。
两者对上层 `UnitHitReaction` 的接口完全一致，**切换成本只是一个 `UnitView` 方法**。

---

## 5. 风险登记

| 风险 | 说明 | 缓解 |
|---|---|---|
| **R1 受击动画打断敌人自身攻击** | `Animator.Play` 会切换敌人当前动画；敌人若在 `AttackState`，其段号推进依赖 `normalizedTime`，切状态后进度语义改变 ⇒ 可能跳段/卡段 | 仅在受击方**非 Attack 状态**时播动作；在 Attack 时降级为只做顿帧+特效。**需用户确认是否允许「受击打断」** |
| **R2 顿帧期间 FSM 停滞** | Animator.speed=0 时 normalizedTime 不动，受击方若在 Attack 则其段号不推进 | 顿帧时长取 0.05–0.10s，且只作用于受击方 |
| **R3 段 1 无 Hure** | 第一刀无受击反应，与「三连斩」直觉不符 | 需确认：是配置遗漏（补 `0;22`）还是有意为之 |
| **R4 CSV 改动波及全局** | 改 BackDis 会影响所有读该列的技能 | 只改 10003 一行；不改公共默认值 |
| **R5 孤儿状态命名不匹配** | 若 GBL 之外的敌人用别的控制器，状态名可能不是 `HeavyHit` | 状态名走 `BattleConst` 常量，播前用 `Animator.HasState` 判存在 |

---

## 6. 验证策略（本项目的「红-绿」等价物）

本工程**无 Unity Test Framework 接入**（`BuffSystem.Tests/` 未接入测试运行器），
因此沿用工程既有的、已验证有效的**日志契约对照法**：

| 阶段 | 动作 | 判据 |
|---|---|---|
| Red | 先跑一轮 Play 采 baseline | `[SegEffect] 技能10003 第2段 效果=[0,22]` 存在，**但没有任何 `[HitReact]`** |
| Green | 实现后重跑 | 每一条 `第2/3段 效果=[0,22]` 对应**恰好一条** `[HitReact]`，1:1 |
| 不回归 | 对照基线 | `[HitEffectCalc]` 32 次 ↔ `[HitEffect]` 32 次 仍严格 1:1 |
| 静态 | 编译 + dll 时间戳 | 0 Error 且无源 .cs 晚于 `Assembly-CSharp.dll` |

另需跑：`Assets/Tools/smoke_html.js`、`Assets/Tools/check_html_tags.py`（文档同步后）。

---

## 7. 决策定稿（2026-09-18 用户答复）

| # | 问题 | **拍板** | 落地方式 |
|---|---|---|---|
| 1 | 受击是否打断敌人攻击 | **不打断**（上午实现 → 下午回退） | ~~`ApplyHurt` 中 `CurrentState == Attack` → `RequestState(Idle)`~~ **已撤销**：该分支对玩家与敌人**对称生效**，敌人技能一旦配效果 22，玩家的三连斩会被同样打断 —— 属玩法规则，应落在霸体 / 韧性或 FSM / Buff 的显式判定 |
| 2 | 第 1 段是否补配 Hure | **不补** | CSV 列 13 保持 `0\|0;22\|0;22`；第 1 刀只有掉血 + 火花，无受击动作 |
| 3 | 受击轻重如何判定 | **按现状，不做** | CSV 无强度列 ⇒ 统一 `HIT_TYPE_LIGHT`；`Skill.OnHure` 是将来加强度列的唯一替换点 |
| 4 | 范围 | **A+B** | 动画状态机改造 + 代码接线；击退位移（C 段）缓办 |
| 5 | 是否删 idle 两条无条件出边 | **不动**（用户表示不理解该点） | 改为「把 `idle → Hit` 插到 idle 转换列表首位」绕开它们 —— 见第 10 节 R1' |

> 附带约束（用户同轮提出）：**敌人本次不播放任何技能动画**，只做受击表现与（当时的）打断。

### 决策 1 的回退说明（2026-09-18 下午，用户第二轮答复）

用户原话：「打断对称的事情，以及对应的代码先恢复不增加这个打断的机制。」

实现后遗症：`ApplyHurt` 同时服务玩家与敌人，**没有任何一处区分身份**。一旦在这里写
`if (CurrentState == Attack) RequestState(Idle)`，效果就是双向的 —— CSV 里 37 个技能配了效果 22，
敌人用这些技能时玩家的三连斩会被逐段打断。当前场景只有玩家在放技能所以看不出来，
一旦敌人 AI 上线就直接暴露。

结论：**受击表现管线不做任何决策，只回答「怎么播」**。将来要加打断，正确落点是
在受击方侧加「是否允许被受击打断」的显式配置（霸体 / 韧性），由 FSM 或 Buff 判定后再切 Idle。
`UnitHitReaction.ApplyHurt` 的类注释里已经写死了这段理由，防止后人再捡回来。

### 决策 5 补充说明（用户说"不懂"，这里讲清楚）

`GBL.controller` 的 `idle` 状态上挂着 4 条**真正的无条件出边**（`hasExitTime=0` 且无任何条件的
`Run_F` / `Down` / `FloatHit` / `Die`）。所谓"无条件"就是**永远满足** —— 敌人一进 idle 就会立刻被
拉去播 Run_F。它们是历史遗留的脏边，删掉会改变既有行为（且需要独立验证），所以默认不动。

真正的问题是它们会**抢掉受击转换**：Unity 没公开保证「Any State 转换」与「当前状态的无条件出边」
谁优先。所以本次没有去赌这个规则 —— 而是把 `idle → Hit(SM) [Hit]` 插进 idle 转换列表的**第一位**，
再额外补一条 `Any State → Hit(SM)` 兜底。**两条并存，无论 Unity 怎么判都进得去**，也就不必动那 4 条脏边。

---

## 7b. 原决策原文（保留存档）

### 决策 1（R1）：受击是否允许打断敌人自身攻击？

- **选「不打断」（默认，安全）**：`ApplyHurt` 判断 `Owner.CurrentState != Attack` 才播动作，
  Attack 中降级为只做顿帧+特效。避免敌人段号推进因动画切换而错乱。
- **选「打断」（更真实）**：无条件播受击动作。需额外处理敌人 `AttackState` 的段号错乱（工作量增加）。

> 注：动画层用 `Any State → Hit` 意味着**动画上允许**打断；是否真的打断由 B 段代码判断控制。
> 这样同一份资源可以支持两种策略，切换只改一个 `if`。

### 决策 2（R3）：第 1 段是否补配 Hure？

现状第 1 段 = `[0]`（仅掉血），第一刀完全没有受击反应，与「三连斩」直觉不符。
若同意，把列 13 改为 `0;22|0;22|0;22`。**需你确认**（改 CSV 属全局数据变更）。

### 决策 3：受击轻重如何判定？（本次新增的关键设计问题）

CSV 列 13 只写了「效果 22 = 受击」，**没有「轻 / 重」之分**。而 `HitType` 需要一个具体数值。
可选：

| 方案 | 做法 | 评价 |
|---|---|---|
| **a. 末段加重（推荐）** | `segIdx == SegCount` 时 `HitType=2`（HeavyHit），其余 `=1`（LightHit） | 零配置改动，符合「三连斩最后一刀最重」直觉；属**代码启发式**，需在注释中标明 |
| b. 新增 CSV 列 | 加一列 `HitType`，逐段配置 | 最干净、完全数据驱动；但要动 27→28 列结构，**所有 `proArray[n]` 硬编码下标都要同步**（MEMORY 隐患 #8），成本高 |
| c. 一律轻型 | 所有 Hure 都用 `LightHit` | 最简单，但失去层次感，`HeavyHit` 等 13 段动画继续闲置 |

我推荐 **a**：把规则收进 `BattleConst`（如 `HIT_TYPE_LIGHT` / `HIT_TYPE_HEAVY`），
并注明「当前为启发式，将来若加 CSV 列则此处为唯一替换点」。

### 决策 4：范围

- **A+B（推荐）**：动画状态机改造 + 代码接线，能让三连斩完整表现「轻—轻—重」
- **A+B+C**：再加上击退位移（需你给 10003 的 BackDis / BackTime 数值）
- **仅 B**：不动资源，用 `Animator.Play` 直播单个受击动作（回退方案，表现层次最少）

### 决策 5：是否顺手删掉 `idle` 的两条无条件出边？

`idle → Down`、`idle → FloatHit` 是无条件转换，属既有隐患。**默认不动**（改变既有行为需独立验证）。

---

## 8. 验证策略补充（动画资源侧）

动画状态机改完**必须**能被工具复验，否则等于没改。用重写后的 `parse_animator.py` 做前后对照：

| 判据 | 改前 | 改后期望 |
|---|---|---|
| 参数个数 | 3（`Die`/`Run`/`SkillId`） | 6（新增 `Hit`/`HitType`/`Move`） |
| `Hit` 子状态机入边 | **无**（诊断报「无入边子状态机」） | 有 ≥1 条 |
| 无入边状态数 | 10 | 0（或仅剩刻意保留的） |
| `LightHit` / `AirHit` 状态 | 不存在 | 存在且绑定正确片段 |
| `Move` 告警 | 每帧一条 | 消失 |

`parse_animator.py` 现已支持子状态机树形输出与 guid 反查动画名，
改完直接重跑即可拿到**可 diff 的拓扑快照**。

---

## 9. 实施结果（2026-09-18 · 实测）

### 10.1 改了什么

| 层 | 文件 | 改动 |
|---|---|---|
| 动画资源 | `GBL.controller` | +参数 `Hit`(Trigger) / `HitType`(Int) / `Move`(Bool)；+状态 `LightHit`(@LightHit.FBX)；+转换 `idle→Hit(SM)`(置顶) / `Any State→Hit(SM)` / `Entry→LightHit` / `LightHit→idle` / `ContiHit→idle` |
| 编辑器脚本 | `Assets/Editor/GBLAnimatorBuilder.cs` | 新建，走 Unity 官方 `UnityEditor.Animations` API，**全程幂等**（二次执行全部"已存在，跳过"） |
| 常量 | `BattleConst.cs` | +`ani_Hit` / `ani_HitType` / `HIT_TYPE_*` / `HIT_STOP_SECONDS` |
| 画面 | `UnitView.cs` | +`PlayHit(int)`、`SetAnimatorSpeed(float)`、`EnsureHitParams()`（懒检查 + 只告警一次）；`PlayDeath()` 首行恢复 `speed=1` |
| 编排 | `UnitHitReaction.cs` | +`ApplyHurt(attacker, hitType)`（顿帧 → 播动作）、`HitStopRoutine()`（令牌递增防连击提前解冻）。~~含打断分支~~ **已回退，见 9b** |
| 根因 | `Skill.cs` | `OnHure` 空桩补齐；`OnHure(m_SkillTarget)` → `OnHure(target)` |
| 工具 | `Assets/Tools/parse_animator.py` | 修 3 个解析缺陷（见 10.3） |
| 文档 | 两份主 HTML + smoke 断言 | 同步 P15 章节，冒烟 71 项 ALL PASS |

### 10.2 验证（Play 实测，非推断）

| 判据 | 结果 |
|---|---|
| 编译 | **0 Error / 0 Warning**；`Assembly-CSharp.dll` mtime 晚于全部源 .cs |
| `[SegEffect] 效果=[0,22]`（第 2/3 段） | 99 条 |
| `[HitReact]` | **99 条 —— 严格 1:1** |
| `[SegEffect] 第1段 效果=[0]` | 50 条 → **无** `[HitReact]`（符合决策 2） |
| `[HitEffectCalc]` ↔ `[HitEffect]` | 149 : 149，仍 1:1（不回归） |
| `Parameter 'Move' does not exist` | **0 条**（A 段补了 Move 参数，术后消失） |
| 动画拓扑复验 | 参数 3 → 6；`Any State → Hit(SM)` 与 `idle → Hit(SM)`（首位）均在位；`Hit` 子树不再报"无入边" |

### 10.3 过程中修掉的三个工具缺陷（`parse_animator.py`）

1. **参数类型映射写反**：`4` 是 Bool、`9` 是 Trigger，此前当成连续下标 ⇒ 把 `Die/Run`(Bool) 误报成 Trigger。
2. **`fileID: 0` 被当成合法引用** ⇒ 「idle → Hit(子状态机)」被打印成「→ ?」。
3. **只解析 1101 不解析 1109** ⇒ Entry 分支整条漏掉，新建的 `LightHit` 明明有入边却被判成孤儿。
   另修正诊断口径：子状态机的默认状态（ContiHit）无入边是**正常兜底**，不该报孤儿。

---

## 9b. 第二轮（同日下午）：打断回退 + 死亡链路修复

用户第二轮答复两条：① 把打断机制及其代码**恢复回去**（不要这个机制）；② 修掉解析器发现的那处既有缺陷。

### 9b.1 打断回退（决策 1 翻案）

`UnitHitReaction.ApplyHurt` 里的 `if (CurrentState == Attack) RequestState(Idle)` **整块删除**，
注释改写为「为什么不做打断」。理由写在方法文档里，要点见第 7 节决策 1 的回退说明。
编译 0 Error。受击管线其余部分（顿帧 / 播动作 / 命中才播）不変。

### 9b.2 死亡链路修复（迁移前就存在的缺陷，本次根治）

| 层 | 文件 | 改动 |
|---|---|---|
| 动画资源 | `GBL.controller` | +转换 `Any State → Die [Die==true]`（AnyState 列表**首位**、`canTransitionToSelf=false`）；给 `idle → Die` 补条件 `[Die==true]`；给 `Die → idle` 补条件 `[Die==false]` |
| 编辑器脚本 | `Assets/Editor/GBLAnimatorBuilder.cs` | +菜单「GBL - 修复死亡链路（Die）」，幂等；+菜单「死亡链路自检（需先进入 Play）」 |
| 常量 | `BattleConst.cs` | +`ani_Die`（注释写明：**敌人是 Bool、玩家根本没有这个参数**） |
| 画面 | `UnitView.cs` | `PlayDeath()` 不再裸写 `SetTrigger`；新增 `WriteDeathParam()`：查类型 → Bool 写 `SetBool(true)` / Trigger 写 `SetTrigger` / 不存在只告警一次；抽出公共的 `FindParameter()` |
| 验证工具 | `Assets/Scripts/BattleFreamwork/Debug/DeathLinkProbe.cs` | 新建运行时探针（`DeathLinkProbe`），`#if UNITY_EDITOR` 包裹、不进正式包 |

**三个病根必须同时治**（缺任何一个都播不出来）：

1. `Die` 是 **Bool**（`m_Type: 4`），而旧代码写 `SetTrigger("Die")` —— 对 Bool 参数设触发器
   **既不生效也不报错**，静默失效；
2. `idle → Die` 是**无条件**边却排在 idle 转换列表**末位**，前面 Run_F / Down / FloatHit
   三条同样无条件 ⇒ 这条边从未生效过，**死亡根本没有入口**；
3. `Die → idle` 有 `ExitTime` 回边 —— 就算真进去了播完也会被拉回待机。
   ⚠ 只做 2 不做 3 会立刻变成「死亡 → 回待机 → 再被 AnyState 拉去死亡」的**无限循环**。

### 9b.3 验证（Play 实测）

- `[Death] GBL(Clone) 写 Die(Bool) = 死亡` —— 类型识别正确
- `[DeathDiag]` 8 次采样 `inDie=True` 全部成立、`Die=True`；
  结论行 `[DeathDiag] 结论 PASS —— GBL(Clone) 进入并停留在死亡状态`
- 受击契约未被破坏：`[HitReact]` 51 条 ↔ `[SegEffect] 效果=[0,22]` 53 条 —— **差额 2 正是死亡之后的两段**
  （`ApplyHurt` 首行 `IsDead` 直接 return）；`[HitEffectCalc]` ↔ `[HitEffect]` 78 : 78
- 0 Error / 0 Warning

> ⚠ 踩坑记录：**`EditorApplication.update` 在本机编辑器未获焦时一次都不回调**，
> 第一次自检只拿到"选中"日志、采样全空。改用运行时探针对象（MonoBehaviour.Update）才拿到结果。

---

## 10. 一句话总结（第二轮订正）

10003 的第 2、3 段早就配好了「受击」，但代码侧 `Skill.OnHure()` 是**空桩**，
动画侧 `Hit` 子状态机（内含 13 个受击动画）**整棵子树没有任何转换能进入** —— 两头都断着。

与第一轮判断不同的是：**受击动画不是散落的孤儿，而是被整齐收在 `Hit` 子状态机里**，
且资源库里还有 `LightHit` / `AirHit` 等未使用的片段。所以正确做法不是「用 `Animator.Play` 绕开」，
而是**正式把 `Hit` 子树接进主流程**：加 `Hit`+`HitType` 参数、补 `LightHit`/`AirHit` 两个状态、
用「一条入口 + 内部按 `HitType` 分流」的转换结构，让三连斩打出「轻—轻—重」的层次。

**当日两处修正**：受击**不打断**攻击（该机制对玩家与敌人对称，属玩法规则，已从受击管线中移除）；
以及顺手根治了「死亡动画从未播放」——`Die` 是 Bool 却用 `SetTrigger` 写值、`idle → Die` 边排在末位永不可达、
`Die → idle` 回边会把尸体拉回待机，三个病根一起治掉后才真正播得出来。
资源改动全部走 Unity 官方 Editor API 脚本化执行，不手写 YAML。
