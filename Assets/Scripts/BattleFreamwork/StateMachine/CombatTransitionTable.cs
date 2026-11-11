using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战斗状态分层：判定「哪类不对称需要人工确认」的唯一依据。
///
/// 【为什么要把分层从 Editor 工具提升到运行时】
///   分层原本只是矩阵窗口里的一个私有字典，用来判断「同层单向=可疑、跨层单向=正常打断」。
///   一旦转换关系要存进资产，分层就必须跟着一起存 —— 否则资产里只有边、没有层，
///   工具的分层字典和资产内容会各说各话（新增状态只在一边登记，另一边静默失效）。
///   提升为运行时类型后，分层成为转换契约的一部分，新增 CombatState 时漏声明会被加载期指名。
/// </summary>
public enum CombatStateLayer
{
    Action = 0,     // 行动层：Idle / Move / Attack —— 单位自主行为
    Control = 1,    // 受控层：Stun / Silence / Knockback —— 由 Buff 强加的控制效果
    Terminal = 2,   // 终态：Dead —— 无出边
}

/// <summary>
/// 战斗状态转换表资产（ScriptableObject）。
///
/// 【为什么需要它】
///   CombatStateMachine.InitDefaultTransitions() 用 28 行 AddTransition 逐条列举转换关系，
///   两个问题：(1) 规则重复 —— Idle/Move/Attack 三组各写一遍「→ 三种受控」，加状态要改 6 处；
///   (2) 不对称无法自证 —— A→B 有边而 B→A 没有，看不出是有意还是漏写。
///   把关系外置成资产后，关系可编辑、可版本管理、可导出对照，代码那份退化为**保底默认值**。
///
/// 【运行时怎么用】
///   CombatStateMachine 构造时通过 <see cref="Load"/> 取本资产；存在且校验通过则**整体覆盖**
///   代码内置表，不存在则保留代码表并只报一次警告（指名路径，不静默降级）。
///
/// 【校验为什么放在资产里而不是 Editor 里】
///   同一套 <see cref="ValidateRows"/> 被「编辑器窗口」和「运行时加载」两处共用，
///   杜绝「编辑器说没问题、运行时却拒绝加载」这类两套规则的裂缝。
/// </summary>
[CreateAssetMenu(
    menuName = "Battle Framework/Combat Transition Table",
    fileName = "CombatTransitionTable",
    order = 100)]
public class CombatTransitionTable : ScriptableObject
{
    /// <summary>Resources 加载路径（不含扩展名）</summary>
    public const string RESOURCE_PATH = "Config/CombatTransitionTable";

    /// <summary>默认资产存放位置（必须位于 Resources 下，否则运行时 Resources.Load 取不到）</summary>
    public const string ASSET_PATH = "Assets/Resources/Config/CombatTransitionTable.asset";

    public const string CURRENT_VERSION = "1.0";

    // ─────────────────── 默认分层（新建资产时用于填充，也是漏声明时的提示依据）───────────────────
    private static readonly Dictionary<CombatState, CombatStateLayer> s_DefaultLayer =
        new Dictionary<CombatState, CombatStateLayer>
        {
            { CombatState.Idle, CombatStateLayer.Action },
            { CombatState.Move, CombatStateLayer.Action },
            { CombatState.Attack, CombatStateLayer.Action },
            { CombatState.Stun, CombatStateLayer.Control },
            { CombatState.Silence, CombatStateLayer.Control },
            { CombatState.Knockback, CombatStateLayer.Control },
            { CombatState.Dead, CombatStateLayer.Terminal },
        };

    /// <summary>取某状态的默认分层；未登记返回 -1（调用方负责指名报错）。</summary>
    public static int DefaultLayerOf(CombatState state)
    {
        CombatStateLayer layer;
        if (s_DefaultLayer.TryGetValue(state, out layer)) return (int)layer;
        return -1;
    }

    // ─────────────────── 数据 ───────────────────

    /// <summary>一行 = 一个状态的出边集合（邻接表，比矩阵更直观也更省空间）。</summary>
    [Serializable]
    public class StateRow
    {
        public CombatState State;
        public CombatStateLayer Layer;
        public List<CombatState> Targets = new List<CombatState>();

        [TextArea(1, 3)]
        public string Note;
    }

    /// <summary>
    /// 已确认的「有意不对称」：同层之内 A→B 有边而 B→A 没有，人工确认后登记在此并写明理由。
    /// 未被登记的会在编辑器里高亮 —— 让「说不清」的边自己浮出来，而不是靠记忆。
    /// </summary>
    [Serializable]
    public class AsymmetryAck
    {
        public CombatState From;
        public CombatState To;
        public string Reason;
    }

    [Header("版本：用于将来结构变更时的兼容判定")]
    public string Version = CURRENT_VERSION;

