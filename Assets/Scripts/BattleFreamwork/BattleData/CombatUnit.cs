using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BattleFramework.Core;

/// <summary>
/// 战斗数据
/// </summary>
public class CombatUnit : BaseUnit
{
    #region Buff系统

    /// <summary>
    /// 全局共享的Buff对象池（所有战斗单位的Buff系统统一从这里取对象，只预热一次）
    /// </summary>
    private static ClassObjectPool<Buff> s_SharedBuffPool;

    private BuffSystemPooled m_BuffSystem;

    /// <summary>
    /// 本战斗单位专属的Buff系统（首次访问时懒创建）
    /// 数据层持有，Warrior.Damage等纯数据属性也能直接读取Buff加成
    /// </summary>
    public BuffSystemPooled BuffSystem
    {
        get
        {
            if (m_BuffSystem == null)
            {
                if (s_SharedBuffPool == null)
                {
                    s_SharedBuffPool = new ClassObjectPool<Buff>(200);
                }
                m_BuffSystem = new BuffSystemPooled(0, s_SharedBuffPool);
            }
            return m_BuffSystem;
        }
    }

    #endregion

    /// <summary>
    /// 最大血量
    /// </summary>
    protected int m_MaxHP = 0;
    public virtual int MaxHP { get { return m_MaxHP; }set { m_MaxHP = value; } }


    protected string heroImage;
    public virtual string Imag { get { return heroImage; } set { heroImage = value; } }
    protected string heroPhone;
    public virtual string Photo { get { return heroPhone; } set { heroPhone = value; } }
    protected string battlePhoto;
    public virtual string BattlePhoto { get { return battlePhoto; } set { battlePhoto = value; } }



    /// <summary>
    /// 攻击力
    /// </summary>
    protected int m_Damage;
    public virtual int Damage { get { return m_Damage; } set { m_Damage = value; } }
    /// <summary>
    /// 防御
    /// </summary>
    protected int defend;
    public virtual int Defend { get { return defend; } set { defend = value; } }
    /// <summary>
    /// 暴击
    /// </summary>
    protected int crit;
    public virtual int Crit { get { return crit; } set { crit = value; } }
    /// <summary>
    /// 招架
    /// </summary>
    protected int parry;
    public virtual int Parry { get { return parry; } set { parry = value; } }
    /// <summary>
    /// 穿刺
    /// </summary>
    protected int penetrate;
    public virtual int Penetrate { get { return penetrate; } set { penetrate = value; } }


    /// <summary>
    /// 怒气值
    /// </summary>
    protected  float m_Rage = 0;
    public  float Rage { get { return m_Rage; } set { m_Rage = value; } }
   /// <summary>
   /// 敌人死亡数量
   /// </summary>
    protected int m_AttackDieNum = 0;
    public virtual int AttackDicNum { get { return m_AttackDieNum; }set { m_AttackDieNum = value; } }


    protected float shorHit = 0.4f;
    /// <summary>
    /// 减伤  %
    /// </summary>
    public virtual float ShorHit { get { return shorHit; } set { shorHit = value; } }
    #region 与血量相关的构造函数及事件与委托
    /// <summary>
    /// 当前血量
    /// </summary>
    protected int m_HP;
    public virtual int HP
    {
        get
        {
            return m_HP;
        }
        set
        {

            m_HP = value >= 0 ? value : 0;
            //如果有事件
            if (OnHpChangeEvent!=null)
            {
                OnHpChangeEvent(this);//调用血量变化事件
            }
            // 需要调用速度事件函数，目前所写的因为参数的原因没办法调用
            // 参数需要传日CombatUnit

            //如果血量=0，死亡

            if (m_HP==0)
            {
                if (OnDeadEvent!=null)
                {
                   // AttackDicNum++;

                    OnDeadEvent(this);

                }
                if (OnDead != null)
                    OnDead(this);
            }
        }
    }

    /// <summary>
    /// 血量变化事件
    /// </summary>
    /// <param name="unit"></param>
    public delegate void HpChangeEventHandler(CombatUnit unit);
    public event HpChangeEventHandler OnHpChangeEvent;

    /// <summary>
    /// 死亡事件
    /// </summary>
    /// <param name="unit"></param>
    public delegate void DeadEventHandler(CombatUnit unit);
    public event DeadEventHandler OnDeadEvent;

