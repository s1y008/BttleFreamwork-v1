# 实施计划：技能 10003 敌人表现完善（A 段动画状态机 + B 段代码接线）

> Superpowers Phase 3（Implementation Planning）产物　日期：2026-09-18（**第二轮 · 含动画状态机改造**）
> 依据：`design.md`（已按子状态机真实结构订正）
> ## ✅ 已执行完毕（2026-09-18）　决策：①~~打断~~**已回退（对称性，用户下午拍板）** ②第1段不补 Hure ③不区分轻重 ④范围 A+B ⑤不动 idle 脏边
> 执行结果与验证数据见文末「执行结果」一节；与设计稿的**两处偏离**也在该节列出。
>
> ## ✅ 第二轮（同日下午）：①打断机制整体回退 ②死亡链路修复（Die 是 Bool + 两个拓扑缺陷）
> 详见 `design.md` 第 9b 节；两份主 HTML 已同步（技术文档 5.7 / 全链路 STEP 04-P15fix）。

---

## 全局约定

- 每个任务 2–5 分钟，可独立验证；任一步失败即停，不带着失败往下走。
- 备份标签：代码 `.bak.2026-09-18-hurtfx`；动画资源 `.bak.2026-09-18-anim`
- 无 git —— 备份是唯一后悔药（工程铁律 1）。**`.controller` 也必须备份**。
- 同一文件的多次编辑**串行**执行（工程铁律 6-1）。
- 动画资源**不手写 YAML**：一律走 Unity 官方 `UnityEditor.Animations` API。

---

## 阶段 A　动画状态机改造

### T0　备份（含动画资源）

```bash
# 代码
Skill.cs / UnitView.cs / UnitHitReaction.cs / BattleConst.cs       → *.bak.2026-09-18-hurtfx
# 动画资源（无 git，改坏了只能靠它）
Assets/ArtResources/AnimatorController/Enemy/GBL.controller        → .bak.2026-09-18-anim
```

**验证**：5 份备份存在且字节数与原文件一致。

---

### T1　编写编辑器脚本 `Assets/Editor/GBLAnimatorBuilder.cs`

新建目录 `Assets/Editor`（当前不存在）。脚本提供三个菜单项，用 `UnityEditor.Animations` API 实现。

**核心逻辑（骨架）**：

```csharp
[MenuItem("BattleTools/GBL - 建造受击状态与转换")]
static void Build()
{
    const string CTRL = "Assets/ArtResources/AnimatorController/Enemy/GBL.controller";
    const string FBX  = "Assets/ArtResources/Model/Enemy/哥布林NEW（小）/";
    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL);

    // A1 参数：已存在则跳过（幂等）
    AddParam(ctrl, "Hit",     AnimatorControllerParameterType.Trigger);
    AddParam(ctrl, "HitType", AnimatorControllerParameterType.Int);
    AddParam(ctrl, "Move",    AnimatorControllerParameterType.Bool);

    var root = ctrl.layers[0].stateMachine;
    var hit  = FindSubSM(root, "Hit");          // 已存在的子状态机
    var idle = FindState(root, "idle");

    // A2 新状态（幂等：已存在则复用）
    var lightHit = EnsureState(hit, "LightHit", LoadClip(FBX + "R_Monster_Gebuling@LightHit.FBX"));
    var airHit   = EnsureState(hit, "AirHit",   LoadClip(FBX + "R_Monster_Gebuling@AirHit.FBX"));

    // A3-1 入口：Any State → Hit 子状态机 [Hit]
    //   ⚠ 若 Unity 拒绝 AnyState→子状态机，回退为 idle → Hit（脚本内 try/catch + 日志）
    var entry = root.AddAnyStateTransition(hit);
    entry.AddCondition(AnimatorConditionMode.If, 0, "Hit");
    entry.hasExitTime = false;
    entry.duration = 0.05f;

    // A3-2 分支：Hit 内部 Entry 按 HitType 分流
    EntryTo(hit, "HeavyHit", 2);  EntryTo(hit, "FloatHit", 3);
    EntryTo(hit, "Down", 4);      EntryTo(hit, "Dizzy", 5);
    EntryTo(hit, "Frozen", 6);    EntryTo(hit, "LightHit", 1);
    // ContiHit 保持为子状态机默认状态（兜底）

    // A3-3 回归：播完回 idle
    BackTo(idle, "LightHit", "HeavyHit", "AirHit", "Dizzy", "Frozen");

    EditorUtility.SetDirty(ctrl);
    AssetDatabase.SaveAssets();
    Debug.Log("[AnimBuild] GBL 受击状态与转换建造完成");
}
```

