#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 死亡表现自检探针 —— ★ v5 P15 / P15-fix / P15-sink（2026-09-18）**验证专用**，不参与正式构建。
///
/// 【为什么需要它】
///   要回答「死亡动画 + 缓慢下沉是否都在跑」，静态看 YAML 拓扑不够：
///   Animator 有没有真的完成转换、会不会被回边拉回 idle、物体有没有真的往下走，
///   只有把游戏跑起来逐帧观测才有答案。而自动化侧（UnitySkills）没有任意代码执行能力，
///   只能触发菜单，所以要有一个对象挂进 Play 场景替我们采样。
///
/// 【为什么不用 EditorApplication.update】
///   实测：编辑器窗口不在前台时该回调压根不跑（一次都没进），采样全空。
///   MonoBehaviour.Update 在 Play 中一定按帧执行，与窗口焦点无关。
///
/// 【★ 待命模式（Armed）为什么必须有】
///   上一版是「菜单里立刻 DoDead()」，它证明了希望 *身份验证后* OnDead 链路的下沉是好的
///   （累计 4.5m 后 Destroy），但**证明不了玩家真刀真枪把怪打死时也是好的** ——
///   真实致死走的是 ApplyHit → RequestState(Dead) → DeadState.OnEnter → DoDead，中间多两步。
///   所以本版改成**先待命、检测到单位真的死了再开始采样**，用自然战斗数据说话。
///
/// 【★ 为什么要同时盯 Root Motion】
///   「缓慢沉入地面」由 Skin() 协程**每帧写 transform.position** 实现，
///   只要还有别的系统在写同一个 position（Animator 的 Root Motion、NavMeshAgent 贴地吸附），
///   下沉就会被静默覆盖。这种冲突在代码里看不出来，必须采样 y 轨迹并打印嫌疑值。
///
/// 【判定标准】
///   ① 采样期间进入过 Die 且最后仍停在 Die ⇒ 死亡动画在播；
///   ② 累计下沉量 ≥ 阈值 ⇒ 下沉真的发生。
/// </summary>
public class DeathLinkProbe : MonoBehaviour
{
    /// <summary>被观测单位的 Animator</summary>
    public Animator Target;

    /// <summary>被观测的战斗单位（用来判 IsDead；与 Target 必须是同一 GameObject）</summary>
    public CombatUnitController Unit;

    /// <summary>采样间隔（秒）。用时间而非帧数：帧率波动时按帧采样会让总时长失控。</summary>
    public float SampleInterval = 2.0f;

    /// <summary>采样次数上限；20 次 × 2 秒 = 40 秒，足以覆盖 Skin() 的 3 秒等待 + 下沉全过程。</summary>
    public int MaxSamples = 20;

    /// <summary>判定「确实下沉了」的最小位移（米）</summary>
    public float SinkThreshold = 0.3f;

    /// <summary>true = 等目标自然死亡后再采样；false = 立刻采样（兼容手动触发的场景）</summary>
    public bool WaitForNaturalDeath = true;

    private const string ST_DIE = "Die";

    private float _nextSampleTime;
    private float _nextHeartbeat;
    private int _samples;
    private bool _armed;
    private bool _enteredDie;
    private bool _lastInDie;

    private float _startY;
    private float _minY;
    private NavMeshAgent _agent;

    // ★ 每次采样都刷新，供 FinalReport 兜底输出 —— 控制台滚动缓冲会吃掉中间的 #N 采样行
    private bool _lastLoop;
    private float _lastNt;

    private void Start()
    {
        if (Target == null || Unit == null)
        {
            Debug.LogError("[SinkDiag] 探针没有获得完整参数（Target / Unit），已自毁");
            Destroy(gameObject);
            return;
        }

        // ⚠ 先把「描述字符串」算出来再拼进日志：本项目用的 C# 版本不允许在插值表达式里
        //   再套带引号的字符串 / 三元表达式（那样会被 ':' 提前终止插值，编译报 CS8361）。
        _agent = Target.GetComponent<NavMeshAgent>();
        string agentLabel = _agent != null ? (_agent.enabled ? "启用" : "存在但禁用") : "无";

        _nextHeartbeat = Time.time + 5f;
        Debug.Log($"[SinkDiag] 待命 单位={Target.name} 起始y={Target.transform.position.y:F3} " +
                  $"applyRootMotion={Target.applyRootMotion} NavMeshAgent={agentLabel} " +
                  $"（等它自然死亡后再开始采样）");
    }

