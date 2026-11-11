using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 弹道类型 —— 投掷物「怎么飞」（投掷物方案 P1，2026-10-10）。
/// ⚠ 本枚举只回答飞行方式；「打到谁 / 打多疼」仍由技能行（SkillData）的
///   HitType / SkillHitSharpType / AttackRadius / HitNum / EffectTypesBySegment 决定 —— 单一真相源。
/// </summary>
public enum ProjectileTrajectoryType
{
    /// <summary>直线直飞：按出膛方向匀速直线，命中靠 HitRadius 距离判定（Bullet 语义）</summary>
    Straight = 0,

    /// <summary>锁定追踪：每帧朝锁定目标转向，角速度受 HomingTurnRate 限制（Missile/MagiBall 语义）</summary>
    Homing = 1,

    /// <summary>抛物线落点（★ 预留未实现）：按目标落点算抛物线飞行，到点按技能形状结算</summary>
    Lob = 2,
}

/// <summary>
/// 投掷物配置行（Resources/Config/ProjectileData.json，投掷物方案 P1）。
/// 键名与公开成员一一对应（JsonConfigLoader 契约）；枚举 TrajectoryType 接受 int 或枚举名字符串。
/// 【与 SkillData 的分工】本表只描述「这个弹怎么飞、长什么样」；
///   技能行通过 DeliveryType + ProjectileId 两个字段引用本表（见 SkillData 同名字段注释）。
/// </summary>
public class ProjectileData
{
    public int ID { get; set; }
    public string Name { get; set; }

    /// <summary>弹道类型（0=直线 1=锁定追踪 2=抛物线，见 ProjectileTrajectoryType）</summary>
    public ProjectileTrajectoryType TrajectoryType { get; set; }

    /// <summary>飞行速度（米/秒），必须 &gt; 0（加载期校验）</summary>
    public float Speed { get; set; }

    /// <summary>最大射程（米）：飞到即自爆 —— AoE 弹在自爆点结算，单体弹打空丢弃（双保险之一）</summary>
    public float MaxRange { get; set; }

    /// <summary>最大寿命（秒）：超时自爆，与 MaxRange 互为双保险</summary>
    public float LifeTime { get; set; }

    /// <summary>命中半径（米）：与瞄准点在 <b>XZ 平面</b>上的距离 ≤ 此值即命中（与出膛高度解耦）</summary>
    public float HitRadius { get; set; }

    /// <summary>追踪角速度上限（度/秒）；直线弹忽略此值</summary>
    public float HomingTurnRate { get; set; }

    /// <summary>出膛高度（米）：弹体初始位置相对施法者脚底的抬升（魔法球从手部高度出膛）</summary>
    public float LaunchHeight { get; set; }

    /// <summary>
    /// 飞行体特效 id（EffectData，0=无）。
    /// ⚠ 实例化走 isSave:false 路径并挂在弹体上跟随 —— 绝不能走 isSave:true 单实例池：
    ///   EffectManager 池按 ResName 只留一个实例，飞行体占用会被下一次施法特效抢走（P20 事故形态）。
    /// </summary>
    public int FxId { get; set; }

    /// <summary>弹着点环境特效 id（EffectData，0=无）：在弹着点原地播放、不绑点（目标身上的受击特效由技能 HitEffect 列走既有管线，与此正交）</summary>
    public int ImpactFxId { get; set; }

    /// <summary>穿透数（0=命中即爆；&gt;0 = 依次穿透结算，★ 预留未实现，当前一律按 0 处理）</summary>
    public int PierceCount { get; set; }
}

/// <summary>
/// ProjectileData.json 装载器 —— 结构对齐 SkillDataManager：懒加载、重复 ID 跳过指名、
/// 单行脏数据由 JsonConfigLoader 的错误处理器吃掉（跳行继续）。
/// </summary>
public class ProjectileDataManager
{
    private Dictionary<int, ProjectileData> m_Dic;

    public ProjectileData GetData(int key)
    {
        if (m_Dic == null)
        {
            Load();
        }
        return (m_Dic != null && m_Dic.ContainsKey(key)) ? m_Dic[key] : null;
    }

    private void Load()
    {
        m_Dic = new Dictionary<int, ProjectileData>();

        List<ProjectileData> rows = JsonConfigLoader.LoadRows<ProjectileData>("ProjectileData");
        if (rows == null)
        {
            return; // 整表不可用：保持空字典，LoadRows 已 LogError 指名，不静默降级
        }

        for (int i = 0; i < rows.Count; i++)
        {
            ProjectileData row = rows[i];
            if (row == null)
            {
                continue;
            }
            if (!m_Dic.ContainsKey(row.ID))
            {
                m_Dic.Add(row.ID, row);
            }
            else
            {
                Debug.LogError("[ProjectileData] 重复投掷物ID已跳过: " + row.ID);
            }
        }
    }
}
