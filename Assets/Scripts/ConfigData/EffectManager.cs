using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum EffectTypes
{
    Normal=1,
    WeaponTrail=2,
    /// <summary>
    /// 子弹，用于特效判定一次伤害的作用
    /// </summary>
    Bullet=3,
    /// <summary>
    /// 导弹，用于特效判定多次伤害的作用
    /// </summary>
    Missile=4  ,

    /// <summary>
    /// 毒箭
    /// </summary>
    GasArrow,
    /// <summary>
    /// 爆炸箭
    /// </summary>
    ExplosiveArrow,
    /// <summary>
    /// 箭雨
    /// </summary>
    ArrowHail,
    /// <summary>
    /// 男法师的魔法球
    /// </summary>
    MagiBall,
    /// <summary>
    /// 男法师的寒冰球
    /// </summary>
    IceBall,
    /// <summary>
    /// 男骑士的直捣黄龙
    /// </summary>
    BackDownHitAndAirDown,
    /// <summary>
    /// 女法师的火球
    /// </summary>
    FireBall,
    /// <summary>
    /// 台风技能
    /// </summary>
    Typhoon,
    /// <summary>
    /// 火墙
    /// </summary>
    FireWall,
    /// <summary>
    /// 风震
    /// </summary>
    WindJarred,
    /// <summary>
    /// 女牧师
    /// 技能：水球禁锢
    /// 投技
    /// </summary>
    WaterDungeonGu,
    /// <summary>
    /// 浮空后退受击
    /// </summary>
    BackAirHit,
    /// <summary>
    /// 跟随模型或者特效移动方向移动（设置该对象为父物体）
    /// 站立后退持续受击
    /// </summary>
    OnBackContiHit,
    /// <summary>
    /// 后退，升空，并受击
    /// 男天籁：异界天音
    /// </summary>
    OnAirAndBackContiHit,
    /// <summary>
    /// 倒地（不带位移）
    /// </summary>
    OnDown=19,
    /// <summary>
    /// 僵直
    /// </summary>
    OnRigor=20,
    /// <summary>
    /// 当第2个特效攻击的时候浮空倒地
    /// </summary>
    On2ndEffectForAniAirDown=21,
    /// <summary>
    /// 女骑士  毒爆射击 技能    毒圈爆炸
    /// </summary>
    GasArrowBoom = 22,
    /// <summary>
    /// 倒地持续受击
    /// </summary>
    OnDownContiHit=23,
}
/// <summary>
/// 特效碰撞框类型
/// </summary>
public enum EffectColliderType
{  
    /// <summary>
    /// 球体碰撞框
    /// </summary>
   SphereCollider=1,
   /// <summary>
   /// 胶囊体碰撞框
   /// </summary>
   CapsuleCollider=2,
   /// <summary>
   /// 盒子碰撞框
   /// </summary>
   BoxCollider=3,

}

/// <summary>
/// 特效管理类
/// </summary>
public class EffectManager {

    #region 单例
    private static EffectManager s_Instance = null;
    public static EffectManager Instance
    {
        get
        {
            if (s_Instance == null)
            {
                s_Instance = new EffectManager();
            }
            return s_Instance;
        }
    }
    #endregion

    /*
     * 特效字典
     * <string, List<GameObject>>  
     *  string：特效名称
     *  List<GameObject> 特效数组
     */
   // private Dictionary<string, List<GameObject>> m_EffectDic = new Dictionary<string, List<GameObject>>();
    public Dictionary<string, GameObject> m_EffectDic = new Dictionary<string, GameObject>();

    private GameObject m_EffectParent = null;

    /// <summary>
    /// 特效管理类构造函数
    /// </summary>
    public EffectManager()
    {
        m_EffectParent = new GameObject("Effect");
        //特效不消失
        Object.DontDestroyOnLoad(m_EffectParent);
    }

    #region 创建特效的方法及重载方法

    

  

    public void OnAttackAudio(int audioId)
    {
       
    }
/// <summary>
    ///  创建特效
    /// </summary>
    /// <param name="name">特效名称</param>
    /// <param isSava="isSave">是否保存</param>
    /// <returns></returns>
    public GameObject CreateEffect(string name, bool isSave)
    {
        GameObject effect = null;
        GameObject prefab = Resources.Load<GameObject>($"Effect/Skill/{name}");
        //如果该特效保存
        if (isSave)
        {
            if (!m_EffectDic.ContainsKey(name))
            {
                if (prefab != null)
                {
                    //实例化特效  及设置父节点
                    effect = Object.Instantiate(prefab, Vector3.one * 1000, prefab.transform.localRotation);
                    //设置克隆出来的特效名称
                    effect.name = name;
                 
                    effect.SetActive(false);

                    //加入特效字典
                    m_EffectDic.Add(name, effect);
                }
            }
            else
            {
                m_EffectDic.TryGetValue(name, out effect);
                if (effect==null)
                {
                    m_EffectDic.Remove(name);
                    effect = Object.Instantiate(prefab, Vector3.one * 1000, prefab.transform.localRotation);
                    effect.name = name;
                    m_EffectDic.Add(name, effect);
                }
               
                effect.SetActive(false);

            }
        }
        else
        {
            if (prefab != null)
            {
                //实例化特效  及设置父节点
                effect = Object.Instantiate(prefab, /*Vector3.one * 1000, Quaternion.identity,*/ m_EffectParent.transform);
                //设置克隆出来的特效名称
                effect.name = name;
             
                effect.SetActive(true);
            }
            else
            {
                Debug.LogError("Effect prefab is null ,plaease check Effect name:" + name);
                return null;
            }
        }
        return effect;
    }

