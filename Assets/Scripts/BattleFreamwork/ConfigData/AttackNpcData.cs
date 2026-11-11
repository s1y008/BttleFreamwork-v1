using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AttackNpcData  {
    public int ID;
    public string Name;
    public int Level;
    public string Model;
    public int UnitLevel;
    public float Height;
    public float Radius;
    public float BaseOffset;
    public float Speed;
    public float Spring;

    /// <summary>
    /// 攻击
    /// </summary>
    public int attack;
    /// <summary>
    /// 防御
    /// </summary>
    public int defend;
    /// <summary>
    /// 暴击
    /// </summary>
    public int crit;
    /// <summary>
    /// 生命
    /// </summary>
    public int hp;
    /// <summary>
    /// 招架
    /// </summary>
    public int parry;
    /// <summary>
    /// 穿刺
    /// </summary>
    public int penetrate;



    //public int MaxHP;
    //public int Damage;
    public float NormalAttackCDTime;
    public int[] SkillArray;
    public string Des;
}

public class AttackNpcDataManager
{
    private Dictionary<int, AttackNpcData> m_AttackNpcDataDic = null;

    public AttackNpcData GetData(int key)
    {
        if (m_AttackNpcDataDic==null)
        {
            LoadAttackNpcData();
        }
        return m_AttackNpcDataDic.ContainsKey(key) ? m_AttackNpcDataDic[key] : null;
    }

    public void LoadAttackNpcData()
    {
        m_AttackNpcDataDic = new Dictionary<int, AttackNpcData>();

        // ★ 2026-09-28 CSV→JSON：配置源切换为 AttackNpcData.json（键名与 AttackNpcData 公开成员一一对应）。
        //   旧裸 int.Parse（ID/Level 列）遇空单元格会 FormatException 崩掉整表；循环上界含末行、
        //   依赖文件不得以换行结尾 —— JSON 结构化解析天然免疫这两类问题。
        List<AttackNpcData> rows = JsonConfigLoader.LoadRows<AttackNpcData>("AttackNpcData");
        if (rows == null)
        {
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            AttackNpcData info = rows[i];
            if (info == null) continue;
            if (!m_AttackNpcDataDic.ContainsKey(info.ID))
            {
                m_AttackNpcDataDic.Add(info.ID, info);
            }
        }
    }

    /// <summary>全部 NPC 配置行 —— 供迁移验证工具 / Editor 探针全表遍历。</summary>
    public List<AttackNpcData> GetAll()
    {
        return new List<AttackNpcData>(m_AttackNpcDataDic.Values);
    }
}