    public delegate void DeadEventAni(CombatUnit unit);
    public event DeadEventAni OnDead;

    #endregion

    public void Clear()
    {
        OnDeadEvent = null;
        OnDead = null;
     
    }

    /// <summary>
    /// 攻击敌人范围
    /// </summary>
    protected float m_Spring ;
    public virtual float Spring { get { return m_Spring; } set { m_Spring = value; } }

    /// <summary>
    /// 游戏物体警戒范围
    /// </summary>
    protected int m_MoveSpring;
    public virtual int MoveSpring { get { return m_MoveSpring; } set { m_MoveSpring = value; } }


    /// <summary>
    /// 普通攻击CD时间
    /// </summary>
    protected float m_NormalAttackCDTime = 0;
    public virtual float NormalAttackCDTime
    {   get { return m_NormalAttackCDTime; }
        set { m_NormalAttackCDTime = value; }
    }

    public int CantAttack = 0; //是否攻击
    public int CanbeAttack = 0; //是否被攻击

    /// <summary>
    /// 技能实例
    /// </summary>
    protected List<Skill> m_SkillList = null;
    public List<Skill> SkillList { get { return m_SkillList; } }



    /// <summary>
    /// 普通攻击技能
    /// </summary>
    public List<int> NorSkillList = new List<int>();

    /// <summary>
    /// 攻击技能
    /// </summary>
    public List<int> AttackSkillList = new List<int>();
    /// <summary>
    /// 怒气技能（主要针对精英、BOSS使用。）
    /// </summary>
    public List<int> SuperSkillList = new List<int>();

    /// <summary>
    /// 判断该技能是否被使用过了
    /// </summary>
    public List<int> IsUseSkill = new List<int>();
    /// <summary>
    /// 出生事件
    /// </summary>
    /// <param name="unit"></param>
    public delegate void SpawnEventHandler(CombatUnit unit);
    public event SpawnEventHandler OnSpawnEvent;




    /// <summary>
    /// 获得指定技能
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Skill GetSkill(int id)
    {
        if (m_SkillList!=null&&m_SkillList.Count>0)
        {
            for (int i = 0; i < m_SkillList.Count; i++)
            {
                if (m_SkillList[i].SkillId==id)
                {
                    return m_SkillList[i];
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 获得随机普通技能
    /// </summary>
    /// <returns></returns>
    public Skill GetRandomNormalSkill()
    {
        List<object> allSkill = new List<object>();
        for (int i = 0; i < NorSkillList.Count; i++)
        {
            Skill skill = GetSkill(NorSkillList[i]);
            allSkill.Add(skill);

        }
        return CommonHelper.RandomObject(allSkill)as Skill;
    }

    /// <summary>
    /// 自动  技能
    /// 指的是点击自动攻击按钮后用到的功能
    /// </summary>
    /// <returns></returns>
    public Skill GetAutoSkillId()
    {
        List<object> allSkill = new List<object>();
        for (int i = 0; i < AttackSkillList.Count; i++)
        {
            Skill skill = GetSkill(AttackSkillList[i]);
            //技能没有在释放
            if (!skill.NormalAttackCD)
            {
                allSkill.Add(skill);
            }
        }
        return CommonHelper.RandomObject(allSkill) as Skill;
    }


    // ==================================================以上为原代码================================================

    /// <summary>
    /// 获得可以使用的技能
    /// </summary>
    /// <returns></returns>
    public Skill GetCanUseSkill()
    {
        //List<object> allSkill = new List<object>();
        for (int i = m_SkillList.Count-1; i >= 0; i--)
        {
            if (m_SkillList[i].NormalAttackCD)
            {
                continue;
            }
            if (m_SkillList[i].CanUseSkill==CanUseSkill.YES)
            {
                return m_SkillList[i];
               // allSkill.Add(m_SkillList[i]);
            }
        }
        return null;
        //return CommonHelper.RandomObject(allSkill) as Skill;
    }

    /// <summary>
    /// 出生事件调用
    /// </summary>
    public void OnSpawn()
    {
        if (OnSpawnEvent!=null)
        {
            OnSpawnEvent(this);
        }
    }
}
