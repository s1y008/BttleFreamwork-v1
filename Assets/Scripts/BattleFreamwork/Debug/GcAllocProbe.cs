#if UNITY_EDITOR
using System;
using UnityEngine;
using Unity.Profiling;

/// <summary>
/// GC 分配采样探针 —— ★ 2026-09-27 下游分配审计专用，**不参与正式构建**（整文件在 #if UNITY_EDITOR 内）
///
/// 【为什么放在 Scripts 下而不是 Editor 目录】
///   Editor 目录里的 MonoBehaviour **不允许 AddComponent**（Unity 硬限制，运行时报
///   "Can't add script behaviour ... because it is an editor script"）。
///   本探针必须挂 GameObject 才能按帧采样，所以只能住在运行时程序集里、
///   再用条件编译把内容整体掏空 —— 与同目录的 DeathLinkProbe 同一手法。
///
/// 【为什么不用 EditorApplication.update】
///   实测：编辑器窗口不在前台时该回调一次都不跑，采样全空。
///   MonoBehaviour 的 Update / LateUpdate 在 Play 中一定按帧执行，与窗口焦点无关。
///
/// 【测什么】用 Unity 内建的 "GC.Alloc" Profiler 计数器，按窗口（每 120 帧）汇总托管分配字节数，
///   回答「一次命中结算到底分配了多少字节」。
///
/// 【为什么不用 GC.GetTotalMemory 差值】
///   GetTotalMemory 是「堆已用字节」，会随 Unity 后台分配 / 协程 / 字符串驻留一起波动，
///   单次读数噪声远大于想测的信号（几百字节）。GC.Alloc 是 Unity 每次分配时直接累加的计数器，精确。
///
/// 【★ 为什么本探针**不订阅** Application.logMessageReceived】
///   那是「统计命中次数」最省事的办法，但它本身是分配大户 —— Unity 每打一条日志都要
///   新造 stackTrace 字符串再回调进来。**测 GC 的探针自己制造 GC，读数就废了**。
///   命中次数改为离线统计：本探针只按窗口输出带序号的汇总，
///   由外部脚本抓全量日志后按 [GCProbe] 切窗口、数窗口内的 [HitReact] 条数。
///
/// 【自身污染】每 120 帧一条日志（约 200 B），相对每窗口 KB 级信号可忽略。
/// </summary>
public class GcAllocProbe : MonoBehaviour
{
    private const int WINDOW = 120;

    private static ProfilerRecorder s_Rec;
    private static bool s_UseRecorder;

    private static long s_WinTotal;
    private static long s_WinPeak;
    private static long s_Grand;
    private static int s_Frame;
    private static int s_Win;
    private static long s_PrevHeap;
    private static bool s_Calibrated;

    /// <summary>
    /// ★ 结果落盘而不走 Console —— 这是本探针能用来做「对照实验」的关键。
    ///   Debug.Log 在 Editor 下每打一条都要生成 stackTrace 字符串（分配大户），
    ///   而 console 捕获端还会持续轮询日志缓冲。两者都会污染被测对象。
    ///   把结果直接写文件，就能在「完全不打日志」的状态下采样，
    ///   与「开 capture + 打日志」那一轮做对照，量化观测行为自身的开销。
    /// </summary>
    private static readonly string s_OutPath =
        System.IO.Path.Combine(Application.dataPath, "Tools", "_gcprobe.txt");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        try
        {
            System.IO.File.WriteAllText(s_OutPath, "# GcAllocProbe 窗口采样（无 Console 输出）\n");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogWarning("[GCProbe] 无法写入结果文件: " + e.Message);
        }

