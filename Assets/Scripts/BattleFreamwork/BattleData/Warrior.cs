using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Warrior : CombatUnit
{

    protected WarriorData m_WarriorBaseData;

    public WarriorData WarriorBaseData
    {
        get { return m_WarriorBaseData; }
    }

    /// <summary>
    /// 获取玩家ID
    /// </summary>
    public virtual int ID
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.ID;
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
           return WarriorBaseData.Atk;
           
        }
    }
    /// <summary>
    /// 防御
    /// </summary>
    public override int Defend
    {
        get
        {
           return WarriorBaseData.Def;
        }
    }
    /// <summary>
    /// 暴击
    /// </summary>
    public override int Crit
    {
        get
        {
            return WarriorBaseData.Crit;
        }
    }
    /// <summary>
    /// 招架
    /// </summary>
    public override int Parry
    {
        get
        {
           return  WarriorBaseData.Parry; 
        }
    }
    /// <summary>
    /// 穿刺
    /// </summary>
    public override int Penetrate
    {
        get
        {
            return WarriorBaseData.penetrate;
        }
    }


    /// <summary>
    /// 最大血量
    /// </summary>
    public override int MaxHP
    {
        get
        {
            return WarriorBaseData.HP;
        }
    }



    /// <summary>
    /// 获取人物模型
    /// </summary>
    public virtual string Model
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                if (m_Modle == null)
                {
                    base.Modle = m_WarriorBaseData.Model;
                }
                return m_WarriorBaseData.Model;
            }
            else
            {
                return "";
            }
        }
    }
    /// <summary>
    /// 获取人物形象图片
    /// </summary>
    public override string Imag
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.Image;
            }
            else
            {
                return "";
            }
        }
    }
    /// <summary>
    /// 获取人物头像
    /// </summary>
    public override string Photo
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.Photo;
            }
            else
            {
                return "";
            }
        }
    }
    /// <summary>
    /// 战斗头像
    /// </summary>
    public override string BattlePhoto
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.BattlePhoto;
            }
            else
            {
                return "";
            }
        }
    }

    /// <summary>
    /// 获取名称
    /// </summary>
    public override string Name
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.Name;
            }
            else
            {
                return "";
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
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.NormalAttackCDTime;
            }
            else
            {
                return 0;
            }
        }
    }

   

    


    /// <summary>
    /// 敌人触发范围
    /// </summary>
    public override float Spring
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.Spring;
            }
            else
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// 玩家向前移动警戒距离
    /// </summary>
    public override int MoveSpring
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.MoveSpring;
            }
            else
            {
                return 0;
            }
        }
    }


    /// <summary>
    /// 模型高度
    /// </summary>
    public override float Height
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.Height;
            }
            else
            {
                return 0;
            }
        }
    }
    /// <summary>
    /// 模型半径
    /// </summary>
    public override float Radius
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.Radius;
            }
            else
            {
                return 0;
            }
        }
    }
    /// <summary>
    /// 模型移动速度
    /// </summary>
    public override float Speed
    {
        get
        {
            if (m_WarriorBaseData != null)
            {
                return m_WarriorBaseData.Speed;
            }
            else
            {
                return 0;
            }
        }
    }
    /// <summary>
    /// 构造函数
    /// 获取一条模型信息
    /// </summary>
    /// <param name="dataId"></param>
    public Warrior(int dataId)
    {
        //获取英雄的配置文件数据
        m_WarriorBaseData = ConfigManager.GetWarriorData(dataId);
        if (m_WarriorBaseData == null)
        {
            Debug.LogError("WarriorData is null  :" + dataId);
            return;
        }
        m_SkillList = new List<Skill>();    //当前玩家所以技能
        //遍历英雄配置文件中配置的应用技能ID
        
        for (int i = 0; i < m_WarriorBaseData.SkillArray.Length; i++)
        {
            Skill skill = new Skill(m_WarriorBaseData.SkillArray[i]);
            m_SkillList.Add(skill);
            //对技能类型分类，分出不同的数组
            switch (skill.BaseData.SkillType)
            {
                case SkillTypes.NormalSkill:
                    NorSkillList.Add(skill.SkillId);
                   
                    break;
                case SkillTypes.AttackSkill:
                    AttackSkillList.Add(skill.SkillId);
                    break;
                case SkillTypes.SuperSkill:
                    SuperSkillList.Add(skill.SkillId);
                    break;
                default:
                    break;
            }
        }
        HP = MaxHP;
    }
    






}