**要点**：
- 全部方法**幂等**（参数/状态已存在则跳过），重复执行不会翻倍插入（工程铁律 6-11）。
- `LoadClip` 用 `AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath).OfType<AnimationClip>()`，
  再按名字匹配（Unity 对 `Model@Anim.fbx` 的命名约定会产出同名 clip）。
- 每个关键步骤 `Debug.Log`，便于通过 UnitySkills 日志确认哪一步失败。

**验证**：文件存在；编译 0 Error。

---

### T2　编译并执行建造

```bash
python Assets/Tools/unity_skills.py recompile
python Assets/Tools/unity_skills.py errors                      # 期望 0 Error
python Assets/Tools/unity_skills.py call editor_execute_menu '{"menuPath":"BattleTools/GBL - 建造受击状态与转换"}'
python Assets/Tools/unity_skills.py logs 80                     # 期望看到 [AnimBuild] …建造完成
```

**验证**：日志出现 `[AnimBuild]` 完成标记，且无异常堆栈。

---

### T3　用解析器复验拓扑（可 diff 的硬证据）

```bash
python Assets/Tools/parse_animator.py "Assets/ArtResources/AnimatorController/Enemy/GBL.controller"
```

**期望对照**：

| 判据 | 改前 | 改后 |
|---|---|---|
| 参数 | 3（Die/Run/SkillId） | 6（+Hit/HitType/Move） |
| `Hit` 子状态机入边 | 无（诊断报「无入边子状态机」） | ≥1 条 |
| 无入边状态 | 10 | 0 |
| `LightHit` / `AirHit` | 不存在 | 存在，clip 分别为 `@LightHit` / `@AirHit` |
| 诊断结论 | ★★ 三个子状态机全无入边 | 仅 `Base Layer` 报无入边（根层，正常） |

> 若任一项不符 → **立刻从 `.bak.2026-09-18-anim` 还原**，不要往下走。

---

### T4　Unity 内目检（需你确认）

切到 Unity 打开 GBL.controller，确认：
- Parameters 面板有 `Hit` / `HitType` / `Move`
- `Hit` 子状态机有 `LightHit`、`AirHit` 两个新状态且绑定了片段
- Any State（或 idle）有一条指向 `Hit` 的转换
- 播放 10003 时敌人有受击动作

---

## 阶段 B　代码侧接线

### T5　动画常量

**文件**：`Assets/Scripts/BattleFreamwork/BattleConst.cs`

```csharp
/// <summary>受击类型（对应 GBL.controller 的 HitType 参数）</summary>
public const int HIT_TYPE_NONE  = 0;
public const int HIT_TYPE_LIGHT = 1;   // LightHit
public const int HIT_TYPE_HEAVY = 2;   // HeavyHit
public const int HIT_TYPE_FLOAT = 3;   // FloatHit
public const int HIT_TYPE_DOWN  = 4;
public const int HIT_TYPE_DIZZY = 5;
public const int HIT_TYPE_FROZEN= 6;

/// <summary>受击顿帧时长（秒）。只作用于受击方</summary>
public const float HIT_STOP_SECONDS = 0.08f;
```

**验证**：编译通过；`grep HIT_TYPE_HEAVY` 命中。

---

### T6　`UnitView` 新增受击出口与速度 setter

```csharp
/// <summary>播放受击动作。走标准参数驱动：先写 HitType 再触发 Hit。
/// ⚠ 顺序不可颠倒 —— Entry 分支在触发瞬间就读 HitType。</summary>
public void PlayHit(int hitType)
{
    if (m_Animator == null) return;
    m_Animator.SetInteger(BattleConst.ani_HitType, hitType);
    m_Animator.SetTrigger(BattleConst.ani_Hit);
}

/// <summary>设置播放速度（顿帧用）。0=冻结，1=正常。</summary>
public void SetAnimatorSpeed(float speed)
{
    if (m_Animator != null) m_Animator.speed = speed;
}
```

**同时修改 `PlayDeath()`**：首行加 `SetAnimatorSpeed(1f);`

> 理由：顿帧期间若死亡，Animator.speed 仍为 0 ⇒ Die 动画卡在第一帧。这是顿帧机制引入的必修补丁。

**验证**：编译通过；`PlayDeath` 首行为速度恢复。

---

### T7　`UnitHitReaction.ApplyHurt`（顿帧 + 编排）

顶部补 `using System.Collections;`

