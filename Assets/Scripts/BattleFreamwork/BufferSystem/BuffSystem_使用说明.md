# BuffSystem 游戏增益/减益系统 · 使用说明

> 位置:`Assets/Scripts/BattleFreamwork/BufferSystem/`(配置解析在 `BattleData/BuffData.cs`)
> 命名空间:`BattleFramework.Core`
> 适用于:Unity 2021+ / .NET Standard 2.1
> 核心思想:`ulong` **位枚举** O(1) 状态检测 + **对象池**零 GC + **CSV 配置驱动**(所有 Buff 参数来自 `Resources/Config/BufferData.csv`)

---

## 目录

1. [文件清单与分工](#1-文件清单与分工)
2. [核心设计:配置驱动的 BuffSystemPooled](#2-核心设计配置驱动的-buffsystempooled)
3. [一分钟上手](#3-一分钟上手)
4. [BuffType 位枚举详解](#4-bufftype-位枚举详解)
5. [字段说明:Buff 与 BuffData](#5-字段说明buff-与-buffdata)
6. [公共 API 速查](#6-公共-api-速查)
7. [实战教程](#7-实战教程)
8. [战斗链路接线(已内置)](#8-战斗链路接线已内置)
9. [注意事项与常见坑](#9-注意事项与常见坑)
10. [性能说明](#10-性能说明)
11. [常见问题 FAQ](#11-常见问题-faq)

---

## 1. 文件清单与分工

| 文件 | 作用 | 是否需要改动 |
|------|------|----------|
| `BuffType.cs` | Buff 类型位枚举(46 种预定义 + 组合标记)与分类 | 可按需扩展 |
| `Buff.cs` | 单个 Buff 的运行时载体(剩余时间/层数/施加者等) | 一般不改 |
| `BuffSystemPooled.cs` | **唯一的 Buff 管理系统**(零 GC),全文档都围绕它 | 直接用即可 |
| `BuffSystemExample.cs` | 可运行的交互示例(挂到空物体点 Play) | 仅演示 |
| `../BattleData/BuffData.cs` | CSV 配置解析(`BuffMsg`)+ 配置数据结构(`BuffData`) | 调表不改码 |
| `../../../Resources/Config/BufferData.csv` | **Buff 配置表**(全部参数从这里来) | 策划常改 |

> 运行核心(`BuffType/Buff/BuffSystemPooled/BuffData`)是**纯 C#**,不依赖 MonoBehaviour,逻辑可被任意系统调用(角色、怪物、单位、甚至服务端逻辑);`BuffSystemExample.cs` 与战斗接线部分使用了 UnityEngine。

---

## 2. 核心设计:配置驱动的 BuffSystemPooled

> 早期版本提供过 `AddBuff(type, id, name, duration, ...)` 参数版与 `BuffPool` 类,现已全部移除:**现行 API 只有 `AddBuff(BuffType type, int casterId)`,一切 Buff 参数从 CSV 读取**。

两个固定搭配:

```csharp
using BattleFramework.Core;

// 池:项目统一使用 ClassObjectPool<Buff>(构造时预热)
var pool = new ClassObjectPool<Buff>(200);

// 每个角色一个 BuffSystemPooled,共享上面的池
var bs = new BuffSystemPooled(ownerId: 1001, buffPool: pool);
// buffPool 可省略,系统会自动兜底一个内部池(容量32),但多角色建议显式共享
```

**战斗单位(`CombatUnit` 派生的角色/怪物)无需手动创建**——数据层已内置懒加载的 `BuffSystem` 属性与全局共享池,Controller 已自动驱动与清理(见[第 8 节](#8-战斗链路接线已内置))。

---

## 3. 一分钟上手

```csharp
using BattleFramework.Core;
using UnityEngine;

public class PlayerBuffDemo : MonoBehaviour
{
    private BuffSystemPooled _bs;

    void Awake()
    {
        _bs = new BuffSystemPooled(ownerId: 0, buffPool: new ClassObjectPool<Buff>(200));

        // 订阅事件(UI/特效/表现层从这里接)
        _bs.OnBuffAdded   += OnBuffAdded;
        _bs.OnBuffRemoved += OnBuffRemoved;
        _bs.OnBuffTick    += OnBuffTick;
    }

    void Update() => _bs.Update(Time.deltaTime);   // 每帧必须调用

    void OnDestroy()
    {
        _bs.ClearAllBuffs();
        _bs.OnBuffAdded   -= OnBuffAdded;
        _bs.OnBuffRemoved -= OnBuffRemoved;
        _bs.OnBuffTick    -= OnBuffTick;
    }

    // 加 Buff:只传类型与施加者,参数全部来自 CSV
    // 例:加攻击提升(对应配置行 AttackBoost,EffectValue=50,Duration=10)
    void AddAttackBuff(int casterId) => _bs.AddBuff(BuffType.AttackBoost, casterId);

    private void OnBuffAdded(Buff b)   => Debug.Log($"获得 {b._BuffData.Name}");
    private void OnBuffRemoved(Buff b) => Debug.Log($"失去 {b._BuffData.Name}");
    private void OnBuffTick(Buff b)    => Debug.Log($"{b._BuffData.Name} 触发,伤害 {b._BuffData.EffectValue}");
}
```

查状态全是位运算,O(1):

```csharp
bool stun    = _bs.HasBuff(BuffType.Stunned);
bool locked  = _bs.HasAnyBuff(BuffType.Stunned | BuffType.Frozen | BuffType.Sleeping);
bool buffed  = _bs.HasPositiveBuff();
bool debuffed= _bs.HasNegativeBuff();
bool canAtk  = _bs.CanAttack();
bool canMove = _bs.CanMove();
bool canCast = _bs.CanCast();
```

**调参数改 CSV,不改代码**:编辑 `Resources/Config/BufferData.csv` 对应行即可(列布局见 [5.2](#52-buffdata配置字段csv-14-列))。

---

## 4. BuffType 位枚举详解

定义于 `BuffType.cs`,基类型 `ulong`,共可用 64 位,当前占用 46 位,从 `1UL << 46` 起可自由扩展。

### 4.1 增益类(位 0–15)

| 值 | 名称 | 含义 |
|----|------|------|
| `1<<0` | `AttackBoost` | 攻击力提升 |
| `1<<1` | `DefenseBoost` | 防御力提升 |
| `1<<2` | `SpeedBoost` | 速度提升 |
| `1<<3` | `CriticalBoost` | 暴击率提升 |
| `1<<4` | `HealthRegen` | 生命回复 |
| `1<<5` | `ManaRegen` | 魔法回复 |
| `1<<6` | `Invincible` | 无敌 |
| `1<<7` | `Shield` | 护盾 |
| `1<<8` | `Invisible` | 隐身 |
| `1<<9` | `Berserk` | 狂暴 |
| `1<<10` | `Lifesteal` | 吸血 |
| `1<<11` | `Thorns` | 反伤 |
| `1<<12` | `DodgeBoost` | 闪避提升 |
| `1<<13` | `ExpBoost` | 经验加成 |
| `1<<14` | `GoldBoost` | 金币加成 |
| `1<<15` | `ControlImmune` | 免疫控制 |

### 4.2 减益类(位 16–31)

| 值 | 名称 | 含义 |
|----|------|------|
| `1<<16` | `Poisoned` | 中毒 |
| `1<<17` | `Burning` | 燃烧 |
| `1<<18` | `Frozen` | 冰冻 |
| `1<<19` | `Stunned` | 眩晕 |
| `1<<20` | `Silenced` | 沉默 |
| `1<<21` | `Disarmed` | 缴械 |
| `1<<22` | `Blinded` | 致盲 |
| `1<<23` | `Slowed` | 减速 |
| `1<<24` | `Weakened` | 虚弱 |
| `1<<25` | `Bleeding` | 流血 |
| `1<<26` | `Cursed` | 诅咒 |
| `1<<27` | `Feared` | 恐惧 |
| `1<<28` | `Taunted` | 嘲讽 |
| `1<<29` | `HealingBlock` | 禁疗 |
| `1<<30` | `ArmorBreak` | 破甲 |
| `1<<31` | `Corroded` | 腐蚀 |

### 4.3 特殊状态(位 32–45)

`Charmed(1<<32)`、`Polymorphed(1<<33)`、`Petrified(1<<34)`、`Sleeping(1<<35)`、`Confused(1<<36)`、`Enraged(1<<37)`、`Bloodlust(1<<38)`、`Divine(1<<39)`、`Shadow(1<<40)`、`Flying(1<<41)`、`Stealth(1<<42)`、`Rage(1<<43)`、`Focused(1<<44)`、`Frenzy(1<<45)`。

### 4.4 预定义组合标记(系统自动使用)

| 组合 | 内容 |
|------|------|
| `BuffType.AllPositive` | 全部 16 个增益 → 用于 `HasPositiveBuff()` |
| `BuffType.AllNegative` | 全部 16 个减益 → 用于 `HasNegativeBuff()` 与**无敌免疫判断** |
| `BuffType.AllControlEffects` | 眩晕/冰冻/恐惧/沉默/缴械/致盲/嘲讽/魅惑/变形/石化/睡眠/混乱 → 用于 `IsControlled()` 与**控制免疫判断** |
| `BuffType.AllDamageOverTime` | 中毒/燃烧/流血/腐蚀 → 可自行判断持续伤害 |
| `BuffType.AllMovementImpair` | 冰冻/眩晕/减速/石化/睡眠 → 用于 `CanMove()` |

### 4.5 `BuffCategory`(分类枚举,用于驱散和归类)

`Positive` / `Negative` / `Control` / `Special` 四类。

> ⚠️ **注意:** `BuffCategory` 只影响**归类与驱散过滤**,不影响上面那些位运算方法——位运算方法(如 `IsInvincible` 免疫、`IsControlled`)只看 `BuffType` 的二进制位。

---

## 5. 字段说明:Buff 与 BuffData

### 5.1 Buff(运行时字段)

`Buff.cs` —— 每个激活的 Buff 对应一个池化实例,**参数本体在 `_BuffData` 里**:

| 字段 | 类型 | 默认 | 说明 |
|------|------|------|------|
| `_BuffData` | `BuffData` | null | **配置数据引用**(类型/时长/效果值等全在这里) |
| `RemainingTime` | `float` | -1 | 剩余时间,由系统扣减 |
| `StackCount` | `int` | 1 | 当前叠加层数 |
| `NextTickTime` | `float` | 0 | 下次触发时间点(系统维护) |
| `CasterId` | `int` | 0 | 施加者 ID(战斗链路里是施加者 GetInstanceID) |
| `IsActive` | `bool` | **false** | 是否激活(出池由 AddBuff 置 true,新建≠激活) |
| `CreatedTime` | `float` | 0 | 创建时间点(相对系统进入活跃期起累计的秒数) |

辅助方法(对已回收的失效引用安全——`_BuffData==null` 时 `IsExpired()` 返回 true、`ShouldTick()` 返回 false):

```csharp
bool isExpired = buff.IsExpired();            // Duration>=0 且 RemainingTime<=0
bool needTick   = buff.ShouldTick(curTime);   // 当前时间 >= NextTickTime 且 TickInterval>0
```

### 5.2 BuffData(配置字段,CSV 14 列)

`Resources/Config/BufferData.csv`,**必须 CRLF 行尾**:

```
ID,BuffType,Name,BuffCategory,Duration,MaxStack,EffectValue,EffectPercent,TickInterval,Dispellable,Stackable,IconName,EffectName,Des
1001,AttackBoost,增加攻击,Positive,10,1,50,0,0,0,0,dsaf,dflkafhgjlkdja,333
1003,Bleeding,流血,Negative,5,1,10,0,1,0,0,diosfahjlk,gfgarer,rerw
1004,Poisoned,中毒,Negative,8,3,5,0,2,1,1,poison_icon,poison_effect,每2秒扣5血·可叠3层
```

| 列 | 字段 | 约束/说明 |
|----|------|-----------|
| 1 | `ID` | 唯一,重复行加载时报错跳过 |
| 2 | `BuffType` | 枚举名,需与 `BuffType.cs` 一致 |
| 3 | `Name` | 显示名 |
| 4 | `BuffCategory` | Positive/Negative/Control/Special |
| 5 | `Duration` | 秒;**-1=永久,>0=限时,0 非法**(加载时拒绝) |
| 6 | `MaxStack` | 最大叠加层数 |
| 7 | `EffectValue` | 数值型效果(如 +50 攻击 / DoT 每跳伤害) |
| 8 | `EffectPercent` | 百分比型效果(0.2 = +20%) |
| 9 | `TickInterval` | 间隔触发秒;**0 = 永不 Tick**(DoT 必填 >0) |
| 10 | `Dispellable` | 1/0,能否被驱散 |
| 11 | `Stackable` | 1/0,同类型能否叠加 |
| 12 | `IconName` | 图标名(UI 用) |
| 13 | `EffectName` | 特效名 |
| 14 | `Des` | 描述 |

加载时自动校验:非法 Duration、重复 Id、重复 BuffType 的行会被**拒绝并打 Error 日志**(不再抛异常崩溃);`GetData` 查不到返回 null 由调用方判空。

---

## 6. 公共 API 速查

以下全部是 `BuffSystemPooled` 的公开接口。

### 6.1 状态判断(位运算,O(1))

| 方法 | 返回 | 说明 |
|------|------|------|
| `HasBuff(BuffType t)` | bool | 是否**具有**该 Buff |
| `HasAnyBuff(BuffType ts)` | bool | 是否具有其中**任意一个**(可传组合位) |
| `HasAllBuffs(BuffType ts)` | bool | 是否**同时具有全部** |
| `HasPositiveBuff()` | bool | 是否有任何增益 |
| `HasNegativeBuff()` | bool | 是否有任何减益 |
| `IsControlled()` | bool | 是否被控制(见 AllControlEffects) |
| `CanMove()` | bool | 能否移动(不受冻结/眩晕/减速等) |
| `CanAttack()` | bool | 能否攻击(不受缴械/眩晕/冰冻/睡眠) |
| `CanCast()` | bool | 能否施法(不受沉默/眩晕/冰冻/睡眠) |
| `IsInvincible()` | bool | 是否无敌 |
| `ActiveBuffFlags` | `BuffType` | 当前全部激活标志的合集 |
| `ActiveBuffCount` | int | 当前激活 Buff 个数 |

### 6.2 查询

| 方法 | 返回 | 说明 |
|------|------|------|
| `ActiveBuffs` | `IReadOnlyList<Buff>` | 全部激活 Buff(只读) |
| `GetBuffs(BuffType t)` | `List<Buff>` | 该类型的所有 Buff(有分配,冷路径) |
| `GetBuffsByCategory(c)` | `List<Buff>` | 该分类的所有 Buff(有分配) |
| `GetBuffsByCategory(c, List<Buff>)` | void | **填充式重载,零 GC**(调用方传复用 List) |
| `GetBuffById(int id)` | `Buff`(可空) | 按配置 Id 查 |
| `GetBuffStackCount(BuffType t)` | int | 该类型层数(无则 0) |
| `GetBuffRemainingTime(BuffType t)` | float | 剩余秒数(无则 0) |
| `CalculateStatBonus(BuffType t)` | float | 数值加和 = Σ EffectValue×StackCount |
| `CalculateStatMultiplier(BuffType t)` | float | 百分比连乘 = Π(1 + EffectPercent×StackCount) |
| `GetDebugInfo()` | string | 调试输出 |

### 6.3 管理

| 方法 | 返回 | 说明 |
|------|------|------|
| `AddBuff(BuffType type, int casterId)` | Buff(可空) | **唯一入口**;内部查 CSV 配置,池取对象;免疫/已有更强/配置缺失/组合位 → null |
| `RemoveBuff(BuffType t)` | bool | 移除该类型**所有** Buff(仅支持单一类型) |
| `RemoveBuffById(int id)` | bool | 按配置 Id 精确移除 |
| `DispelNegativeBuffs(int count = 1)` | int | 驱散前 N 个 `Dispellable && Category==Negative` |
| `DispelAllBuffs()` | int | 驱散全部可驱散 Buff |
| `ClearAllBuffs()` | void | 强制清空全部(死亡/销毁时用) |
| `Update(float dt)` | void | **每帧调用**,扣时间、过期移除、触发 Tick |

`AddBuff` 返回 null 的情况(看 Console 日志区分原因):
- 传了组合位或 `None`(编程错误,打 Error)
- 配置缺失或 Type 列与行键不一致(打 Error)
- 免疫命中(不打日志)
- 同类型已有且无法叠加/刷新(不打日志,属正常吞并)

### 6.4 事件(表现层从这里接)

```csharp
event Action<Buff> OnBuffAdded;    // 添加成功(免疫失败不触发)
event Action<Buff> OnBuffRemoved;  // 移除/过期/被驱散(发出后才回收对象)
event Action<Buff> OnBuffUpdated;  // 叠加层数变化/刷新时间
event Action<Buff> OnBuffTick;     // 每次 Tick(中毒跳伤害等)
```

> ⚠️ **引用生命周期**:事件回调内读 `b._BuffData` 是安全的;回调返回后 Buff 被归还对象池并重置(`_BuffData=null`),**不要跨帧持有**。需要长期记忆请记 `(BuffType, Id)`,用 `GetBuffById` 现查。

### 6.5 对象池(ClassObjectPool&lt;Buff&gt;)

| 成员 | 说明 |
|------|------|
| `ClassObjectPool<Buff>(int maxcount)` | 构造并**预热** maxcount 个对象 |
| `Spawn(bool creatIfPoolEmpty)` | 取对象(空则按需 new) |
| `Recycle(T obj)` | 归还(池满拒绝;编辑器下带**重复回收检测**) |

战斗链路的共享池由 `CombatUnit` 内部静态持有(容量 200,首次使用时创建),业务无需自建。

---

## 7. 实战教程

### 7.1 战斗属性集成(攻击力计算)

Buff 只负责"标记 + 提供加成系数",真正的数值计算放在角色的属性方法里。**推荐"事件触发 + 全量重算"模式**(幂等,加减严格对称,叠加自动生效):

```csharp
private float m_BaseAtk;   // Start 时记录基础值

private void OnBuffChanged(Buff b)   // 挂到 Added/Updated/Removed 三个事件
{
    float bonus = _bs.CalculateStatBonus(BuffType.AttackBoost);      // Σ EffectValue×StackCount
    float mult  = _bs.CalculateStatMultiplier(BuffType.AttackBoost); // Π(1+EffectPercent×StackCount)
    if (_bs.HasBuff(BuffType.Weakened)) mult *= 0.7f;
    atk = m_BaseAtk + bonus;   // 示例:只用加值;需要乘区就乘 mult
}
```

### 7.2 行为控制(能不能动/能不能打)

战斗代码入口处统一问 BuffSystem:

```csharp
void TryAttack(Character target)
{
    if (!_bs.CanAttack()) { PlayBlockedFx(); return; }   // 眩晕/缴械/冰冻等
    if (!_bs.IsControlled()) { PlayBlockedFx(); return; } // 通用控制
    // ...实际攻击逻辑
}

void Move(Vector3 dir)
{
    if (!_bs.CanMove()) return;                          // 冻结/减速/眩晕
    transform.position += dir * GetSpeed() * Time.deltaTime;
}
```

### 7.3 持续伤害(Tick)

CSV 里给 `TickInterval > 0`,在 `OnBuffTick` 里结算:

```csharp
// CSV 配置(中毒:8秒/每2秒一跳/每跳5伤/可叠3层):
// 1004,Poisoned,中毒,Negative,8,3,5,0,2,1,1,poison_icon,poison_effect,每2秒扣5血·可叠3层

_bs.AddBuff(BuffType.Poisoned, attackerId);   // 重复调即叠层

private void OnBuffTick(Buff b)
{
    switch (b._BuffData.Type)
    {
        case BuffType.Poisoned:
        case BuffType.Burning:
        case BuffType.Bleeding:
        case BuffType.Corroded:
            TakeDamage(b._BuffData.EffectValue * b.StackCount, b.CasterId); // 每层结算
            break;
        case BuffType.HealthRegen:
            Heal(b._BuffData.EffectValue * b.StackCount);
            break;
    }
}
```

### 7.4 叠加与刷新规则(同类型第二个 Buff 加进来时)

1. **免疫** → 直接失败(见 7.5)。
2. 若 `CSV配置 Stackable==1 && existing.StackCount < MaxStack`
   → `StackCount++`,`RemainingTime` 重置为 Duration,触发 `OnBuffUpdated`。
3. 否则若 `Duration > existing.RemainingTime` → 刷新剩余时间,触发 `OnBuffUpdated`。
4. 否则 → 不生效,返回 null(较短/更弱的重复施加被吞掉)。

叠加效果自动反映在 `CalculateStatBonus/Multiplier`(×StackCount)与 `OnBuffTick`(×StackCount)里,表现层订阅 `OnBuffUpdated` 重算即可。

### 7.5 免疫规则(自动判断)

`AddBuff` 内部会先做免疫检查,命中直接拒绝:

| 已有 Buff | 会免疫的 |
|-----------|----------|
| `ControlImmune`(免疫控制) | 所有 `BuffType.AllControlEffects` 成员(眩晕/冰冻/恐惧/沉默/缴械/致盲/嘲讽/魅惑/变形/石化/睡眠/混乱) |
| `Invincible`(无敌) | 所有 `BuffType.AllNegative` 成员(16 种减益) |

### 7.6 驱散

```csharp
// 净化:驱散 3 个减益(只清 CSV 里 Dispellable=1 且 Category==Negative 的)
int n = _bs.DispelNegativeBuffs(count: 3);

// 驱散一切可驱散的(会清掉增益,慎用)
int all = _bs.DispelAllBuffs();

// 无条件的全清(连不可驱散也清,Boss 转阶段/死亡时用)
_bs.ClearAllBuffs();
```

> 想让某个减益**不能被净化**(如硬控、诅咒),CSV 该行 `Dispellable` 列配 0。

### 7.7 事件驱动表现层(特效 / UI / 音效)

```csharp
private void OnBuffAdded(Buff b)
{
    if (!string.IsNullOrEmpty(b._BuffData.EffectName)) PlayFx(b._BuffData.EffectName);
    if (b._BuffData.Type == BuffType.Invisible) SetVisible(false);
}

private void OnBuffRemoved(Buff b)
{
    if (b._BuffData.Type == BuffType.Invisible) SetVisible(true);
    RemoveIcon(b._BuffData.Id);   // 回调返回后 b 失效,这里只能同步用
}

private void OnBuffUpdated(Buff b) => RefreshIcon(b._BuffData.Id, b.StackCount, b.RemainingTime);
```

### 7.8 永久 Buff(持续到被移除)

CSV 该行 `Duration` 列配 **-1** 即永久:

```csharp
_bs.AddBuff(BuffType.Flying, casterId);   // 前提:CSV 有 Flying 行且 Duration=-1
// 某条件达成后移除:
_bs.RemoveBuff(BuffType.Flying);
```

### 7.9 新增一种 Buff 的完整流程

1. `BuffType.cs` 枚举加一位(从 `1UL << 46` 起);若属增益/减益/控制,同步加进对应组合标记;
2. `BufferData.csv` 加一行(注意 **CRLF 行尾、14 列、Duration 只能 -1 或 >0**);
3. 代码里 `AddBuff(新类型, casterId)` —— 无需改任何管理代码。

未配置的类型调 `AddBuff` 会打 Error 并返回 null,**不会崩溃**。

### 7.10 BuffSystemExample.cs 交互演示

把 `BuffSystemExample.cs` 挂到场景任意空 GameObject 上,点 Play:

| 按键 | 作用 |
|------|------|
| `1` | 添加攻击 Buff |
| `2` | 添加中毒 Buff |
| `3` | 添加眩晕 Buff(未配置 → Console 报 Error,返回 null,**验证配置缺失防护**) |
| `C` | 驱散减益 |
| `空格` | 打印当前 Buff 与状态 |

屏幕左上角实时显示 Buff 数量与可动/可打/可施法判断。

---

## 8. 战斗链路接线(已内置)

批次五接线后,**`CombatUnit` 派生的战斗单位开箱即用**,无需手写任何接入代码:

```
技能命中(Skill.AddBuff)
  → unit.OnAddBuff(attacker, skill, buffid)          // CombatUnitController 实现
      → ConfigManager.GetBuffData(buffid)            // 按配置Id查CSV
      → CurCombatUnit.BuffSystem.AddBuff(type, attacker.GetInstanceID())

每帧 CombatUnitController.Update
  → CurCombatUnit.BuffSystem.Update(dt)              // 自动驱动

DoT 结算(自动订阅 OnBuffTick)
  → 施加者在场:OnDamaged(attacker, 伤害, 1f, 空特效表)   // 完整伤害管线(特效/仇恨)
  → 施加者已销毁:仅扣数值,致死仍走 RequestState(Dead)

死亡(OnDead)/销毁(OnDestroy)
  → ClearAllBuffs() + 事件退订 + 施加者反查表注销
```

要点:
- `CombatUnit.BuffSystem` 是懒属性,首次访问创建(全局共享 200 容量池,只建一次)——纯数据层也能读加成(如 `Warrior.Damage` 里叠 `CalculateStatBonus`);
- 施加者反查表 `UnitBuffComponent.FindControllerById(casterId)` 静态公开,供 DoT 结算找回攻击者;
- 想手动给某单位挂 Buff:`unit.CurCombatUnit.BuffSystem.AddBuff(type, attacker.GetInstanceID())` 或走 `OnAddBuff`。

---

## 9. 注意事项与常见坑

1. **不要长期持有 `Buff` 引用。** Buff 被移除/过期后归还对象池并重置字段(`_BuffData=null`),跨帧记忆请只记 `(BuffType, Id)`,用 `GetBuffById` 现查。`IsExpired()/ShouldTick()` 对失效引用安全(视为过期/不跳),但读 `_BuffData` 会 NRE。
2. **`OnBuffRemoved` 里读到的对象是有效的**(事件先于回收发出),但仅限回调内同步使用。
3. **`Update` 必须每帧调**,否则时间不流逝、Buff 永不过期、Tick 永不触发(战斗单位已由 Controller 自动驱动;自建系统要自己调)。无激活 Buff 期间内部时钟自动归零,长时间对局无精度漂移。
4. **单帧大 deltaTime 只会触发一次 Tick**,帧率不稳的 Tick 类 Buff 建议限制 dt 或做补偿。
5. **`AddBuff` 只接受单一 BuffType**:传组合位(如 `AllDamageOverTime`)或 `None` 会打 Error 返回 null。查状态(`HasAnyBuff` 等)可以传组合位。
6. **事件重入安全**:回调里可以再调 `AddBuff/RemoveBuff/ClearAllBuffs`,移除请求会被延后到当前遍历结束后统一清算,不会跳帧或重复回收。
7. **CSV 三条铁律**:CRLF 行尾(LF 会导致整表静默加载失败)、Duration 只能 -1 或 >0(0 会被拒绝加载)、DoT 必须 TickInterval>0(0 = 永不触发)。
8. **配置缺失不崩溃**:未配置的类型 AddBuff 返回 null 并打 Error;重复 Id/Type、非法 Duration 的行加载时被跳过——启动时留意 Console。
9. **分类别写错**:`DispelNegativeBuffs` 认的是 `Category == Negative`,而免疫/控制判断认的是 `BuffType` 位。让"眩晕"可被净化就把它 Category 配成 Negative。
10. **永久 Buff 会一直占一个池对象**,大量常驻被动建议用位标志而非走池。
11. **命名空间**:需 `using BattleFramework.Core;`。

---

## 10. 性能说明

| 操作 | 复杂度 / 量级 |
|------|----------------|
| `HasBuff / HasAnyBuff / CanMove / ...` | O(1),纯位运算,~1–5 ns |
| `AddBuff`(池有货) | 无 GC 分配 |
| `RemoveBuff / Dispel / Clear / Update(dt)` | O(活跃 Buff 数),循环实现,无 LINQ 分配 |
| `GetBuffsByCategory(c, buffer)` 填充式 | 零 GC(复用调用方 List) |

- 46 种 Buff 全部存在一个 `ulong` 里,单次 `Update` 对几百个单位 × 十几个 Buff 也足够用在手机端。
- 事件回调里避免高频 `Debug.Log`(字符串插值 + 控制台写入远比一次属性重算贵)。

---

## 11. 常见问题 FAQ

**Q1:Unity 里报 "找不到 Xunit"?**
A:那是测试文件,已移出到工程根的 `BuffSystem.Tests\`(Unity 不编译)。Assets 内的运行时代码不需要任何外部库。

**Q2:我要加一个新 Buff(比如"格挡")怎么办?**
A:见 [7.9](#79-新增一种-buff-的完整流程):枚举加一位 → CSV 加一行 → 直接 AddBuff。记得同步组合标记,否则 `HasPositiveBuff()` 之类判断不到。

**Q3:以前那个参数版 `AddBuff(type, id, name, duration, ...)` 和 `BuffPool` 呢?**
A:已删除。现行 API 只有 `AddBuff(BuffType, int casterId)`,参数全部来自 CSV;池统一用 `ClassObjectPool<Buff>`。本目录唯一入口是 `BuffSystemPooled`。

**Q4:同一个 Buff 不同来源(不同 CasterId)能各自独立吗?**
A:默认策略是同 `Type` 共享一条(叠加/刷新)。CasterId 只用于 DoT 结算时找回施加者,不参与去重。需要按来源独立请设计不同的 BuffType 位。

**Q5:数值怎么算成最终攻击力/最终速度?**
A:Buff 系统提供 `CalculateStatBonus`(加值)与 `CalculateStatMultiplier`(乘区),公式放角色属性类里组合(见 7.1,推荐事件触发全量重算)。

**Q6:Buff 加上了但不掉血/不生效?**
A:按顺序查:①Console 有没有配置缺失 Error;②CSV 该行 `TickInterval` 是否 >0(DoT 必填);③`Duration` 是否配了 0(非法,整行被拒);④行尾是否 CRLF;⑤订阅的是不是 `OnBuffTick`(加成类走 `OnBuffAdded/Updated` 重算)。

**Q7:怎么在暂停菜单 / 死亡时停掉所有 DoT?**
A:暂停时别调 `Update`;战斗单位死亡/销毁已自动 `ClearAllBuffs()` 并退订;自建系统请在 `OnDestroy` 里 `ClearAllBuffs()` + 事件退订。

---

*本文档与 `Assets/Scripts/BattleFreamwork/BufferSystem/` 下当前源码逐一对齐;若改动源码请同步更新本文档对应章节。*