    public GameObject CreateEffect(int id, Transform tran)
    {
        
        EffectData info = ConfigManager.GetEffectData(id);
      //在此播放声音
        //OnAttackAudio(info.Audio);
        if (info == null)
        {
            return null;
        }
        if (info.IsBind)
        {
            GameObject effect = CreateEffect(info.ResName, info.IsSave);
            if (effect == null)
            {
               // Debug.LogError("effect is null,effect name is :" + info.ArtRes);
                return null;
            }

            BindOnDot(effect.transform,info,tran);
            effect.SetActive(true);
            //设置特效存活时间
           // SetEffectLiveTime(effect, info);

            return effect;

        }
        else if(!info.IsBind)
        {
            GameObject effect = CreateEffect(info.ResName, info.IsSave);
            if (effect==null)
            {
                //Debug.LogError("effect is null,effect name is :" + info.ArtRes);
                return null;
            }
            BindOnDot(effect.transform,info,tran);
            //设置特效存活时间
           // SetEffectLiveTime(effect, info);
            effect.SetActive(true);

            return effect; 
        }
        return null;
    }

    /// <summary>
    /// ★ 2026-10-10 弹道投递（投掷物方案 P4）：在**世界坐标原地**播放一个不绑点的特效 —— 弹着点爆点专用。
    /// 为什么不走 CreateEffect(id, tran)+BindOnDot：它要求目标身上有 EffectDot（弹着点/地面点不存在绑点组件），
    /// 拿不到绑点会提前 return，特效停留在隐藏位置等于没播且不报错。
    /// isSave 一律走 false 实例化路径：播放频率 = 命中频率，与既有受击特效同款成本档位（后续可升级为配对池）。
    /// </summary>
    public GameObject CreateEffectAt(int id, Vector3 position, Quaternion rotation)
    {
        EffectData info = ConfigManager.GetEffectData(id);
        if (info == null)
        {
            return null;   // 悬空 id 已由 ValidateProjectileBindings 在加载期 LogError 指名，此处不重复报
        }

        GameObject effect = CreateEffect(info.ResName, false);
        if (effect == null)
        {
            return null;   // CreateEffect 内部已对 prefab 缺失 LogError
        }

        effect.transform.position = position;
        effect.transform.rotation = rotation;
        effect.SetActive(true);
        return effect;
    }

    #endregion
    #region 特效时间管理
   
    private void ResaveEffect(params object[] args)
    {
        if (args == null)
        {
            return;
        }

        GameObject effect = args[0] as GameObject;
        if (effect != null)
        {
            effect.SetActive(false);
            effect.transform.position = Vector3.zero;
            effect.transform.parent = m_EffectParent.transform;
            // m_EffectPoolList[effect.name].Add(effect);
            if (!m_EffectDic.ContainsKey(effect.name))
            {
                m_EffectDic.Add(effect.name, effect);
            }
        }
    }


    /// <summary>
    /// 如果特效是不需要保存的
    /// </summary>
    /// <param name="args"></param>
    private void OndDestroyEffect(params object[] args)
    {
        if (args == null)
        {
            return;
        }

        GameObject effect = args[0] as GameObject;
        if (effect != null)
        {
            Object.Destroy(effect);
        }
    }
    #endregion
    /// <summary>
    /// 是否需要绑点
    ///  effectTran 绑点
    /// info 绑点的特效
    /// tran 被绑点的位置
    /// </summary>
    /// <param name="effectTran">绑点</param>
    /// <param name="info">绑点的特效</param>
    /// <param name="tran">释放特效的人</param>
    void BindOnDot(Transform effectTran, EffectData info, Transform tran)
    {
        if (effectTran==null||info==null||tran==null)
        {
            Debug.LogError("BindOnDot is error");
            return;
        }
       
            EffectDot effectDot = tran.GetComponent<EffectDot>();//在玩家身上获取特效绑点。
            if (effectDot==null)
            {
                effectTran.parent = tran;
            }
            else
            {
                //判定特效是否需要绑点
                if (info.IsBind)
                {
                    //特效需要绑定在固定的位置的时候，随着绑点移动 ，例如挥动的刀光
                    Transform dot = effectDot.GetEffectDot(info.BindName);
                    if (dot==null)
                    {
                       // Debug.LogError(info.Dot[0] + "is null");
                        return;
                    }
                    effectTran.parent = dot;
                    effectTran.localPosition = Vector3.zero;
                    effectTran.rotation = dot.rotation;
                   
                    
                }
                else
                {
                    //不需要绑定在固定的特效绑点，例如玩家受伤，玩家身上的受伤特效，播放完就消失
                    //对于受到击打发生的特效没必要绑定绑点
                    Transform dot = effectDot.GetEffectDot(info.BindName);
                    if (dot==null)
                    {
                        //Debug.LogError(info.Dot[0] + "is null");
                        return;
                    }
                    effectTran.position = dot.position;
                    
                }
            }
       
    }

    
   

}
