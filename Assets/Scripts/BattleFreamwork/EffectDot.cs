using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 特效绑点脚本
///  这个脚本需要挂载在玩家身上。并指定特效的绑点
/// </summary>
public class EffectDot : MonoBehaviour
{
    //绑点字典
    public Dictionary<string, Transform> m_DotDic = new Dictionary<string, Transform>();
    //绑点的数组
    public List<Transform> DotList = new List<Transform>();

    private void Awake()
    {
        for (int i = 0; i < DotList.Count; i++)
        {
            if (DotList[i] != null)
            {
                if (!m_DotDic.ContainsKey(DotList[i].name))
                {
                    m_DotDic.Add(DotList[i].name, DotList[i]);
                }
            }
        }
    }

 
    /// <summary>
    /// 获取特效绑点
    /// </summary>
    /// <param name="name"></特效名称>
    /// <returns></returns>
    public Transform GetEffectDot(string name)
    {
        return m_DotDic.ContainsKey(name) ? m_DotDic[name] : null;
    }
 

}
