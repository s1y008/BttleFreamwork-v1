using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 敌人（哥布林 GBL）受击动画状态机的**建造器** —— ★ v5 P15（2026-09-18）
///
/// 【为什么要有这个脚本】
///   本工程无 git，而 `.controller` 是 Unity 的 YAML 资源 —— 手写 YAML 要自己分配 fileID、
///   维护双向引用（状态 ↔ 转换 ↔ 条件），错一处就是「控制器打不开」。
///   所以资源改动一律走 **Unity 官方 Editor API**（UnityEditor.Animations），由 Unity 自己序列化。
///   又因为自动化侧（UnitySkills）**没有任意代码执行能力**，只能调菜单，
///   所以把改动封装成 [MenuItem]，编译后由外部 `editor_execute_menu` 触发。
///
/// 【它做了什么（一次说清）】
///   A1 参数：新增 Hit(Trigger) / HitType(Int) / Move(Bool)
///   A2 状态：在 Hit 子状态机里新建 LightHit（绑定现成的 @LightHit.FBX）
///   A3 进入：idle → Hit(SM) [Hit]（**插到 idle 转换列表首位**）+ Any State → Hit(SM) [Hit]
///   A4 分流：Hit 内部 Entry → LightHit [HitType Equals 1]；ContiHit 保持为默认兜底
///   A5 回归：LightHit → idle / ContiHit → idle（ExitTime），播完回常态
///
/// 【为什么入口要做两条（idle 一条 + Any State 一条）】
///   ⚠ GBL 的 idle 上挂着 4 条**真正的无条件出边**（hasExitTime=0 且无条件的
///     Run_F / Down / FloatHit / Die）。Unity 对「当前状态的转换」与「Any State 转换」
///     谁优先没有公开保证，一旦无条件边抢在前面，受击动画就永远播不出来。
///     所以两条保险都要有：
///       · idle → Hit 放在 idle 转换列表 **index 0** ⇒ 在 idle 状态下一定先被判定；
///       · Any State → Hit ⇒ 处在 Run_F / Skill20014 / 其它受击态时也能被打断。
///   这样无论 Unity 的 AnyState 优先级规则如何，受击都进得去。
///
/// 【幂等性（铁律）】
///   菜单可能被重复执行。所有「新增」前都先查是否已存在，存在则跳过，绝不翻倍插入。
/// </summary>
public static class GBLAnimatorBuilder
{
    private const string CTRL_PATH = "Assets/ArtResources/AnimatorController/Enemy/GBL.controller";
    private const string FBX_DIR = "Assets/ArtResources/Model/Enemy/哥布林NEW（小）/";

    private const string SM_HIT = "Hit";
    private const string ST_IDLE = "idle";
    private const string ST_LIGHT_HIT = "LightHit";
    private const string ST_CONTI_HIT = "ContiHit";

    private const string ST_DIE = "Die";
    private const string PARAM_DIE = "Die";

    // ★ 定格版死亡动画：独立 .anim 资源，**一个字节都不动原 FBX**
    //   路径放在原 FBX 同目录，语义上标明「这是 Die 的定格副本」。
    private const string DIE_CLAMPED_NAME = "Die_ClampForever";
    private const string DIE_CLAMPED_PATH = "Assets/ArtResources/Model/Enemy/哥布林NEW（小）/Die_ClampForever.anim";

    private const string PARAM_HIT = "Hit";
    private const string PARAM_HIT_TYPE = "HitType";
    private const string PARAM_MOVE = "Move";

    /// <summary>死亡动画所在的 FBX（★ P15-sink：用来关掉它的循环）</summary>
    private const string DIE_FBX_PATH = "Assets/ArtResources/Model/Enemy/哥布林NEW（小）/R_Monster_Gebuling@Die.FBX";

    [MenuItem("BattleTools/GBL - 建造受击状态与转换")]
    public static void Build()
    {
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL_PATH);
        if (ctrl == null)
        {
            Debug.LogError("[AnimBuild] 找不到控制器：" + CTRL_PATH);
            return;
        }

        // ── A1 参数 ──────────────────────────────────────────────
        EnsureParameter(ctrl, PARAM_HIT, AnimatorControllerParameterType.Trigger);
        EnsureParameter(ctrl, PARAM_HIT_TYPE, AnimatorControllerParameterType.Int);
        EnsureParameter(ctrl, PARAM_MOVE, AnimatorControllerParameterType.Bool);

        AnimatorStateMachine root = ctrl.layers[0].stateMachine;

        AnimatorStateMachine hit = FindSubStateMachine(root, SM_HIT);
        AnimatorState idle = FindState(root, ST_IDLE);
        if (hit == null)
        {
            Debug.LogError("[AnimBuild] 找不到子状态机 " + SM_HIT);
            return;
        }
        if (idle == null)
        {
            Debug.LogError("[AnimBuild] 找不到状态 " + ST_IDLE);
            return;
        }

