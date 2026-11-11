using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;



public class EffectMsg
{
    public Dictionary<int,EffectData> EffectDataDict=new Dictionary<int, EffectData>();

    /// <summary>配置表是否已加载过 —— 懒加载开关，保证整张表只 Add 一次</summary>
    private bool m_Loaded = false;

    /// <summary>
    /// 按特效 id 取配置。
    /// ★ 2026-09-14 修复两个致命缺陷（打通刀光链路时暴露，此前 CreateEffect 从未被真正调用故一直潜伏）：
    ///   ① 原来只在「本次查询 miss」时才加载整表，且加载完立刻用 EffectDataDict[id] 取值。
    ///      若 id 本身在表里不存在，加载完仍然取不到 → 直接抛 KeyNotFoundException（红色报错 + 整段调用栈中断）。
    ///   ② 更糟的是第二次 miss 会再次走 Add 全表的分支 → 重复 key → 抛 ArgumentException
    ///      "An item with the same key has already been added"。
    ///   典型触发场景：技能配了一个表里没有的特效 id，一次三连斩就刷出 2~3 条异常。
    /// 现在的行为：整张表懒加载一次（用空表判断而不是用 miss 判断），查不到返回 null。
    /// 上层 EffectManager.CreateEffect 已有 if (info == null) return null 容错，缺失配置只会静默跳过。
    /// </summary>
    public EffectData GetData(int id)
    {
        if (!m_Loaded)
        {
            LoadAll();
            m_Loaded = true;
        }

        EffectDataDict.TryGetValue(id, out EffectData data);
        return data;   // 找不到返回 null，由调用方决定如何处理（原来这里是抛异常）
    }

    /// <summary>
    /// 一次性把 EffectData.csv 全表读进字典。
    /// 用索引器赋值而非 Add：万一表里真有重复 ID，后者覆盖前者即可，不会抛异常把整局游戏打断。
    /// </summary>
    private void LoadAll()
    {
        // ★ 2026-09-28 CSV→JSON：配置源切换为 EffectData.json（键名与 EffectData 公开成员一一对应）。
        //   旧 Split('\r') 解析依赖 CRLF 行尾、且首列靠 TryParse 吃掉行首换行 —— 结构化解析一并移除。
        List<EffectData> rows = JsonConfigLoader.LoadRows<EffectData>("EffectData");
        if (rows == null)
        {
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            EffectData temp = rows[i];
            if (temp == null) continue;
            EffectDataDict[temp.ID] = temp;   // 索引器：重复 key 覆盖，绝不抛异常（迁移自 CSV 版 last-wins）
        }
    }
}

public class EffectData 
{
    //ID
   public int ID { get; set; }
   //绑点名称
   public string BindName { get; set; }
   //是否保存
   public bool IsSave { get; set; }
   //是否绑定绑点
   public bool IsBind { get; set; }
   //是否跟随旋转
   public bool IsRate { get; set; }
   //是否显示
   public bool IsActive { get; set; }
   //资源名称
   public string ResName { get; set; }
  
}
