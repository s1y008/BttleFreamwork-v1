using System.Collections.Generic;
using UnityEngine;
using BattleFramework.Core;

/// <summary>
/// 战斗单位Buff组件：Buff事件订阅 / DoT结算 / 控制状态仲裁
/// 从CombatUnitController拆出的独立组件，由其Start时自动挂载（一个战斗单位一个）
/// 生命周期由CombatUnitController显式驱动：Setup绑定 → Tick每帧驱动 → Shutdown清理
/// </summary>
public class UnitBuffComponent : MonoBehaviour
{
    // ★ 硬控位已提升为公有组合标记 BuffType.HardControl（见 BuffType.cs「组合标记」区）：
    //   新增硬控种类时只改那一处枚举，FSM仲裁 / 免疫 / 驱散全部自动生效，
    //   不再需要在本文件再抄一份掩码（抄两份 = 两套真相源，加类型时必漏一处）

    /// <summary>
    /// 施加者反查表：CasterId(控制器GetInstanceID) → 控制器
    /// 用于DoT结算时按Buff.CasterId找回攻击者
    /// </summary>
    private static readonly Dictionary<int, CombatUnitController> s_ControllerById = new Dictionary<int, CombatUnitController>();

    // ★ v5 P10：原 `s_EmptyHitEffects`（空命中特效表）已删除 —— OnDamaged 第三参已由 List<int>
    //   改为 int，DoT 伤害直接传 0 表示「本段无受击特效」，不再需要占位用的空表。

    private CombatUnitController m_Owner;
    private int m_OwnerId;
    private CombatUnit m_Unit;
    private bool m_Subscribed;

    /// <summary>按CasterId查找战斗控制器（找不到返回null，如施加者已销毁）</summary>
    public static CombatUnitController FindControllerById(int casterId)
    {
        CombatUnitController controller;
        s_ControllerById.TryGetValue(casterId, out controller);
        return controller;
    }

    /// <summary>
    /// 初始化：绑定所有者与数据单位，注册施加者反查表并订阅Buff事件
    /// </summary>
    /// <param name="owner">所属战斗控制器</param>
    /// <param name="unit">战斗数据（可为null，后续通过Rebind补绑）</param>
    public void Setup(CombatUnitController owner, CombatUnit unit)
    {
        m_Owner = owner;
        m_OwnerId = owner.GetInstanceID();
        s_ControllerById[m_OwnerId] = owner;
        Rebind(unit);
    }

    /// <summary>
    /// 重绑数据单位（CurCombatUnit变更时调用）：退订旧单位、订阅新单位
    /// </summary>
    /// <param name="unit">新的战斗数据</param>
    public void Rebind(CombatUnit unit)
    {
        if (m_Subscribed && m_Unit != null)
        {
            BuffSystemPooled oldSystem = m_Unit.BuffSystem;
            oldSystem.OnBuffAdded   -= OnControlBuffAdded;
            oldSystem.OnBuffRemoved -= OnControlBuffRemoved;
            oldSystem.OnBuffTick    -= OnBuffTickDamage;
            m_Subscribed = false;
        }

        m_Unit = unit;
        if (m_Unit == null)
        {
            return;
        }

        BuffSystemPooled buffSystem = m_Unit.BuffSystem;
        buffSystem.OnBuffAdded   += OnControlBuffAdded;
        buffSystem.OnBuffRemoved += OnControlBuffRemoved;
        buffSystem.OnBuffTick    += OnBuffTickDamage;
        m_Subscribed = true;
    }