        // ── A2 新建 LightHit 状态 ────────────────────────────────
        AnimatorState lightHit = FindState(hit, ST_LIGHT_HIT);
        if (lightHit == null)
        {
            lightHit = hit.AddState(ST_LIGHT_HIT, new Vector3(360f, -60f, 0f));
            lightHit.motion = LoadClip("R_Monster_Gebuling@LightHit.FBX", ST_LIGHT_HIT);
            Debug.Log("[AnimBuild] 新建状态 " + ST_LIGHT_HIT +
                      " clip=" + (lightHit.motion != null ? lightHit.motion.name : "null"));
        }
        else
        {
            Debug.Log("[AnimBuild] 状态 " + ST_LIGHT_HIT + " 已存在，跳过");
        }

        // ── A3 进入 Hit 子树（两条保险）─────────────────────────
        if (!HasTransitionToStateMachine(idle.transitions, hit))
        {
            AnimatorStateTransition t = idle.AddTransition(hit);
            t.AddCondition(AnimatorConditionMode.If, 0, PARAM_HIT);
            t.hasExitTime = false;
            t.duration = 0.05f;
            MoveTransitionToFront(idle, t);
            Debug.Log("[AnimBuild] 新增 idle → Hit(SM) [Hit]，并置为 idle 首条转换");
        }
        else
        {
            Debug.Log("[AnimBuild] idle → Hit(SM) 已存在，跳过");
        }

        if (!HasTransitionToStateMachine(root.anyStateTransitions, hit))
        {
            AnimatorStateTransition t = root.AddAnyStateTransition(hit);
            t.AddCondition(AnimatorConditionMode.If, 0, PARAM_HIT);
            t.hasExitTime = false;
            t.duration = 0.05f;
            t.canTransitionToSelf = true; // 连击时允许重新进入（三连斩每段都要重播受击）
            Debug.Log("[AnimBuild] 新增 Any State → Hit(SM) [Hit]");
        }
        else
        {
            Debug.Log("[AnimBuild] Any State → Hit(SM) 已存在，跳过");
        }

        // ── A4 Hit 内部分流 ──────────────────────────────────────
        if (!HasEntryTransitionTo(hit, lightHit))
        {
            AnimatorTransition et = hit.AddEntryTransition(lightHit);
            et.AddCondition(AnimatorConditionMode.Equals, 1, PARAM_HIT_TYPE);
            Debug.Log("[AnimBuild] 新增 Hit 内部 Entry → LightHit [HitType Equals 1]");
        }
        else
        {
            Debug.Log("[AnimBuild] Entry → LightHit 已存在，跳过");
        }

        // ── A5 播完回常态 ────────────────────────────────────────
        EnsureExitToIdle(lightHit, idle);

        // ContiHit 是 Hit 子树的默认状态（HitType 未命中任何分支时的兜底）。
        // 它原本没有出边 —— 一旦真的被兜底进入就会**永久卡住**，所以补一条回 idle 的安全阀。
        AnimatorState contiHit = FindState(hit, ST_CONTI_HIT);
        if (contiHit != null)
        {
            EnsureExitToIdle(contiHit, idle);
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        Debug.Log("[AnimBuild] GBL 受击状态与转换建造完成：参数 " + ctrl.parameters.Length + " 个");
    }

    //═════════════════════════ 死亡链路修复（★ v5 P15-fix）════════════════════════

    /// <summary>
    /// 修复「敌人死亡动画从不播放」这个迁移前就存在的缺陷。
    ///
    /// 【病因三条，缺一不可地同时存在】
    ///   ① GBL.controller 的 Die 参数是 **Bool**，而旧的 UnitView.PlayDeath 写的是
    ///      <c>SetTrigger("Die")</c> —— 对 Bool 参数设触发器不产生任何效果；
    ///   ② idle → Die 这条边是**无条件**的，但排在 idle 转换列表末位，
    ///      前面 Run_F / Down / FloatHit 三条同样无条件 ⇒ 轮不到它（这条边从来没生效过）；
    ///   ③ Die 状态有一条 **Die → idle [ExitTime]** 的回边，即使真进去了也会在播完后回到待机。
    ///
    /// 【对应的三个动作】
    ///   D1 给 idle → Die 补上 [Die == true]（把它从一条抢不到的脏边变成语义正确的死亡入口）；
    ///   D2 新增 **Any State → Die [Die == true]** —— 死亡必须能打断受击 / 跑动 / 施法，
    ///      且 <c>canTransitionToSelf = false</c>，避免已在 Die 状态时反复重播死亡动画；
    ///   D3 给 Die → idle 补上 [Die == false] —— 死了就不能再回待机。
    ///      ⚠ 如果只做 D2 不做 D3，会形成「Die 播完 → 回 idle → AnyState 再拉回 Die」的**死亡无限循环**。
    ///
    /// 【幂等】三处都先判定已存在 / 已有条件再动手。
    /// </summary>
    [MenuItem("BattleTools/GBL - 修复死亡链路（Die）")]
    public static void FixDeathLink()
    {
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL_PATH);
        if (ctrl == null)
        {
            Debug.LogError("[DeathBuild] 找不到控制器：" + CTRL_PATH);
            return;
        }