        GameObject go = new GameObject("[GCProbe]");
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<GcAllocProbe>();
    }

    private void Awake()
    {
        s_Rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc");
        s_UseRecorder = s_Rec.Valid;
        s_Win = 0;
        s_Frame = 0;
        s_WinTotal = 0;
        s_WinPeak = 0;
        s_Grand = 0;
        s_PrevHeap = GC.GetTotalMemory(false);

        Append("# 采样源=" + (s_UseRecorder ? "ProfilerRecorder(GC.Alloc)" : "GC.GetTotalMemory(回退)")
               + " 窗口=" + WINDOW + "帧");
    }

    private void LateUpdate()
    {
        long v = 0;

        if (s_UseRecorder)
        {
            v = s_Rec.LastValue;
        }
        else
        {
            long now = GC.GetTotalMemory(false);
            v = now - s_PrevHeap;
            s_PrevHeap = now;
            if (v < 0) { v = 0; }
        }

        s_WinTotal += v;
        s_Grand += v;
        s_Frame++;
        if (v > s_WinPeak) { s_WinPeak = v; }

        if (s_Frame >= WINDOW)
        {
            s_Win++;
            long mean = s_WinTotal / WINDOW;
            Append("win=" + s_Win + "\tframes=" + WINDOW + "\ttotal=" + s_WinTotal
                   + "\tmean=" + mean + "\tpeak=" + s_WinPeak + "\tgrand=" + s_Grand);

            // ★ 标定必须等到战斗单位真的出现之后再做：本工程的单位是运行时克隆出来的，
            //   Awake 阶段 FindObjectsOfType 返回 0，此时在原点做球形检测一个碰撞体都打不到，
            //   Unity 会返回缓存的空数组 ⇒ 标定出「0 B/次」这种假性结论（已踩一次）。
            if (s_Win == 3 && !s_Calibrated)
            {
                s_Calibrated = true;
                Calibrate();
            }
            s_WinTotal = 0;
            s_WinPeak = 0;
            s_Frame = 0;
        }
    }

    /// <summary>
    /// ★ 分配标定（只在启动帧跑一次）——把「每帧 10 KB 到底是哪些 API 造成的」变成可计算的量。
    ///
    /// 【为什么必须标定而不是靠估】
    ///   窗口采样只能给出「每帧总共分配多少」，给不出构成。而 Unity 的某些 API
    ///   （Physics.OverlapSphere 的 Collider[]、Animator.parameters 的数组）到底分配多少字节，
    ///   随场景碰撞体数量而变，不实测就只能拍脑袋。
    ///
    /// 【方法】重复 N 次目标调用，取 GC.GetTotalMemory 差值 / N。
    ///   单次的噪声大，但 N=2000 时噪声被摊薄到可忽略；跑 3 轮取最大值，避开中途 GC 导致的低估。
    /// </summary>
    private void Calibrate()
    {
        const int N = 2000;
        const float R = 5f;

        // 取一个真实单位的位置做标定（原点可能一个碰撞体都没有，测出来是空数组，没有意义）
        Vector3 p = Vector3.zero;
        CombatUnitController[] units = FindObjectsOfType<CombatUnitController>();
        Append("# 场景战斗单位数=" + units.Length);
        if (units.Length > 0 && units[0] != null)
        {
            p = units[0].transform.position;
        }

        // ① Physics.OverlapSphere —— 返回新 Collider[]，是工程已知的每帧调用点
        Append("# 标定 Physics.OverlapSphere x" + N + " (半径" + R + ") : "
               + CalibrateOnce(N, () => { Collider[] a = Physics.OverlapSphere(p, R); }) + " B/次");

        // ② GetComponent<T>() —— Unity 泛型版本是否真的零分配，实测而不是听信经验
        if (units.Length > 0 && units[0] != null)
        {
            GameObject go = units[0].gameObject;
            Append("# 标定 GetComponent<CombatUnitController> x" + N + " : "
                   + CalibrateOnce(N, () => { CombatUnitController c = go.GetComponent<CombatUnitController>(); })
                   + " B/次");
        }

        // ③ 空委托 —— 标定方法自身的底噪（闭包调用 + 循环），上面的读数要减去它
        Append("# 标定 空循环底噪 x" + N + " : "
               + CalibrateOnce(N, () => { }) + " B/次");
    }

    /// <summary>重复 N 次 action，返回「每次调用的平均分配字节数」（3 轮取最大值，规避中途 GC 低估）</summary>
    private static long CalibrateOnce(int n, System.Action action)
    {
        long best = 0;
        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < 100; i++) { action(); }   // 预热：让 JIT 与首次分配先发生

            long a = GC.GetTotalMemory(false);
            for (int i = 0; i < n; i++) { action(); }
            long b = GC.GetTotalMemory(false);

            long per = (b - a) / n;
            if (per > best) { best = per; }
        }
        return best;
    }

    private static void Append(string line)
    {
        try
        {
            System.IO.File.AppendAllText(s_OutPath, line + "\n");
        }
        catch (System.Exception)
        {
            // 落盘失败不打断采样：宁可丢一行结果，也不让探针把被测对象搞崩
        }
    }

    private void OnDestroy()
    {
        if (s_Rec.Valid) { s_Rec.Dispose(); }
    }
}
#endif