    /// <summary>
    /// 每帧驱动Buff系统（由CombatUnitController.Update调用，保证先于FSM Tick）
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (m_Unit != null)
        {
            m_Unit.BuffSystem.Update(deltaTime);
        }
    }

    /// <summary>
    /// 清空所有Buff（死亡时调用；OnBuffRemoved事件先于对象回收发出，回调内数据仍可读）
    /// </summary>
    public void ClearBuffs()
    {
        if (m_Unit != null)
        {
            m_Unit.BuffSystem.ClearAllBuffs();
        }
    }

    /// <summary>
    /// 清理：退订事件并清空所有Buff（销毁时由CombatUnitController.OnDestroy调用）
    /// </summary>
    public void Shutdown()
    {
        if (m_Unit != null)
        {
            BuffSystemPooled buffSystem = m_Unit.BuffSystem;
            buffSystem.OnBuffAdded   -= OnControlBuffAdded;
            buffSystem.OnBuffRemoved -= OnControlBuffRemoved;
            buffSystem.OnBuffTick    -= OnBuffTickDamage;
            buffSystem.ClearAllBuffs();
        }
        m_Subscribed = false;
    }

    private void OnDestroy()
    {
        //兜底清理：注销反查表、退订事件（正常流程由CombatUnitController.OnDestroy驱动Shutdown）
        s_ControllerById.Remove(m_OwnerId);
        if (m_Subscribed && m_Unit != null)
        {
            BuffSystemPooled buffSystem = m_Unit.BuffSystem;
            buffSystem.OnBuffAdded   -= OnControlBuffAdded;
            buffSystem.OnBuffRemoved -= OnControlBuffRemoved;
            buffSystem.OnBuffTick    -= OnBuffTickDamage;
            m_Subscribed = false;
        }
    }

    /// <summary>是否存在硬控Buff</summary>
    public bool HasHardControlBuff()
    {
        if (m_Unit == null)
        {
            return false;
        }
        //★ Tags 优先（数据驱动：CSV 写 HardControl 即可）；该行没声明 Tags 时退回位判定
        return m_Unit.BuffSystem.HasBuffWithTag(BuffTags.HardControl)
            || m_Unit.BuffSystem.HasAnyBuff(BuffType.HardControl);
    }

    /// <summary>是否存在沉默Buff</summary>
    public bool HasSilenceBuff()
    {
        if (m_Unit == null)
        {
            return false;
        }
        return m_Unit.BuffSystem.HasBuffWithTag(BuffTags.Silence)
            || m_Unit.BuffSystem.HasBuff(BuffType.Silenced);
    }

    /// <summary>该Buff是否映射到FSM控制状态（硬控->Stun，沉默->Silence）</summary>
    private bool IsControlBuff(Buff buff)
    {
        if (buff == null || buff._BuffData == null)
        {
            return false;
        }
        BuffData d = buff._BuffData;

        //★ Tags 优先（数据驱动）；该行没声明 Tags 时退回位判定
        //  （退回时一律用「命中位」不用「等值」：Type 是标签位图，组合位下等值会漏判）
        if (d.Tags != null && d.Tags.Length > 0)
        {
            return d.HasTag(BuffTags.HardControl) || d.HasTag(BuffTags.Silence);
        }
        return (BuffType.HardControl & d.Type) != 0 || (BuffType.Silenced & d.Type) != 0;
    }

    // =================控制状态仲裁================

    /// <summary>控制Buff施加：驱动FSM切入对应控制态</summary>
    private void OnControlBuffAdded(Buff buff)
    {
        if (buff == null || buff._BuffData == null)
        {
            return;
        }
        if (IsControlBuff(buff))
        {
            SyncControlStateFromBuffs();
        }
    }

    /// <summary>控制Buff移除/到期：重新仲裁控制态</summary>
    private void OnControlBuffRemoved(Buff buff)
    {
        if (buff == null || buff._BuffData == null)
        {
            return;
        }
        if (IsControlBuff(buff))
        {
            SyncControlStateFromBuffs();
        }
    }

    /// <summary>
    /// 按当前Buff标志位仲裁应处的控制状态并请求切换。
    /// Buff是控制效果的唯一真相源：时长/叠加/免疫/驱散均由Buff系统管理，
    /// FSM的Stun/Silence状态只是对Buff控制标志的投影，不再各自倒计时。
    /// </summary>
    private void SyncControlStateFromBuffs()
    {
        if (m_Owner == null || m_Owner.IsDead || !m_Owner.HasStateMachine)
        {
            return;
        }

        CombatState current = m_Owner.CurrentState;

        if (HasHardControlBuff())
        {
            if (current != CombatState.Stun)
            {
                m_Owner.RequestState(CombatState.Stun);
            }
            return;
        }

        if (HasSilenceBuff())
        {
            if (current != CombatState.Silence)
            {
                m_Owner.RequestState(CombatState.Silence);
            }
            return;
        }

        // 无控制Buff：若当前仍处于控制态，回退待机
        if (current == CombatState.Stun || current == CombatState.Silence)
        {
            m_Owner.RequestState(CombatState.Idle);
        }
    }

    // =================DoT结算================

    /// <summary>
    /// Buff持续伤害Tick落地
    /// 施加者存活时走完整伤害管线（含受击特效与仇恨）；施加者已销毁时仅结算数值
    /// </summary>
    /// <param name="tickBuff">触发中的Buff</param>
    private void OnBuffTickDamage(Buff tickBuff)
    {
        if (m_Owner == null || m_Owner.IsDead || m_Unit == null)
        {
            return;
        }

        int damage = (int)(tickBuff._BuffData.EffectValue * tickBuff.StackCount);
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
#endif

        CombatUnitController attacker = FindControllerById(tickBuff.CasterId);
        if (attacker != null)
        {
            //施加者仍在场：走完整伤害结算
            // ★ v5 P10：第三参由「空特效表」改为 int 0 —— 语义更直白（DoT 伤害不播受击特效）。
            //   改造前传空 List 其实是无效的：OnDamaged 根本不读该参数，真正决定特效的是
            //   反查 attacker.m_CurSkill，反而会让 DoT 误播攻击者上一轮残留段号的受击特效。
            m_Owner.OnDamaged(attacker, damage, 0);
        }
        else
        {
            //施加者已销毁：只结算数值，不触发特效与仇恨
            m_Unit.HP = Mathf.Max(0, m_Unit.HP - damage);
            if (m_Unit.HP <= 0)
            {
                m_Owner.RequestState(CombatState.Dead);
            }
        }
    }
}
