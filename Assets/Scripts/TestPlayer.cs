using System;
using System.Collections;
using System.Collections.Generic;
using BattleFramework.Core;
using UnityEngine;

public class TestPlayer : MonoBehaviour
{
    BuffSystemPooled  buff;

    public int hp = 5000;
    public TestEnemy enemy;

    /// <summary>
    /// 玩家的攻击力
    /// </summary>
    public float atk = 10;

    public float speed = 3;

    /// <summary>
    /// 未受Buff影响的基础攻击力
    /// </summary>
    private float m_BaseAtk;

    /// <summary>
    /// 未受Buff影响的基础速度
    /// </summary>
    private float m_BaseSpeed;

    public void CreadBuff(ClassObjectPool<Buff> buffPool)
    {
        buff = new BuffSystemPooled(1, buffPool);
    }

    private void Start()
    {
        //记录基础属性，Buff变动时以此为基准重算
        m_BaseAtk = atk;
        m_BaseSpeed = speed;

        //添加/叠加/移除统一重算，保证加减严格对称
        buff.OnBuffAdded += RefreshStatsByBuff;
        buff.OnBuffUpdated += RefreshStatsByBuff;
        buff.OnBuffRemoved += RefreshStatsByBuff;
        //持续伤害Buff按Tick触发扣血
        buff.OnBuffTick += OnBuffTickDamage;

        //开局即给玩家添加流血与中毒（验证持续伤害Tick）
        buff.AddBuff(BuffType.Bleeding, 1);
        buff.AddBuff(BuffType.Poisoned, 1);
    }

    /// <summary>
    /// 持续伤害Buff按间隔触发扣血
    /// ChangHP传入正伤害（内部是hp-=伤害），叠加层数放大每跳伤害
    /// </summary>
    /// <param name="tickBuff">触发中的Buff</param>
    private void OnBuffTickDamage(Buff tickBuff)
    {
        ChangHP((int)(tickBuff._BuffData.EffectValue * tickBuff.StackCount));
    }

    /// <summary>
    /// 根据Buff系统当前状态重算属性
    /// 按Buff类型各算各的，增删/叠加/刷新全走同一条重算路径，不会互相污染
    /// </summary>
    /// <param name="changedBuff">发生变动的Buff</param>
    private void RefreshStatsByBuff(Buff changedBuff)
    {
        atk = m_BaseAtk + buff.CalculateStatBonus(BuffType.AttackBoost);
        speed = m_BaseSpeed + buff.CalculateStatBonus(BuffType.SpeedBoost);
        Debug.LogError($"Buff变动: {changedBuff._BuffData.Name} → atk={atk} speed={speed}");
    }
    
    
    /// <summary>
    /// 伤害见血
    /// </summary>
    /// <param name="demage"></param>
    public void ChangHP(int demage)
    {
        hp -= demage;
        Debug.LogError(hp);
    }

    // Update is called once per frame
    void Update()
    {
        buff.Update(Time.deltaTime);
        if (Input.GetKeyDown(KeyCode.A))
        {
            buff.AddBuff(BuffType.AttackBoost, 1);
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            //enemy.ChangHP(atk);
            buff.AddBuff(BuffType.SpeedBoost, 1);
            
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            //验证：中毒叠层（Stackable=1,MaxStack=3），叠层后每跳伤害×层数
            buff.AddBuff(BuffType.Poisoned, 1);
        }

        if (Input.GetKeyDown(KeyCode.D))
        {
            //验证：组合位参数拒绝（应打印Error返回null，不再KeyNotFound崩溃）
            buff.AddBuff(BuffType.AllDamageOverTime, 1);
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            //验证：未配置类型拒绝（Stunned无配置行，应打印Error返回null）
            buff.AddBuff(BuffType.Stunned, 1);
        }
    }
}