    private void Update()
    {
        if (Target == null)
        {
            Debug.LogWarning($"[SinkDiag] 观测目标已销毁 —— 累计下沉 {(_startY - _minY):F3}m");
            FinalReport("目标销毁");
            Destroy(gameObject);
            return;
        }

        // ── 待命阶段：等单位真的死 ───────────────────────────
        if (_armed == false)
        {
            if (Unit != null && Unit.IsDead)
            {
                _armed = true;
                _agent = Target.GetComponent<NavMeshAgent>();
                _startY = Target.transform.position.y;
                _minY = _startY;
                _nextSampleTime = Time.time + SampleInterval;

                Debug.Log($"[SinkDiag] ▶ 探测到自然死亡 单位={Target.name} " +
                          $"IsDead={Unit.IsDead} 起始y={_startY:F3} —— 开始采样");
                return;
            }

            // 心跳：证明探针活着、且目标确实还没死（避免"没数据"被误读成"下沉没了"）
            if (Time.time >= _nextHeartbeat)
            {
                _nextHeartbeat = Time.time + 5f;
                string hpLabel = Unit != null ? Unit.Hp.ToString() : "?";
                Debug.Log($"[SinkDiag] 待命中 单位={Target.name} HP={hpLabel} y={Target.transform.position.y:F3}");
            }

            return;
        }

        // ── 采样阶段 ──────────────────────────────────────
        // ★ 下沉量必须**逐帧**统计：Skin 协程每帧只写一小小段位移，
        //   若只在采样点看瞬时 y，恰好落在写入前后会看错数量级。
        float y = Target.transform.position.y;
        if (y < _minY)
        {
            _minY = y;
        }

        if (Time.time < _nextSampleTime)
        {
            return;
        }

        _nextSampleTime = Time.time + SampleInterval;
        _samples++;

        AnimatorStateInfo st = Target.GetCurrentAnimatorStateInfo(0);
        bool inDie = st.IsName(ST_DIE);
        if (inDie)
        {
            _enteredDie = true;
        }
        _lastInDie = inDie;

        // ★ 「死亡动画有没有在无限重复」必须写进**结论行**。
        //   控制台是滚动缓冲，战斗日志量大时中间的 #N 采样会被挤掉，
        //   只留下结论 —— 关键判据若只存在于采样行里，等于白测。
        _lastLoop = st.loop;
        _lastNt = st.normalizedTime;

        // ★ 区分「播一遍后停在最后一帧」与「无限重复倒地」的唯一可靠判据是 stateInfo.loop，
        //   不是 normalizedTime —— 实测 nt 会以 dt/时长 的速率一路涨到 20 以上，
        //   循环与不循环都是这个涨法，用它判断会得出完全错误的结论。
        //   循环与否决定两件事：① 是否 Body 会周期性地回弹重播倒地动作
        //   ② 那段看不见的回弹会不会把「缓慢下沉」的观感彻底盖住。
        //   hips 高度作为交叉验证：不循环时应恒定，循环时应周期性回落。
        //   ⚠ GetBoneTransform 只认 **Humanoid** Avatar，敌人这套骨骼是 Generic，
        //     直接调会每帧抛 InvalidOperationException —— **为了一个锦上添花的诊断字段
        //     把控制台刷满 Error 是不能接受的**（连 Error 本身的含义都被稀释了），
        //     所以这里必须吞异常降级为 "n/a"。
        string hipsLabel = "n/a";
        try
        {
            Transform hips = Target.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
            {
                hipsLabel = hips.position.y.ToString("F3");
            }
        }
        catch (System.Exception)
        {
            hipsLabel = "非Humanoid";
        }

        Debug.Log($"[SinkDiag] #{_samples} t={Time.time:F1}s y={y:F3} 已沉={(_startY - _minY):F3} " +
                  $"inDie={inDie} loop={st.loop} nt={st.normalizedTime:F2} hipsY={hipsLabel} " +
                  $"rmDeltaY={Target.deltaPosition.y:F4} rootMotion={Target.applyRootMotion}");

        if (_samples >= MaxSamples)
        {
            FinalReport("采样次数用尽");
            Destroy(gameObject);
        }
    }

    private void FinalReport(string reason)
    {
        bool animOk = _enteredDie && _lastInDie;
        float sink = _startY - _minY;
        bool sinkOk = sink >= SinkThreshold;

        Debug.Log($"[SinkDiag] 结论（{reason}）死亡动画：{(animOk ? "PASS" : "FAIL")}" +
                  $"（进入过Die={_enteredDie} 最后仍在Die={_lastInDie}）；" +
                  $"缓慢下沉：{(sinkOk ? "PASS" : "FAIL")}（累计 {sink:F3}m / 阈值 {SinkThreshold}m）；" +
                  $"死亡动画是否循环={_lastLoop}（False=播完定格，True=反复倒地）nt={_lastNt:F2}");
    }
}
#endif
