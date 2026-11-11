using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战斗NPC数据类
/// </summary>
public class AttackNpc : CombatUnit
{

    /// <summary>
    /// NPC数据基础
    /// </summary>
    protected AttackNpcData m_AttackNpcBaseData;
    public AttackNpcData AttackNpcBaseData { get { return m_AttackNpcBaseData; } }

  

    /// <summary>
    /// 模型包
    /// </summary>
    public virtual string Model
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                if (m_Modle == null)
                {
                    base.Modle = m_AttackNpcBaseData.Model;
                }
                return m_AttackNpcBaseData.Model;
            }
            else
            {
                return "";
            }
        }
    }

  
    public virtual int UnitLevel
    {
        get
        {
            if (m_AttackNpcBaseData!=null)
            {
                return m_AttackNpcBaseData.UnitLevel;
            }
            else
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// 移动速度
    /// </summary>
    public override float Speed
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                return m_AttackNpcBaseData.Speed;
            }
            else
            {
                return 0;
            }
        }
    }
    /// <summary>
    /// 名称
    /// </summary>
    public override string Name
    {
        get;
        set;
    }


    /// <summary>
    /// 最大血量
    /// </summary>
    public override int MaxHP
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                return m_AttackNpcBaseData.hp;
            }
            else
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// 伤害
    /// </summary>
    public override int Damage
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                return m_AttackNpcBaseData.attack;
            }
            else
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// 防御
    /// </summary>
    public override int Defend
    {
        get
        {
            if (m_AttackNpcBaseData != null)
                return m_AttackNpcBaseData.defend;
            else
                return 0;
        }
    }
    /// <summary>
    /// 暴击
    /// </summary>
    public override int Crit
    {
        get
        {
            if (m_AttackNpcBaseData != null)
                return m_AttackNpcBaseData.crit;
            else
                return 0;
        }
    }
    /// <summary>
    /// 招架
    /// </summary>
    public override int Parry
    {
        get
        {
            if (m_AttackNpcBaseData != null)
                return m_AttackNpcBaseData.parry;
            else
                return 0;
        }
    }
    /// <summary>
    /// 穿刺
    /// </summary>
    public override int Penetrate
    {
        get
        {
            if (m_AttackNpcBaseData != null)
                return m_AttackNpcBaseData.penetrate;
            else
                return 0;
        }
    }


    /// <summary>
    /// 高度
    /// </summary>
    public override float Height
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                return m_AttackNpcBaseData.Height;
            }
            else
            {
                return 0;
            }
        }
    }
    /// <summary>
    /// 半径
    /// </summary>
    public override float Radius
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                return m_AttackNpcBaseData.Radius;
            }
            else
            {
                return 0;
            }
        }
    }

    public override float BaseOffset
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                return m_AttackNpcBaseData.BaseOffset;
            }
            else
            {
                return 0;
            }
        }
    }




    /// <summary>
    /// 敌人警戒范围
    /// </summary>
    public override float Spring
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                return m_AttackNpcBaseData.Spring;
            }
            else
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// 等级
    /// </summary>
    public override int Level
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                return m_AttackNpcBaseData.Level;
            }
            else
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// 普通攻击CD
    /// </summary>
    public override float NormalAttackCDTime
    {
        get
        {
            if (m_AttackNpcBaseData != null)
            {
                return m_AttackNpcBaseData.NormalAttackCDTime;
            }
            else
            {
                return 0;
            }
        }
    }
   

    public AttackNpc(int dataId)
    {
        m_AttackNpcBaseData = ConfigManager.GetAttackNpcData(dataId);
        if (m_AttackNpcBaseData == null)
        {
            Debug.LogError("AttackNpcData is null,NpcID :" + dataId);
            return;
        }

        m_SkillList = new List<Skill>();
        for (int i = 0; i < m_AttackNpcBaseData.SkillArray.Length; i++)
        {
            Skill skill = new Skill(m_AttackNpcBaseData.SkillArray[i]);
            m_SkillList.Add(skill);

            if (skill.BaseData.SkillType == SkillTypes.NormalSkill)
            {
                NorSkillList.Add(skill.SkillId);
                skill.CanUseSkill = CanUseSkill.YES;
            }
            else if (skill.BaseData.SkillType == SkillTypes.AttackSkill)
            {
                AttackSkillList.Add(skill.SkillId);
                skill.CanUseSkill = CanUseSkill.NO;
            }
            else if (skill.BaseData.SkillType==SkillTypes.SuperSkill)
            {
                SuperSkillList.Add(skill.SkillId);
            }
        }

        HP = MaxHP;
    }


}
