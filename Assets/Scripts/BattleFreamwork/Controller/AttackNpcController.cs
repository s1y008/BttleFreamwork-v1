
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AttackNpcController : CombatUnitController
{
    /// <summary>
    /// 获取敌人的战斗数据
    /// </summary>
    public AttackNpc CurAttackNpc
    {
        get
        {
            return m_CurCombatUnit as AttackNpc;
        }
    }
          /// <summary>
          /// 敌方层，相对于NPC来说敌人就是玩家
          /// </summary>
    public override int EnemyLayer { get { return 1 << LayerMask.NameToLayer("Warrior"); } }
    /// <summary>
    /// 敌方Tag
    /// </summary>
    public override string EnemyTag {  get  { return BattleConst.tag_Player; } }

    /// <summary>
    /// 重复查找目标
    /// </summary>
    private float m_ReFindTargetTime = 0.3f;
    private float m_ReFindTime = 0f;
    /// <summary>
    /// 唯一标识
    /// </summary>
    public int UniqueID
    {
        get
        {
            return CommonHelper.GetUniqueID(gameObject);
        }
    }

    /// <summary>
    /// 死亡事件 
    /// 协程：死亡后缓慢消失  NPC下陷
    /// </summary>
    protected override void OnDead()
    {
       
        base.OnDead();
      
      
        StartCoroutine(Skin());
        CanAttack = 1;
       
    }
    private Vector3 m_SkinDest = Vector3.zero;
    IEnumerator Skin()
    {
      
        
        m_SkinDest = transform.position + Vector3.down * 5;


        yield return new WaitForSeconds(3.0f);

        //如果当前的位置和目标的位置相差0.5米的时候
        while (Mathf.Abs(transform.position.y-m_SkinDest.y)>0.5f)
        {
            transform.position = Vector3.Lerp(transform.position, m_SkinDest, Time.deltaTime * 0.1f);
           
            yield return null;
        }
       
        Destroy(gameObject);
        
        
    }

    


    /// <summary>
    /// 自动战斗
    /// </summary>
    protected override void AutoFightUpdate()
    {
        m_ReFindTime += Time.deltaTime;
        if (m_ReFindTime > m_ReFindTargetTime)
        {
            m_ReFindTime = 0;
            m_Target = null;
        }
        if (IsDead||CanAttack>0)
        {
           // OnDead();
            return;
        }
       
     
     
        if (m_CurSelectedSkill == null)
        {
            m_CurSelectedSkill = CurAttackNpc.GetCanUseSkill();
        }

      
       
        //根据技能选择一个目标
        SelectTartgetBySkill();

        if (m_CurSelectedSkill!=null&&m_Target!=null)
        {
           
            //准备攻击
            TryCastSkill();
           
            if (m_NeedMoveTarget)
            {
                if(Hp>0&&!IsDead)
                    MoveTarget();

            }
        }
      
    }

   
}