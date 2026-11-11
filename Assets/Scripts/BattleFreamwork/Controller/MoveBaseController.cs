using UnityEngine;
using UnityEngine.AI;


/// <summary>
/// 所有人物移动的基类
/// </summary>
public class MoveBaseController : MonoBehaviour
{
     
    protected NavMeshAgent m_NavMeshAgent;
    protected CharacterController cc;



    protected Animator m_Animator;
    public float m_Speed = 3.0f;  //移动速度
    protected bool m_CrossFade = true;//动画与动画之间过渡

    /// <summary>
    /// 模型正面朝向系数：模型正面为-Z时保持-1（当前NYT模型如此），正面为+Z的正常模型改为1
    /// 影响手动方向移动的实际位移方向
    /// </summary>
    public float m_MoveForwardSign = -1f;
  
    private void Awake()
    {
        m_NavMeshAgent = gameObject.GetComponent<NavMeshAgent>();
        m_Animator = gameObject.GetComponent<Animator>();
        //m_NavMeshAgent.velocity = Vector3.zero;
        //m_NavMeshAgent.stoppingDistance = 3.0f;
        cc = gameObject.GetComponent<CharacterController>();
    }


    protected virtual void Start()
    {
        
    }

    protected virtual void Update()
    {

        if (Input.GetKeyDown(KeyCode.A))
        {
            SetDestination(transform.forward * 30);
        }
    }

   


    /// <summary>
    /// 设置移动目标点
    /// </summary>
    /// <param name="pos"></目标位置>
    public void SetDestination(Vector3 pos)
    {
        if (m_NavMeshAgent != null)
            m_NavMeshAgent.SetDestination(pos);
       
    }

    /// <summary>
    /// 停止移动
    /// </summary>
    protected void StopMove()
    {

        SetDestination(transform.position);
    }

    /// <summary>
    /// 停用导航 —— 只在死亡时调用，比 StopMove 彻底得多。
    ///
    /// 【为什么必须彻底禁用，而不是 StopMove】
    ///   StopMove 只是把 destination 设成当前位置，**组件仍然 enabled**。
    ///   而 NavMeshAgent 只要 enabled，每帧都会把单位吸附回导航网格表面，
    ///   等于持续重写 transform.position —— 谁在下沉都被它拽回来。
    ///
    /// 【它顺带掐掉了另一条链路】
    ///   <see cref="OnAnimatorMove"/> 里已有 `if (m_NavMeshAgent.enabled)` 这道闸门，
    ///   本方法把 enabled 置 false 之后，死亡动画每帧的 Root Motion 回调就再也进不到
    ///   NavmeshMove()，也就不会再有导航位移去覆盖下沉。
    ///
    /// 【顺序】先清零速度再停工位：isStopped 之后 velocity 可能已被锁为 0，
    ///   但反过来先置 velocity 更直观，也避免残留标量速度。
    /// </summary>
    protected void StopNavigation()
    {
        if (m_NavMeshAgent == null)
        {
            return;
        }

        m_NavMeshAgent.velocity = Vector3.zero;
        m_NavMeshAgent.isStopped = true;
        m_NavMeshAgent.enabled = false;
    }

    /// <summary>
    /// 旋转至正对目标
    /// </summary>
    /// <param name="pos"></param>
    public void RotateToTarget(Vector3 pos)
    {
        //物体pos相对于物体transform的位置, 
        //换句话说, 就是把物体transform当做了世界的中心,pos相对于新的世界中心所返回的位置
        Vector3 relative = transform.InverseTransformPoint(pos);
        
        //计算获得transform与目标之间的角度
        float angle = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;
        transform.Rotate(Vector3.up * angle);
    }