    [Header("关闭后运行时不覆盖代码内置表（调试用，便于快速回到硬编码行为）")]
    public bool Enabled = true;

    public List<StateRow> Rows = new List<StateRow>();

    public List<AsymmetryAck> AckedAsymmetries = new List<AsymmetryAck>();

    // ─────────────────── 运行时读取 ───────────────────

    private static CombatTransitionTable s_Cached;
    private static bool s_CacheLoaded;
    private static bool s_MissingLogged;

    /// <summary>
    /// 取当前生效的转换表资产（带静态缓存）。
    /// 静态字段在域重载（进/出 Play）时自动重置，所以不需要额外的清缓存时机。
    /// </summary>
    public static CombatTransitionTable Load()
    {
        if (s_CacheLoaded) return s_Cached;

        s_CacheLoaded = true;
        s_Cached = Resources.Load<CombatTransitionTable>(RESOURCE_PATH);
        return s_Cached;
    }

    /// <summary>编辑器改完资产后调用，让下一次构造重新读盘（否则运行时会一直用旧副本）。</summary>
    public static void ClearCache()
    {
        s_CacheLoaded = false;
        s_Cached = null;
        s_MissingLogged = false;
    }

    /// <summary>
    /// 资产缺失时只报一次，避免每个单位构造都刷一条日志。
    ///
    /// ★ 这是 **Error 不是 Warning**：运行时没有内置表可退
    ///   （CombatStateMachine.InitDefaultTransitions 只在 Editor 下编译），
    ///   所以资产缺失 = 该状态机无任何合法转换 = 所有单位卡死在当前状态。必须一眼看到。
    /// </summary>
    public static void LogMissingOnce()
    {
        if (s_MissingLogged) return;
        s_MissingLogged = true;

        string head = "[TransitionTable] 未找到转换表资产 Resources/" + RESOURCE_PATH + "。";
#if UNITY_EDITOR
        // Editor 下 InitDefaultTransitions 仍然编译进来（它是新建资产 / 重置基线的模板），
        // 所以这里其实是「能跑，但打包后会坏」—— 必须说清，否则人会以为什么事都没有。
        Debug.LogError(head
            + "当前在 Editor 下，状态机已回落到内置默认表（InitDefaultTransitions）可以继续编辑；"
            + "但**打包后该内置表不存在**，届时所有单位将无任何合法转换（卡死在当前状态）。"
            + "请在打包前用菜单 Tools → Battle Framework → Transition Matrix 创建并保存资产。");
#else
        Debug.LogError(head
            + "运行时状态机将无任何合法转换（所有单位卡死在当前状态）。"
            + "请用菜单 Tools → Battle Framework → Transition Matrix 创建并保存转换表资产。");
#endif
    }

    /// <summary>运行时实际生效的是资产还是代码表（编辑器状态栏用）。</summary>
    public static bool HasAsset()
    {
        return Load() != null;
    }

    // ─────────────────── 构建 ───────────────────

    /// <summary>
    /// 把资产内容构建成状态机可直接使用的邻接表。
    /// 调用前必须先 <see cref="Validate"/> 通过 —— 本方法不做兜底修正，
    /// 有问题一律交给校验阶段指名（工程铁律：禁止为资产不一致写运行时静默降级）。
    /// </summary>
    public void BuildTransitions(Dictionary<CombatState, HashSet<CombatState>> output)
    {
        if (output == null) return;
        output.Clear();
        if (Rows == null) return;

        for (int i = 0; i < Rows.Count; i++)
        {
            StateRow row = Rows[i];
            if (row == null) continue;

            HashSet<CombatState> set = new HashSet<CombatState>();
            if (row.Targets != null)
            {
                for (int k = 0; k < row.Targets.Count; k++)
                {
                    // 自身边即便配了也会被 CanTransitTo 的「禁自转换」短路，这里直接丢弃
                    if (row.Targets[k] == row.State) continue;
                    set.Add(row.Targets[k]);
                }
            }
            output[row.State] = set;
        }
    }

    // ─────────────────── 校验（编辑器与运行时共用）───────────────────

    /// <summary>实例校验：把问题写进 errors / warnings，返回是否可安全加载。</summary>
    public bool Validate(List<string> errors, List<string> warnings)
    {
        return ValidateRows(Rows, Version, name, errors, warnings);
    }

