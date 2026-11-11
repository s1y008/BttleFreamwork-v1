using UnityEngine;

using System.Collections;
using System.Collections.Generic;


public class WarrorController : CombatUnitController {
    public Warrior CurWarrior
    {
        get
        {
            return m_CurCombatUnit as Warrior;
        }
    }
  

    public override int EnemyLayer
    {
        get
        {
            return 1 << LayerMask.NameToLayer("AttackNpc");
        }
    }

    /// <summary>
    /// 敌方Tag
    /// </summary>
    public override string EnemyTag { get { return BattleConst.tag_NPC; } }

   

    protected override void Start()
    {
     
        CanAttack = 0;
        CanbeAttack = 0;
        
        base.Start();

    }

    

    protected override void OnDead()
    {
        base.OnDead();
        CanbeAttack = 1;
        CanAttack = 1;
    }

  

    /// <summary>
    /// 自动战斗Update;
    /// </summary>
    protected override void AutoFightUpdate()
    {
     
        //摇杆有输入时由玩家接管移动，跳过自动索敌攻击
        if (JoystickMoveUpdate())
        {
            return;
        }

       

        if (!IsIdleToUseSkill) return;

        bool pressedA = Input.GetKeyDown(KeyCode.A);

        if (pressedA)
        {
            //释放攻击技能
            m_CurSelectedSkill ??= CurWarrior.GetSkill(10002);
        }
        
       

        SelectTartgetBySkill();

        if (m_CurSelectedSkill != null && m_Target != null)
        {

            TryCastSkill();
            //if (m_NeedMoveTarget)
            //{
            //    MoveTarget();
            //    if (m_Animator.GetInteger("SkillId")>0)
            //    {
            //        m_Animator.SetInteger("SkillId", 0);
            //    }
            //}
        }

        if (m_Target == null)
        {
            // ★ v5 P13：目标是空 → 回待机动画。画面改写一律经 View（Controller 不再直接持有 Animator）
            View.ResetSkillId();
        }
      
    }

    /// <summary>
    /// ★ 2026-09-29：玩家「一次按键 = 一次释放」。
    ///
    /// 【问题】m_CurSelectedSkill 一旦被选中就常驻非空（按 A 只负责选中，不负责释放），
    ///   于是 AutoFightUpdate 每帧都满足 `m_CurSelectedSkill != null && m_Target != null`，
    ///   每帧 TryCastSkill → StarSKill → RequestState(Attack) ⇒ **按一次 A 就无限连击**。
    ///
    /// 【修复】AttackState 收尾时（段数打完或动画播完，谁先到谁触发）回调到这里，
    ///   清空当前技能 ⇒ 下一帧 `m_CurSelectedSkill == null`，必须再按一次 A 才能出手。
    ///
    /// 【为什么不给基类加 setter】见 CombatUnitController.OnSkillCastFinished 的注释。
    /// </summary>
    public override void OnSkillCastFinished()
    {
        m_CurSelectedSkill = null;
    }

   

    #region 摇杆输入（输入读取留在玩家层，移动执行在MoveBaseController.MoveByDirection）

    /// <summary>
    /// 摇杆输入读取：有输入时调用基类MoveByDirection执行移动，返回true表示本帧由摇杆接管移动
    /// </summary>
    private bool JoystickMoveUpdate()
    {
        VirtualJoystick joystick = VirtualJoystick.Instance;
        if (joystick == null || !joystick.IsDragging || joystick.Direction == Vector2.zero)
        {
            //无摇杆输入时确保移动动画关闭
            StopManualMove();
            return false;
        }

        //摇杆平面方向映射到世界XZ平面，移动执行交由基类
        MoveByDirection(new Vector3(joystick.Direction.x, 0, joystick.Direction.y), joystick.Magnitude);
        return true;
    }

    #endregion

}
