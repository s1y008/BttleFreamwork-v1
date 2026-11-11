using System.Collections;
using System.Collections.Generic;
using UnityEngine;


 /// <summary>
 /// 玩家类型
 /// </summary>
public enum WarriorTypes
{
    /// <summary>
    /// 近战
    /// </summary>
    Melee=0,
    /// <summary>
    /// 远程
    /// </summary>
    Remote=1,

    Null=100,

}

//public class Prop
//{
//    public int _base;
//    public int _add;
//    public int _grow;
//}
  /// <summary>
  /// 玩家信息
  /// </summary>
public class WarriorData  {

    /// <summary>
    /// 数据编号;
    /// </summary>
    public int ID;

    /// <summary>
    /// 名字;
    /// </summary>
    public string Name;

    /// <summary>
    /// 等级
    /// </summary>
    public int Level;

    /// <summary>
    /// 品质等级
    /// </summary>
    public int Qua;

    /// <summary>
    /// 玩家类型
    /// </summary>
    public WarriorTypes m_WarriorTypes;


    /// <summary>
    /// 描述
    /// </summary>
    public string Desc;

    /// <summary>
    /// 卡牌;
    /// </summary>
    public string Image;

    /// <summary>
    /// 头像
    /// </summary>
    public string Photo;
    /// <summary>
    /// 战斗头像
    /// </summary>
    public string BattlePhoto;

    /// <summary>
    /// 模型资源;
    /// </summary>
    public string Model;

    /// <summary>
    /// 高度;
    /// </summary>
    public float Height;

    /// <summary>
    /// 半径;
    /// </summary>
    public float Radius;

    /// <summary>
    /// 触发范围;
    /// </summary>
    public float Spring;
    /// <summary>
    /// 玩家向前移动距离敌人停止的距离
    /// </summary>
    public int MoveSpring;
    /// <summary>
    /// 缩放系数
    /// </summary>
    public float ScalModulus;



    
    /// <summary>
    /// 血量;
    /// </summary>
    public int HP { get; set; }

    /// <summary>
    /// 攻击力;
    /// </summary>
    public int Atk { get; set; }

    /// <summary>
    /// 防御
    /// </summary>
    public int Def { get; set; }
    public int Crit { get; set; }
    public int Parry{get;set;}
    public int penetrate { get; set; }

    /// <summary>
    /// 移动速度;
    /// </summary>
    public float Speed;


    /// <summary>
    /// 普通攻击CD;
    /// </summary>
    public float NormalAttackCDTime;

    /// <summary>
    /// 技能列表, 第一个为普通技能;
    /// </summary>
    public int[] SkillArray;

    /// <summary>
    /// 主将技
    /// </summary>
    public int LeaderSkill;
   
}

public class WarriorDataManager
{
    private Dictionary<int, WarriorData> m_WarriorDataDic = null;

    /// <summary>
    /// 获取玩家数据
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public WarriorData GetData(int key)
    {
        if (m_WarriorDataDic==null)
        {
            LoadWarriorData();
        }

        return m_WarriorDataDic.ContainsKey(key) ? m_WarriorDataDic[key] : null;
      
    }

    /// <summary>
    /// 读取玩家配置文件（2026-09-28 起为 WarriorData.json）
    /// </summary>
    public void LoadWarriorData ()
    {
        m_WarriorDataDic = new Dictionary<int, WarriorData>();

        // ★ 2026-09-28 CSV→JSON：配置源切换为 WarriorData.json（键名与 WarriorData 公开成员一一对应，
        //   含 m_WarriorTypes 枚举字段）。旧裸 int.Parse（ID 列）与「含末行」的循环上界随 CSV 解析器一并移除。
        List<WarriorData> rows = JsonConfigLoader.LoadRows<WarriorData>("WarriorData");
        if (rows == null)
        {
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            WarriorData info = rows[i];
            if (info == null) continue;
            if (!m_WarriorDataDic.ContainsKey(info.ID))
            {
                m_WarriorDataDic.Add(info.ID, info);
            }
        }
    }

    /// <summary>全部玩家配置行 —— 供迁移验证工具 / Editor 探针全表遍历。</summary>
    public List<WarriorData> GetAll()
    {
        return new List<WarriorData>(m_WarriorDataDic.Values);
    }
}