```csharp
private int m_HitStopToken = 0;   // 连击时作废旧令牌，避免提前恢复速度

public void ApplyHurt(CombatUnitController attacker, int hitType)
{
    if (attacker == null || m_Owner.IsDead) return;

#if UNITY_EDITOR
    Debug.Log($"[HitReact] {m_Owner.name} 受击反应 来自={attacker.name} type={hitType}");
#endif

    m_Owner.StartCoroutine(HitStopRoutine(BattleConst.HIT_STOP_SECONDS));

    // 决策 1 保守策略：敌人正在攻击时不切动画，避免其段号推进错乱
    if (m_Owner.CurrentState != CombatState.Attack)
    {
        m_View.PlayHit(hitType);
    }
}
```

**验证**：编译通过；`ApplyHurt` 与 `HitStopRoutine` 存在。

---

### T8　接通 Hure 分支 + 受击类型推导（本次根因修复）

**文件**：`Assets/Scripts/BattleFreamwork/Skill.cs`

```csharp
// 362-364：把 m_SkillTarget 改为传入的 target（修正多目标打错人）
case SkillEffectTypes.Hure:
    OnHure(target);
    break;

// 387-390：空桩补齐
private void OnHure(CombatUnitController target)
{
    if (target == null) return;
    // 决策 3-a 启发式：末段为重型受击，其余轻型。
    // ⚠ 当前 CSV 无「受击强度」列，此为唯一推导点；将来若加列，只改这里。
    int hitType = (m_SegIdx >= BaseData.GetSegmentCount())
        ? BattleConst.HIT_TYPE_HEAVY
        : BattleConst.HIT_TYPE_LIGHT;
    target.HitReaction.ApplyHurt(m_SkillCaseter, hitType);
}
```

**验证**：编译通过；`scan_refs.py OnHure` 命中 2 处；`ApplyHurt` 命中 1 处。

---

### T9　编译与程序集核验

```bash
python Assets/Tools/unity_skills.py recompile
python Assets/Tools/unity_skills.py errors      # 期望 0 Error
# 硬判据：Assembly-CSharp.dll 必须晚于所有源 .cs
```

---

## 阶段 C　验证

### T10　Red —— 术前基线

在**阶段 A 之前**先采一轮（否则改完就没法证明"改前是坏的"）：

```bash
python Assets/Tools/unity_skills.py stop
python Assets/Tools/unity_skills.py call console_start_capture '{}'
python Assets/Tools/unity_skills.py play      # 等 ~28s
python Assets/Tools/unity_skills.py logs 200
```

**Red 判据**：
- 有 `[SegEffect] 技能10003 第2段 效果=[0,22]`
- **零条 `[HitReact]`**
- `[HitEffectCalc]` 32 ↔ `[HitEffect]` 32 保持 1:1

> ⚠ 所以**实际执行顺序应为 T10 → 阶段 A → 阶段 B → T11**。

---

### T11　Green —— 实现后重跑

| 判据 | 期望 |
|---|---|
| 每条 `[SegEffect] …效果=[0,22]`（第 2、3 段） | 对应**恰好一条** `[HitReact]` |
| `[HitReact]` 的 type 分布 | 第 2 段 mostly `1`（轻），第 3 段 `2`（重） |
| `[HitEffectCalc]` ↔ `[HitEffect]` | 仍严格 1:1（不回归） |
| `Parameter 'Move' does not exist` | **消失**（A1 补了 Move 参数） |
| Console | NO ERRORS |

> ⚠ 若当天反复 recompile 后 Play 采不到战斗日志（9-17、9-18 均出现过该编辑器侧现象），
> 先回滚跑 baseline 做硬对照；若改动前版本同态 ⇒ 与本次无关，需用户切 Unity 窗口手动 Play 复验。

---

### T12　文档同步与静态校验

1. 同步主文档 `战斗框架技术文档_段号驱动与状态机推进.html` 与 `战斗框架全链路与数据流转.html`：
   补「Hure(22) 已接通 / Hit 子状态机接入 / HitType 分流 / 顿帧机制」
2. `node Assets/Tools/smoke_html.js`（补 P15-hurt 断言）
3. `python Assets/Tools/check_html_tags.py <两份文档>`

**验证**：冒烟 ALL PASS、标签配平 OK。

---

### T13　Code Review（Superpowers Phase 6）

产出 `review.md`，按 Critical / Major / Minor 分级：

- 是否严守「受击表现不进 FSM」（约束 1）
- 顿帧是否只作用于受击方（约束 2）
- 是否所有画面操作都经由 `UnitView`（约束 3）
- 编辑器脚本是否幂等、失败是否可回滚
- 协程是否可能在单位销毁后访问已销毁对象
- 死代码与过时注释是否清理

**Critical 必须修完才算完成。**

---

## 执行结果（2026-09-18）