    /// <summary>
    /// 静态校验：编辑器对「未保存的内存副本」校验时也走这一套，保证两边规则完全一致。
    /// </summary>
    public static bool ValidateRows(
        List<StateRow> rows,
        string version,
        string assetName,
        List<string> errors,
        List<string> warnings)
    {
        if (errors == null || warnings == null) return false;

        string tag = string.IsNullOrEmpty(assetName) ? "转换表" : assetName;

        if (rows == null || rows.Count == 0)
        {
            errors.Add(tag + "：没有任何状态行，无法构建转换关系。");
            return false;
        }

        if (!string.Equals(version, CURRENT_VERSION))
            warnings.Add(tag + "：版本为 " + version + "，当前代码期望 " + CURRENT_VERSION + "，请确认结构是否兼容。");

        // ① 重复登记
        Dictionary<CombatState, int> seen = new Dictionary<CombatState, int>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] == null) continue;
            if (seen.ContainsKey(rows[i].State))
                errors.Add(tag + "：" + rows[i].State + " 被重复登记（第 " + (seen[rows[i].State] + 1)
                    + " 行与第 " + (i + 1) + " 行），后一行会被覆盖。");
            else
                seen[rows[i].State] = i;
        }

        // ② 枚举漏声明 —— 新增 CombatState 却没在资产里登记，必须指名
        Array all = Enum.GetValues(typeof(CombatState));
        for (int i = 0; i < all.Length; i++)
        {
            CombatState s = (CombatState)all.GetValue(i);
            if (!seen.ContainsKey(s))
                errors.Add(tag + "：CombatState." + s + " 未在资产中登记，运行时该状态将无任何出边（进得去出不来）。");
        }

        // ③ 死活判定
        int deadIdx = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] != null && rows[i].State == CombatState.Dead) deadIdx = i;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            StateRow row = rows[i];
            if (row == null) continue;

            bool isTerminal = row.Layer == CombatStateLayer.Terminal;

            if (isTerminal && row.State != CombatState.Dead)
                errors.Add(tag + "：" + row.State + " 被标为 Terminal 但不是 Dead，当前状态机只把 Dead 当终态。");

            if (row.State == CombatState.Dead && !isTerminal)
                errors.Add(tag + "：Dead 的 Layer 必须是 Terminal（当前为 " + row.Layer + "）。");

            int outDeg = row.Targets != null ? row.Targets.Count : 0;
            if (outDeg == 0 && !isTerminal)
                errors.Add(tag + "：" + row.State + " 没有任何出边 —— 一旦进入就再也出不来。");

            if (outDeg > 0 && isTerminal)
                errors.Add(tag + "：" + row.State + " 是终态却配置了 " + outDeg + " 条出边。");

            // ④ 非终态必须能进 Dead，否则这个状态的单位打不死
            if (!isTerminal && deadIdx >= 0)
            {
                bool canDie = false;
                if (row.Targets != null)
                {
                    for (int k = 0; k < row.Targets.Count; k++)
                        if (row.Targets[k] == CombatState.Dead) { canDie = true; break; }
                }
                if (!canDie)
                    errors.Add(tag + "：" + row.State + " 无法转换到 Dead —— 处于该状态的单位将永远打不死。");
            }

            // ⑤ 自身边无效
            if (row.Targets != null)
            {
                for (int k = 0; k < row.Targets.Count; k++)
                {
                    if (row.Targets[k] == row.State)
                        warnings.Add(tag + "：" + row.State + " → " + row.State
                            + " 是自身边，会被状态机的「禁自转换」短路，建议删除。");
                }
            }
        }

        return errors.Count == 0;
    }

    // ─────────────────── 查询辅助（编辑器用）──────────────────

    /// <summary>该「同层单向」是否已被人工确认。参数顺序不敏感。</summary>
    public bool IsAcked(CombatState a, CombatState b)
    {
        if (AckedAsymmetries == null) return false;
        for (int i = 0; i < AckedAsymmetries.Count; i++)
        {
            AsymmetryAck ack = AckedAsymmetries[i];
            if (ack == null) continue;
            if ((ack.From == a && ack.To == b) || (ack.From == b && ack.To == a)) return true;
        }
        return false;
    }

    public int LayerOf(CombatState state)
    {
        if (Rows == null) return DefaultLayerOf(state);
        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i] != null && Rows[i].State == state) return (int)Rows[i].Layer;
        }
        return DefaultLayerOf(state);
    }

    /// <summary>按默认分层重建一套空行（新建资产 / 重置时用）。</summary>
    public static List<StateRow> BuildDefaultRows()
    {
        List<StateRow> rows = new List<StateRow>();
        Array all = Enum.GetValues(typeof(CombatState));
        for (int i = 0; i < all.Length; i++)
        {
            CombatState s = (CombatState)all.GetValue(i);
            rows.Add(new StateRow
            {
                State = s,
                Layer = DefaultLayerOf(s) >= 0 ? (CombatStateLayer)DefaultLayerOf(s) : CombatStateLayer.Action,
                Targets = new List<CombatState>(),
                Note = "",
            });
        }
        return rows;
    }
}
