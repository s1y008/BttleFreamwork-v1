using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战斗对象基类
/// 基础属性
/// </summary>
public class BaseUnit {
    public GameObject m_UnitObj;//表示玩家或者敌人

    /// <summary>
    /// 名称
    /// </summary>
    protected string m_Name = "";
    public virtual string Name { get { return m_Name; } set { m_Name = value; } }

    /// <summary>
    /// 等级
    /// </summary>
    protected int m_Level;
    public virtual int Level { get { return m_Level; } set { m_Level = value; } }

    /// <summary>
    /// 高度
    /// </summary>
    protected float m_Height;
    public virtual float Height { get { return m_Height; } set { m_Height = value; } }

    /// <summary>
    /// 半径
    /// </summary>
    protected float m_Radius;
    public virtual float Radius { get { return m_Radius; } set { m_Radius = value; } }

    protected float m_BaseOffset;
    public virtual float BaseOffset { get { return m_BaseOffset; } set { m_BaseOffset = value; } }

    protected string m_Modle;
    public virtual string Modle { get { return m_Modle; }set { m_Modle = value; } }
    /// <summary>
    /// 移动速度
    /// </summary>
    protected float m_Speed;
    public virtual float Speed { get { return m_Speed; } set { m_Speed = value; } }

   
}