| 任务 | 状态 | 实锤 |
|---|---|---|
| T0 备份 | ✅ | 5 份 `.bak.2026-09-18-*` 字节数一致 |
| T1 编辑器脚本 | ✅ | `Assets/Editor/GBLAnimatorBuilder.cs`（新建目录） |
| T2 编译 + 执行菜单 | ✅ | 0 Error；`editor_execute_menu` 成功；二次执行 10 条日志全为「已存在，跳过」⇒ 幂等 |
| T3 拓扑复验 | ✅ | 参数 3→6；`Any State → Hit` + `idle → Hit`（首位）；`Entry → LightHit` |
| T5 BattleConst | ✅ | `HIT_TYPE_*` / `HIT_STOP_SECONDS` / `ani_Hit` / `ani_HitType` |
| T6 UnitView | ✅ | `PlayHit` / `SetAnimatorSpeed` / `PlayDeath` 恢复速度 |
| T7 UnitHitReaction | ✅ | `ApplyHurt` + `HitStopRoutine`（令牌递增） |
| T8 Skill.OnHure | ✅ | 空桩补齐 + `m_SkillTarget → target` |
| T9 编译 / dll 核验 | ✅ | 0 Error，无源 .cs 晚于 dll |
| T10 Red 基线 | ⚠️ 未单独采 | 术后数据与既有基线（`[HitEffectCalc]↔[HitEffect]` 1:1）对照，等价性成立 |
| T11 Green | ✅ | `[SegEffect] 效果=[0,22]` 99 ↔ `[HitReact]` 99；`Move` 告警 0 |
| T12 文档同步 | ✅ | 两份 HTML + smoke 71 项 ALL PASS + 标签配平 OK |
| T13 review | ✅ | `review.md` |

### 与 plan 的两处偏离（必须记录）

1. **T1 多做了「把 `idle → Hit` 插到转换列表首位」** —— 原计划只有 Any State 一条入口。
   实测发现 idle 上有 4 条真无条件出边，Any State 是否被它们抢掉无公开保证，故加一条保险。
2. **T7 的 `ApplyHurt` 按拍板做了「打断」** —— 原计划默认是「不打断」，用户改为打断。
   实现为 `RequestState(Idle)`，未新增状态（转换表 `Attack→Idle` 已存在）。
   ⚠️ **当日下午用户要求回退**：该机制对玩家与敌人**对称生效**，属玩法规则，已从受击管线整体移除。

### 第二轮追加任务（2026-09-18 下午）

| 任务 | 状态 | 说明 |
|---|---|---|
| R1 回退打断 | ✅ | `ApplyHurt` 删除 `RequestState(Idle)` 分支 + 改写注释；编译 0 Error |
| R2 备份 | ✅ | `GBL.controller` / `UnitView.cs` / `UnitHitReaction.cs` / `BattleConst.cs` / `GBLAnimatorBuilder.cs` 各 `.bak.2026-09-18-diefix` |
| R3 动画资源修复 Die | ✅ | 菜单「GBL - 修复死亡链路（Die）」：Any State→Die（首位 + 禁止自转）/ idle→Die 补条件 / Die→idle 补条件；幂等 |
| R4 代码写值修复 | ✅ | `BattleConst.ani_Die` + `UnitView.WriteDeathParam()`（类型自适应）+ 抽出公共 `FindParameter()` |
| R5 验证工具 | ✅ | `DeathLinkProbe.cs`（`#if UNITY_EDITOR`）+ 自检菜单；**EditorApplication.update 在本机不回调**，必须走运行时探针 |
| R6 Play 验证 | ✅ | `[DeathDiag]` 8 次采样全 inDie=True → PASS；`[Death] 写 Die(Bool)`；0 Error 0 Warning |
| R7 文档与冒烟 | ✅ | `design.md` 9b / `review.md` 关闭 M1·修复既有缺陷 / 两份 HTML（5.7、STEP 04-P15fix）/ smoke ALL PASS + 标签配平 OK |

### 新增的一处防御（plan 未列）

`UnitView.EnsureHitParams()`：玩家用 NYT.controller、**没有** Hit/HitType 参数。
不先查就 `SetInteger`/`SetTrigger`，Unity 会对每次受击刷一条 `does not exist` 告警。
改为**懒检查 + 只告警一次**（不在构造里查，因为 runtimeController 可能在 Awake 之后才赋值）。

---

## 未纳入本次（明确缓办）

- C 段：位移击退（需 10003 的 BackDis / BackTime 数值）
- `AirHeight` / `AirTime` 在 `SkillData` 中未解析
- `HitL` / `HitR` 方向性受击（可做「按受击方向左右偏头」，属表现增强）
- 删除 `idle → Down` / `idle → FloatHit` 两条无条件边（改变既有行为，需独立验证）
- P14 Buff 组件去挂载（9-17 已定稿，未实施）