        // ── D0 参数：Die 必须是 Bool ─────────────────────────────
        EnsureParameter(ctrl, PARAM_DIE, AnimatorControllerParameterType.Bool);
        AnimatorControllerParameter[] ps = ctrl.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].name == PARAM_DIE && ps[i].type != AnimatorControllerParameterType.Bool)
            {
                Debug.LogError("[DeathBuild] Die 参数类型是 " + ps[i].type + "，代码侧按 Bool 写入 —— 请先把资产改回 Bool");
                return;
            }
        }

        AnimatorStateMachine root = ctrl.layers[0].stateMachine;
        AnimatorState idle = FindState(root, ST_IDLE);
        AnimatorState die = FindState(root, ST_DIE);
        if (idle == null || die == null)
        {
            Debug.LogError("[DeathBuild] 找不到 idle / Die 状态");
            return;
        }

        // ── D1 idle → Die 补条件 [Die == true] ───────────────────
        bool patchedIdle = false;
        AnimatorStateTransition[] idleTs = idle.transitions;
        for (int i = 0; i < idleTs.Length; i++)
        {
            AnimatorStateTransition t = idleTs[i];
            if (t == null || t.destinationState != die)
            {
                continue;
            }
            if (HasDieCondition(t))
            {
                patchedIdle = true;
                continue;
            }
            t.AddCondition(AnimatorConditionMode.If, 0, PARAM_DIE);
            patchedIdle = true;
            Debug.Log("[DeathBuild] idle → Die 补上条件 [Die == true]");
        }
        if (!patchedIdle)
        {
            Debug.Log("[DeathBuild] idle → Die 的条件已存在，跳过");
        }

        // ── D2 Any State → Die [Die == true] ─────────────────────
        if (!HasTransitionToState(root.anyStateTransitions, die))
        {
            AnimatorStateTransition any = root.AddAnyStateTransition(die);
            any.AddCondition(AnimatorConditionMode.If, 0, PARAM_DIE);
            any.hasExitTime = false;
            any.duration = 0.1f;
            // ⚠ 必须为 false：Any State 转换默认允许「目标就是当前状态」，
            //   若不在 Die 上自锁，死亡动画会在每一帧被重新进入，永远重播第一段。
            any.canTransitionToSelf = false;
            MoveAnyTransitionToFront(root, any);
            Debug.Log("[DeathBuild] 新增 Any State → Die [Die == true]（置为 AnyState 首条、禁止自转）");
        }
        else
        {
            Debug.Log("[DeathBuild] Any State → Die 已存在，跳过");
        }

        // ── D3 Die → idle 补条件 [Die == false] ──────────────────
        bool patchedBack = false;
        AnimatorStateTransition[] dieTs = die.transitions;
        for (int i = 0; i < dieTs.Length; i++)
        {
            AnimatorStateTransition t = dieTs[i];
            if (t == null || t.destinationState != idle)
            {
                continue;
            }
            if (HasDieCondition(t))
            {
                patchedBack = true;
                continue;
            }
            t.AddCondition(AnimatorConditionMode.IfNot, 0, PARAM_DIE);
            patchedBack = true;
            Debug.Log("[DeathBuild] Die → idle 补上条件 [Die == false]（阻断死亡循环）");
        }
        if (!patchedBack)
        {
            Debug.Log("[DeathBuild] Die → idle 的条件已存在，跳过");
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        Debug.Log("[DeathBuild] 死亡链路修复完成：Die 参数 Bool=true 时由 Any State 进入死亡态，且不会退回 idle");
    }

    //═════════════════════════ 死亡链路自检（Play 中）════════════════════════

    /// <summary>
    /// Play 模式下的死亡链路自检 —— 走**真实路径**（CombatUnitController.SetDead → View.PlayDeath），
    /// 之后每 0.4 秒采样一次 Animator 当前状态，共采样 8 次（约 3.2 秒）。
    /// 判定标准：**采样期间进入过 Die，且最后一次采样仍停在 Die**。
    /// 最后一次不在 Die 说明死亡动画被回边拉走（典型的死亡循环，正是 D3 要防的）。
    ///
    /// ⚠ 采样由运行时探针 <c>DeathLinkProbe</c> 承担而非 EditorApplication.update ——
    ///   实测后者在编辑器窗口未获焦时**一次都不回调**，会拿到「只有选中日志、没有采样」的假结果。
    /// </summary>
    [MenuItem("BattleTools/GBL - 死亡链路自检（需先进入 Play）")]
    public static void DiagDeath()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[DeathDiag] 未处于播放模式 —— 请先进入 Play 再执行本菜单");
            return;
        }

        // ⚠ 先清旧探针：探针对 Unity 而言是普通场景对象，若上一次会话没有自然走完就被 stop，
        //   它会带着旧 Target 残留下来的引用活着，下次采样时给出「目标是已销毁旧对象」的假结论。
        DeathLinkProbe[] old = Object.FindObjectsOfType<DeathLinkProbe>();
        for (int k = 0; k < old.Length; k++)
        {
            Object.Destroy(old[k].gameObject);
        }
        if (old.Length > 0)
        {
            Debug.Log($"[SinkDiag] 已清理 {old.Length} 个上一轮残留的探针");
        }

        CombatUnitController[] units = Object.FindObjectsOfType<CombatUnitController>();
        for (int i = 0; i < units.Length; i++)
        {
            Animator anim = units[i].GetComponent<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null)
            {
                continue;
            }
            if (anim.runtimeAnimatorController.name != "GBL")
            {
                continue;
            }

            Debug.Log($"[SinkDiag] 选中 [{units[i].name}] IsDead={units[i].IsDead} " +
                      $"y={units[i].transform.position.y:F3} —— 调用 DoDead() 走 **DeadState → OnDead** 真实死亡链路");

            // 先挂探针：DoDead 之后 Animator 的转换要等下一帧才发生，先观测才能抓到全过程
            GameObject probeGo = new GameObject("DeathLinkProbe_" + units[i].name);
            Object.DontDestroyOnLoad(probeGo);
            DeathLinkProbe probe = probeGo.AddComponent<DeathLinkProbe>();
            probe.Target = anim;
            probe.Unit = units[i];

            // ⚠★ P15-sink：这里必须是 DoDead()，不能图省事写 SetDead(true)。
            //   两者差着一个 **AttackNpcController.OnDead() 覆写** —— 敌人「死亡后缓慢沉入地面」
            //   的 Skin() 协程正是在那个覆写里 StartCoroutine 的。直接 SetDead 等于绕过唯一的启动者，
            //   于是上一次自检只会看到「死亡动画播了」而永远看不到下沉，
            //   还容易据此误判成「下沉逻辑丢了」（其实协程一直在，只是没被拉起来）。
            units[i].DoDead();
            return;
        }

        Debug.LogWarning("[DeathDiag] 场景里找不到使用 GBL.controller 的战斗单位");
    }

    /// <summary>
    /// 给场景里**所有** GBL 敌人挂上待命探针，观测它们在**自然战斗中被打死**后的表现。
    ///
    /// 【与上一个菜单的区别】
    ///   上一个菜单是「立刻 DoDead()」，属于**手动致死**；本菜单什么都不做，只挂钩子，
    ///   等玩家把敌人真正打死（ApplyHit → RequestState(Dead) → DeadState → DoDead）后才开始采样。
    ///   之所以要分开：手动致死能证明「下沉协程本身是好的」，
    ///   但它绕过了 RequestState / DeadState 这两步 —— 真实路径万一在这两步上出问题，
    ///   手动触发是看不出来的（这正是用户反馈只有死亡动画、没有下沉时需要排查的地方）。
    ///
    /// 【为什么要给所有敌人挂而不是第一个】
    ///   场景里通常有一批敌人，谁先被打死不确定；挑一个可能整局都没轮到它。
    /// </summary>
    [MenuItem("BattleTools/GBL - 挂下沉观测探针（自然死亡，需先 Play）")]
    public static void ArmSinkProbe()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[SinkDiag] 未处于播放模式 —— 请先进入 Play 再执行本菜单");
            return;
        }

        CombatUnitController[] units = Object.FindObjectsOfType<CombatUnitController>();
        int armed = 0;
        for (int i = 0; i < units.Length; i++)
        {
            Animator anim = units[i].GetComponent<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null)
            {
                continue;
            }
            if (anim.runtimeAnimatorController.name != "GBL")
            {
                continue;
            }

            // ★ 为什么不干脆直接 DoDead：那样就绕过了 ApplyHit → RequestState(Dead) → DeadState，
            //   而用户看到的现象恰恰需要在这两步上取证。这里只把血量削到「挨一下就死」，
            //   致死仍然由真实的伤害结算走到 —— 路径完整，只是少等几分钟。
            //   ⚠ 不能设成 0：Hp setter 里 HP==0 会直接发 OnDead 事件、绕开 FSM；设成一个小正数最安全。
            units[i].Hp = 10;

            GameObject probeGo = new GameObject("SinkProbe_" + units[i].name);
            Object.DontDestroyOnLoad(probeGo);
            DeathLinkProbe probe = probeGo.AddComponent<DeathLinkProbe>();
            probe.Target = anim;
            probe.Unit = units[i];
            probe.WaitForNaturalDeath = true;
            armed++;
        }

        Debug.Log(armed > 0
            ? $"[SinkDiag] 已给 {armed} 个 GBL 敌人挂上待命探针 —— 现在让它们自然死亡即可观测"
            : "[SinkDiag] 场景里没有使用 GBL.controller 的战斗单位");
    }

    //═════════════════════════ 死亡动画改为不循环（★ P15-sink 2026-09-18）════════════════════════

    /// <summary>
    /// 把 Die 片段的 <c>loopTime</c> 关掉 —— 死亡动画播完应**定格在最后一帧**，而不是反复重播。
    ///
    /// 【为什么要做】
    ///   死亡链路修好之前 SetTrigger("Die") 对 Bool 参数无效，Die 状态根本进不去，
    ///   于是「Die 片段其实是循环的」这件事从来没被看见。链路一修通，
    ///   实测 normalizedTime 一路涨到 **21**（同一段倒地动画重复播了 20 多遍）：
    ///   单位一边不停抽搐着重复倒地，一边按 Skin() 协程缓慢下沉 ——
    ///   观感上就是「只有死亡动画在播，人没沉下去」。
    ///
    /// 【为什么不顺手在代码里冻结 Animator】
    ///   循环是**片段自身的导入属性**，在资源上修一次就一劳永逸；
    ///   在代码里禁用 Animator 只是绕过症状，还会把「动画其实还在跑」这件事实藏起来。
    ///
    /// 【⚠ 最大的坑：clip 名字】
    ///   显式写 clipAnimations 之后 Unity **只认这里定义出来的 clip**。
    ///   如果名字写错，控制器里 Die 状态的 Motion 引用会变成 Missing，死亡动画直接没动作。
    ///   所以片段名用 <c>AssetDatabase.LoadAllAssetsAtPath</c> 读出来的**真实 clip 名**，不猜。
    ///   改完再重新读取做一次自校验（片段数量 / 名字 / 长度），并把结论打进日志。
    ///
    /// 【幂等】已是 loopTime=0 就跳过。
    /// </summary>
    [MenuItem("BattleTools/GBL - Die 动画改为不循环")]
    public static void FixDieLoop()
    {
        AnimationClip clip = LoadDieClip("读取现状");
        if (clip == null)
        {
            return;
        }

        Debug.Log($"[AnimBuild] Die 片段：名字={clip.name} 循环={clip.isLooping} " +
                  $"时长={clip.length:F3}s 包装模式={clip.wrapMode}");

        // ⚠ 这里**不能**用 clip.isLooping 做跳过判据。
        //   实测铁证：Die 片段的 isLooping 报告为 False，可运行时 normalizedTime 一路涨到 21，
        //   同一段倒地动画被重复播了 20 多遍 —— 说明真正决定播放行为的不是 loopTime，
        //   而是导入时写进片段的 **wrapMode**（clipAnimations 为空时 Unity 按默认 Loop 生成）。
        //   若在这里判 isLooping 就跳过，等于什么都没改。
        if (!clip.isLooping && clip.wrapMode == WrapMode.ClampForever)
        {
            Debug.Log("[AnimBuild] Die 已是不循环且 wrapMode=ClampForever，跳过（无需重新导入）");
            return;
        }

        ModelImporter imp = AssetImporter.GetAtPath(DIE_FBX_PATH) as ModelImporter;
        if (imp == null)
        {
            Debug.LogError("[AnimBuild] 取不到 ModelImporter：" + DIE_FBX_PATH);
            return;
        }

        ModelImporterClipAnimation[] clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            // 当前是「没有显式 clip」的状态（meta 里 clipAnimations: []），
            // Unity 会按 take 自动生成一条 —— 名字沿用读出来的 clip.name，保证引用不断。
            ModelImporterClipAnimation c = new ModelImporterClipAnimation();
            c.name = clip.name;
            c.takeName = clip.name;
            c.firstFrame = 0;
            c.lastFrame = 0;
            c.loopTime = false;
            c.loopPose = false;
            c.wrapMode = WrapMode.ClampForever;
            clips = new ModelImporterClipAnimation[] { c };

            Debug.Log($"[AnimBuild] meta 里原本没有显式 clip —— 新建同名片段 [{clip.name}] 并关闭循环");
        }
        else
        {
            Debug.Log($"[AnimBuild] 已把 {clips.Length} 个片段的 loopTime / loopPose 关闭，wrapMode 钉成 ClampForever");
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = false;
                clips[i].loopPose = false;
                clips[i].wrapMode = WrapMode.ClampForever;
            }
        }

        imp.clipAnimations = clips;
        EditorUtility.SetDirty(imp);
        imp.SaveAndReimport();
        AssetDatabase.SaveAssets();

        AnimationClip after = LoadDieClip("导入后自校验");
        if (after != null)
        {
            Debug.Log($"[AnimBuild] 校验结果：名字={after.name} 循环={after.isLooping} 时长={after.length:F3}s —— " +
                      (after.isLooping ? "❌ 仍然循环，请检查导入设置" : "✅ 已定格，不会重复播放"));
        }
    }

    /// <summary>
    /// 回滚 Die.FBX 的导入设置（从本次备份还原 meta 并强制重导入）。
    ///
    /// 【为什么需要它】
    ///   改 FBX 的 <c>clipAnimations</c> 是有代价的：一旦新定义出来的片段与旧的对不上，
    ///   所有引用它们的控制器 Motion 会变成 Missing，而且**不报任何错**。
    ///   实测已经踩到 —— 显式写 clipAnimations 之后片段名不一致，Die 状态直接丢了动画来源。
    ///   有了这个菜单，改资产这件事才是**可逆**的。
    /// </summary>
    [MenuItem("BattleTools/GBL - 回滚 Die 导入设置（还原 meta）")]
    public static void RestoreDieImport()
    {
        string meta = DIE_FBX_PATH + ".meta";
        string backup = meta + ".bak.2026-09-18-dieloop";
        if (!System.IO.File.Exists(backup))
        {
            Debug.LogError("[AnimBuild] 找不到备份文件：" + backup);
            return;
        }

        System.IO.File.Copy(backup, meta, true);
        AssetDatabase.ImportAsset(DIE_FBX_PATH, ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();

        Debug.Log("[AnimBuild] 已从备份还原 Die.FBX.meta 并强制重新导入 —— 请紧接着执行「校验 Die 片段引用」确认恢复");
    }

    /// <summary>
    /// 造一个**定格版**的死亡动画资源，并把控制器 Die 状态指到它身上。
    ///
    /// 【为什么这样就能绕开上一条的坑】
    ///   原 FBX **一个字节都不动**（引用链不会断），只是在资产库里多出一个新的 AnimationClip：
    ///   复制原始片段 + <c>wrapMode = ClampForever</c>，然后让 Die 状态改引这个副本。
    ///   控制器是显式把 motion 赋值到“我创建的那份资源”上的，不存在名字对不上的问题。
    ///
    /// 【为什么是 ClampForever 而不是 bool 开关】
    ///   死亡动画应当**播一遍然后停在最后一帧**（尸体趴在地上）。
    ///   ClampForever 正是这个语义；原先的 wrapMode=Default 会按 Loop 处理，
    ///   于是同一段倒地动画被重复播了 20 多遍，把「缓慢下沉」的观感彻底盖住。
    ///
    /// 【幂等】资源已存在且控制器的 Die 已指向它就跳过。
    /// </summary>
    [MenuItem("BattleTools/GBL - 造定格版死亡动画并接到 Die 状态")]
    public static void CreateClampedDieClip()
    {
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL_PATH);
        if (ctrl == null)
        {
            Debug.LogError("[AnimBuild] 找不到控制器：" + CTRL_PATH);
            return;
        }

        AnimatorState dieState = FindState(ctrl.layers[0].stateMachine, ST_DIE);
        if (dieState == null)
        {
            Debug.LogError($"[AnimBuild] 控制器里找不到名为 {ST_DIE} 的状态");
            return;
        }

        Motion cur = dieState.motion;
        if (cur == null)
        {
            Debug.LogError("[AnimBuild] Die 状态的 Motion 当前是 Missing —— 先执行「回滚 Die 导入设置」把引用恢复回来");
            return;
        }

        if (cur.name == DIE_CLAMPED_NAME)
        {
            Debug.Log($"[AnimBuild] Die 状态已经指向 {DIE_CLAMPED_NAME}，跳过");
            return;
        }

        AnimationClip clamped = AssetDatabase.LoadAssetAtPath<AnimationClip>(DIE_CLAMPED_PATH);
        if (clamped == null)
        {
            clamped = Object.Instantiate(cur) as AnimationClip;
            if (clamped == null)
            {
                Debug.LogError("[AnimBuild] 复制原始死亡动画失败");
                return;
            }
            clamped.wrapMode = WrapMode.ClampForever;
            AssetDatabase.CreateAsset(clamped, DIE_CLAMPED_PATH);
            Debug.Log($"[AnimBuild] 已创建定格版资源 {DIE_CLAMPED_PATH}（wrapMode={clamped.wrapMode}）");
        }

        dieState.motion = clamped;
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        Debug.Log($"[AnimBuild] Die 状态 Motion：{cur.name} → {clamped.name}（wrapMode={clamped.wrapMode}）");
    }

    /// <summary>
    /// 校验 Die 片段的引用链是否完好 —— 改 FBX 的导入设置是有代价的：
    /// 一旦 <c>clipAnimations</c> 里定义出来的片段名与旧的对不上，
    /// 控制器里 Die 状态的 Motion 会变成 **Missing**，死亡动画直接消失且不会报任何错。
    /// 本菜单把「FBX 里现在有哪些片段」和「Die 状态引用到了什么」一次打全，用来验收上一次改动。
    /// </summary>
    [MenuItem("BattleTools/GBL - 校验 Die 片段引用")]
    public static void VerifyDieClip()
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(DIE_FBX_PATH);
        int clipCount = 0;
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip c = assets[i] as AnimationClip;
            if (c == null)
            {
                continue;
            }
            clipCount++;
            Debug.Log($"[AnimBuild] FBX 内片段 #{clipCount}：名字={c.name} 时长={c.length:F3}s " +
                      $"循环={c.isLooping} wrapMode={c.wrapMode}");
        }
        Debug.Log($"[AnimBuild] 片段总数 = {clipCount}");

        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL_PATH);
        if (ctrl == null)
        {
            Debug.LogError("[AnimBuild] 找不到控制器：" + CTRL_PATH);
            return;
        }

        for (int i = 0; i < ctrl.layers[0].stateMachine.states.Length; i++)
        {
            ChildAnimatorState cs = ctrl.layers[0].stateMachine.states[i];
            AnimatorState st = cs.state;
            if (st.name != ST_DIE)
            {
                continue;
            }

            Motion m = st.motion;
            string verdict = m == null ? "❌ Missing（引用断了！）" : ("✅ " + m.name);
            Debug.Log($"[AnimBuild] 控制器 Die 状态 → Motion：{verdict}");
        }
    }

    /// <summary>
    /// 强制重新导入 Die.FBX，并顺手把 Die 状态的 Motion 引用接回去。
    ///
    /// 【为什么会断】
    ///   显式写 <c>clipAnimations</c> 之后，Unity 会**只认新定义出来的那条 clip**，
    ///   原先由 take 自动生成的 sub-asset 被替换 —— 而控制器里存的是那个 sub-asset 的
    ///   <c>fileID: 7400000</c>。重建后 ID 对不上，Motion 就变成 Missing，**且不报任何错**。
    ///
    /// 【为什么 samba 直接覆盖 meta 备份不可靠】
    ///   2018 年的老 meta 是旧序列化格式，覆盖回去会让 Unity 重新分配 sub-asset ID，
    ///   同样接不上。正确做法是保留当前 meta（已回滚成 clipAnimations: []），
    ///   只让 Unity **重新导入**，由它按默认 take 重建出 fileID 7400000 的片段。
    ///
    /// 【挑片段的判据】
    ///   清理后应当只剩一个片段；若还有残留占位 clip（通常是整 1.000s），
    ///   取**时长最长**的那个 —— 真动画长度不会是整数。
    /// </summary>
    [MenuItem("BattleTools/GBL - 重导入 Die.FBX 并修复引用")]
    public static void RepairDieReference()
    {
        AssetDatabase.ImportAsset(DIE_FBX_PATH, ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(DIE_FBX_PATH);
        AnimationClip best = null;
        int count = 0;
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip c = assets[i] as AnimationClip;
            if (c == null)
            {
                continue;
            }
            count++;

            string guid;
            long localId;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out guid, out localId);
            Debug.Log($"[AnimBuild] 片段 #{count}：名字={c.name} 时长={c.length:F3}s " +
                      $"fileID={localId} 循环={c.isLooping} wrapMode={c.wrapMode}");

            if (localId == 7400000)
            {
                Debug.Log("[AnimBuild] ↑↑↑ 这条正是控制器原本引用的 fileID 7400000");
            }
            if (best == null || c.length > best.length)
            {
                best = c;
            }
        }
        Debug.Log($"[AnimBuild] 重导入后片段总数 = {count}");

        if (best == null)
        {
            Debug.LogError("[AnimBuild] 重导入后仍然找不到任何 AnimationClip —— 检查该 FBX 的 animationType 是否被关掉");
            return;
        }

        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CTRL_PATH);
        if (ctrl == null)
        {
            Debug.LogError("[AnimBuild] 找不到控制器：" + CTRL_PATH);
            return;
        }

        AnimatorState dieState = FindState(ctrl.layers[0].stateMachine, ST_DIE);
        if (dieState == null)
        {
            Debug.LogError($"[AnimBuild] 控制器里找不到名为 {ST_DIE} 的状态");
            return;
        }

        Motion cur = dieState.motion;
        if (cur != null && cur.name == best.name && Mathf.Abs(cur.averageDuration - best.length) < 0.001f)
        {
            Debug.Log($"[AnimBuild] Die 引用已完好 —— 指向 [{cur.name}] {cur.averageDuration:F3}s，无需修复");
            return;
        }

        Debug.Log($"[AnimBuild] Die 修复：{(cur == null ? "Missing" : cur.name)} → " +
                  $"[{best.name}] {best.length:F3}s wrapMode={best.wrapMode}");
        dieState.motion = best;
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
    }

    /// <summary>从 Die.FBX 里取出唯一那条 AnimationClip（顺带把找不到的情况报清楚）</summary>
    private static AnimationClip LoadDieClip(string stage)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(DIE_FBX_PATH);
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip c = assets[i] as AnimationClip;
            if (c != null)
            {
                return c;
            }
        }

        Debug.LogError($"[AnimBuild] {stage}：在 {DIE_FBX_PATH} 里找不到任何 AnimationClip " +
                       $"（该资源的 importAnimation 可能被关掉了）");
        return null;
    }

    //═════════════════════════ 工具方法 ═════════════════════════

    private static void EnsureParameter(AnimatorController ctrl, string name, AnimatorControllerParameterType type)
    {
        AnimatorControllerParameter[] ps = ctrl.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].name == name)
            {
                Debug.Log("[AnimBuild] 参数 " + name + " 已存在，跳过");
                return;
            }
        }
        ctrl.AddParameter(name, type);
        Debug.Log("[AnimBuild] 新增参数 " + name + " (" + type + ")");
    }

    private static AnimatorStateMachine FindSubStateMachine(AnimatorStateMachine parent, string name)
    {
        ChildAnimatorStateMachine[] children = parent.stateMachines;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].stateMachine != null && children[i].stateMachine.name == name)
            {
                return children[i].stateMachine;
            }
        }
        return null;
    }

    private static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        ChildAnimatorState[] states = sm.states;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].state != null && states[i].state.name == name)
            {
                return states[i].state;
            }
        }
        return null;
    }

    /// <summary>该转换上是否已经带 Die 条件（幂等判定用）</summary>
    private static bool HasDieCondition(AnimatorStateTransition t)
    {
        AnimatorCondition[] cs = t.conditions;
        for (int i = 0; i < cs.Length; i++)
        {
            if (cs[i].parameter == PARAM_DIE)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Any State 列表里是否已有到某状态的转换</summary>
    private static bool HasTransitionToState(IEnumerable<AnimatorStateTransition> transitions, AnimatorState st)
    {
        foreach (AnimatorStateTransition t in transitions)
        {
            if (t != null && t.destinationState == st)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>把某条 Any State 转换挪到列表首位：死亡必须压过受击那条才能生效。</summary>
    private static void MoveAnyTransitionToFront(AnimatorStateMachine sm, AnimatorStateTransition target)
    {
        List<AnimatorStateTransition> list = new List<AnimatorStateTransition>(sm.anyStateTransitions);
        if (list.Remove(target))
        {
            list.Insert(0, target);
            sm.anyStateTransitions = list.ToArray();
        }
    }

    /// <summary>
    /// 从单个 FBX 里取出动画片段。
    /// `Model@Anim.fbx` 这类模型文件的 AnimationClip 是**子资源**，
    /// 用 LoadAssetAtPath&lt;AnimationClip&gt; 直接取可能拿不到，所以先查子资源；
    /// 取不到再退回全工程按名字搜一次（防止美术把片段挪了位置）。
    /// </summary>
    private static AnimationClip LoadClip(string fbxFileName, string clipNameHint)
    {
        string fbxPath = FBX_DIR + fbxFileName;
        Object[] subs = AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath);
        for (int i = 0; i < subs.Length; i++)
        {
            AnimationClip clip = subs[i] as AnimationClip;
            if (clip != null)
            {
                return clip;
            }
        }

        AnimationClip direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(fbxPath);
        if (direct != null)
        {
            return direct;
        }

        string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null && clip.name == clipNameHint)
            {
                Debug.LogWarning("[AnimBuild] 未从 " + fbxPath + " 取到片段，改用同名片段 " + path);
                return clip;
            }
        }

        Debug.LogError("[AnimBuild] 找不到动画片段：" + fbxPath);
        return null;
    }

    private static bool HasTransitionToStateMachine(IEnumerable<AnimatorStateTransition> transitions, AnimatorStateMachine sm)
    {
        foreach (AnimatorStateTransition t in transitions)
        {
            if (t != null && t.destinationStateMachine == sm)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 把某条转换挪到该状态转换列表的**首位**。
    /// Unity 按列表顺序判定转换，取第一条满足的 —— 这是让带条件的受击转换
    /// 能压过 idle 上那 4 条无条件出边的唯一办法。
    /// </summary>
    private static void MoveTransitionToFront(AnimatorState state, AnimatorStateTransition target)
    {
        List<AnimatorStateTransition> list = new List<AnimatorStateTransition>(state.transitions);
        if (list.Remove(target))
        {
            list.Insert(0, target);
            state.transitions = list.ToArray();
        }
    }

    private static bool HasEntryTransitionTo(AnimatorStateMachine sm, AnimatorState dest)
    {
        AnimatorTransition[] entries = sm.entryTransitions;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].destinationState == dest)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>补一条「播完回 idle」的 ExitTime 转换（幂等）</summary>
    private static void EnsureExitToIdle(AnimatorState from, AnimatorState idle)
    {
        AnimatorStateTransition[] ts = from.transitions;
        for (int i = 0; i < ts.Length; i++)
        {
            if (ts[i] != null && ts[i].destinationState == idle)
            {
                Debug.Log("[AnimBuild] " + from.name + " → idle 已存在，跳过");
                return;
            }
        }

        AnimatorStateTransition t = from.AddTransition(idle);
        t.hasExitTime = true;
        t.exitTime = 1f;
        t.duration = 0.1f;
        Debug.Log("[AnimBuild] 新增 " + from.name + " → idle [ExitTime=1]");
    }
}