    #region 移动部分控制代码
    /// <summary>
    /// 是否需要移动
    /// 如果玩家和目标之间的距离小于等于玩家到达终点的距离  返回false,否则返回True
    /// </summary>
    public bool NeedMove
    {
        get
        {
            if (m_NavMeshAgent.remainingDistance <= m_NavMeshAgent.stoppingDistance)
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 当游戏人物有Animator组件时，系统会在每一帧调用这个方法。
    /// </summary>
    private void OnAnimatorMove()
    {

        //动画播放速度
        if (m_Animator.speed == 0|| m_NavMeshAgent==null)
        {
            return;
        }
            //是否需要移动
            if (m_NavMeshAgent.enabled)
            {
                //如果需要移动
                if (NeedMove)
                {
                    //获得当前层的动画信息
                    AnimatorStateInfo stateInfo = m_Animator.GetCurrentAnimatorStateInfo(0);

                    //获取动画的BOOL值，如果动画的BOOL值Run返回的是True,表示在播放移动的动画.否则是在站立状态
                    //如果移动动画返回的是false修改动画状态为True

                    Debug.LogError(4444);
                    if (!m_Animator.GetBool("Move") /*||this is AttackNpcController*/)
                    {
                        m_Animator.SetBool("Move", true);

                    }
                    //如果动画处于过渡阶段
                    if (m_CrossFade)
                    {
                        //如果当前处于RunAttB动画，并且m_Animator.IsInTransition 动画是否在过渡

                        if (stateInfo.IsName("RunAttB") && !m_Animator.IsInTransition(0)/*|| this is AttackNpcController*/)
                        {
                            //进行移动
                            NavmeshMove();
                        }
                    }
                    else
                    {
                        NavmeshMove();
                    }
                }
                //如果不需要移动
                else
                {
                    //如果当前移动动画处于移动状态的
                    if (m_Animator.GetBool("Move"))
                    {
                        //将速度归0
                        m_NavMeshAgent.velocity = Vector3.zero;
                        //将移动动画设置为FLASE
                        m_Animator.SetBool("Move", false);
                    }
                }
            }
    }

    /// <summary>
    /// 导航移动并旋转
    /// </summary>
    public void NavmeshMove()
    {
        /*当对游戏对象的Tag进行比对时，从性能考虑，可使用CompareTag方法，不建议使用双等号进行判断。
         * 
         */
        if (/*gameObject.tag != "Player"*/!gameObject.CompareTag("Player"))
        {
            /*Vector3(2.5,32.5299988,-60)
             * m_NavMeshAgent.steeringTarge目标向量（目标与世界原点的向量）
            *transform.position 自身向量（玩家自身与世界远点的向量）
            *m_NavMeshAgent.steeringTarget - transform.position=目标与玩家之间的向量
            */
            Vector3 looat = m_NavMeshAgent.steeringTarget - transform.position;
            looat.y = 0;
            if (looat != Vector3.zero)
            {
                //根据目标与玩家之间的向量用四元数来进行旋转
                //Quaternion.LookRotation 使用指定的向前方向创建一个四元数
                Quaternion to = Quaternion.LookRotation(looat, Vector3.up);//返回的是一个四元数，也就是一个角度
                                                                           //旋转，自transform.rotation旋转to的角度，每次旋转m_Speed*4f的度数
                transform.rotation = Quaternion.RotateTowards(transform.rotation, to, m_Speed * 4.0f);
            }
            m_NavMeshAgent.Move(transform.forward * m_Speed * Time.deltaTime);
        }
        else
        {
            m_NavMeshAgent.Move(new Vector3(0, 0, m_Speed * Time.deltaTime));
        }
    }

   
        #endregion

    #region 手动方向移动（摇杆等方向输入的统一执行入口）

    /// <summary>
    /// 按方向手动移动（与NavMesh移动同级的第二种移动方式，供摇杆等方向输入调用）
    /// 负责旋转、位移与移动动画
    /// </summary>
    /// <param name="direction">世界XZ平面方向（内部归一化）</param>
    /// <param name="speedScale">速度缩放0~1（摇杆力度）</param>
    public void MoveByDirection(Vector3 direction, float speedScale)
    {
        direction.y = 0;
        if (direction.sqrMagnitude > 0.001f)
        {
            //转向速度比NavmeshMove更快，保证手动操作跟手
            Quaternion to = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, to, 720.0f * Time.deltaTime);
        }

        CharacterController controller = GetCharacterController();
        if (controller != null)
        {
            //沿模型正面移动（m_MoveForwardSign处理模型正面朝-Z的情况）
            //当前场景没有地面，用Move不做重力，避免坠落
            controller.Move(transform.forward * m_MoveForwardSign * m_Speed * speedScale * Time.deltaTime);
            if (m_Animator != null && !m_Animator.GetBool("Run"))
            {
                m_Animator.SetBool("Run", true);
            }
        }
    }

    /// <summary>
    /// 停止手动移动：关闭移动动画
    /// </summary>
    public void StopManualMove()
    {
        if (m_Animator != null && m_Animator.GetBool("Run"))
        {
            m_Animator.SetBool("Run", false);
        }
    }

    /// <summary>
    /// 获取角色控制器：模型上没有时动态补一个，尺寸与CapsuleCollider对齐
    /// </summary>
    protected CharacterController GetCharacterController()
    {
        if (cc == null)
        {
            cc = GetComponent<CharacterController>();
        }
        if (cc == null)
        {
            cc = gameObject.AddComponent<CharacterController>();
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                cc.radius = capsule.radius;
                cc.height = capsule.height;
                cc.center = capsule.center;
            }
            else
            {
                cc.radius = 0.5f;
                cc.height = 2.0f;
                cc.center = new Vector3(0, 1.0f, 0);
            }
        }
        return cc;
    }

    #endregion

}
