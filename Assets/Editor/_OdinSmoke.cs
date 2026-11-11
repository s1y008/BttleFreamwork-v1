// ★ 临时冒烟脚本 —— 验证 Odin 能否承载技能 Live 面板（验证后可删除）。
//
// 三个必须验证的点：
//   ① OdinEditorWindow 可继承、可编译（Sirenix.OdinInspector.Editor.dll 能被 Assembly-CSharp-Editor 引用）
//   ② Odin 的绘制特性（BoxGroup / LabelText / ReadOnly / EnumToggleButtons / ProgressBar / ShowIf / TableList）生效
//   ③ ★★ [OnInspectorGUI] 能在 Odin 渲染流里嵌入自绘 IMGUI —— 自定义「施法时间轴」控件将来就挂在这里
//
// ⚠ 为什么放在 Editor/ 下且禁止运行时引用 Odin：
//   Sirenix.OdinInspector.Editor.dll 位于 Assets/Plugins/Sirenix/Assemblies/（**不是 Editor 文件夹**），
//   且其 .meta 里没有 defineConstraints / includePlatforms，Unity 会认为它对**所有** assembly 可见。
//   只要 Scripts/ 下有任意一行运行时代码引用了它，打包时就会报「找不到 UnityEditor」。
//   ⇒ 纪律：Odin 只能出现在 Editor/ 目录的脚本里。
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

public class OdinSmokeWindow : OdinEditorWindow
{
    // ⚠ 菜单路径必须 ASCII —— 要能经 UnitySkills 的 editor_execute_menu 远程调用
    [MenuItem("Tools/BattleFramework/Odin Smoke")]
    private static void Open()
    {
        OdinSmokeWindow w = GetWindow<OdinSmokeWindow>();
        w.titleContent = new GUIContent("Odin 冒烟");
        w.Show();
    }

    // ── ① 基础绘制特性 ─────────────────────────────────────────────
    [Title("① 基础绘制特性")]
    [BoxGroup("① 基础绘制特性/标量")]
    [LabelText("技能号")]
    public int skillId = 10003;

    [BoxGroup("① 基础绘制特性/标量")]
    [LabelText("技能名"), ReadOnly]
    public string skillName = "只读字段 —— 验证 ReadOnly";

    [BoxGroup("① 基础绘制特性/标量")]
    [LabelText("模式"), EnumToggleButtons]
    public SmokeMode mode = SmokeMode.Static;

    [BoxGroup("① 基础绘制特性/标量")]
    [LabelText("施法进度"), ProgressBar(0, 1)]
    public float progress = 0.62f;

    // ShowIf：只有切到 Realtime 才出现 —— 将来「循环施放/慢放」这类按钮就是这么挂的
    [BoxGroup("① 基础绘制特性/标量")]
    [LabelText("循环施放"), ShowIf("mode", SmokeMode.Realtime)]
    public bool loopCast = true;

    public enum SmokeMode { Static, Realtime }

    // ── ② 表格渲染（段的只读列表将来就用这个） ─────────────────────
    [Title("② 表格渲染 TableList")]
    [TableList(AlwaysExpanded = true, DrawScrollView = false)]
    [LabelText("伤害段")]
    public List<SegRow> segments = new List<SegRow>
    {
        new SegRow { SegIdx = 1, Timing = 0.16f, State = SegState.Fired },
        new SegRow { SegIdx = 2, Timing = 0.33f, State = SegState.Fired },
        new SegRow { SegIdx = 3, Timing = 0.63f, State = SegState.Pending },
    };

    public enum SegState { Pending, Fired }

    [System.Serializable]
    public class SegRow
    {
        [TableColumnWidth(60, resizable: false), LabelText("段号")]
        public int SegIdx;

        [TableColumnWidth(120), LabelText("时机"), ProgressBar(0, 1)]
        public float Timing;

        [TableColumnWidth(90), LabelText("状态")]
        public SegState State;
    }

    [Button("② 验证按钮可用", ButtonSizes.Medium)]
    [HorizontalGroup("btns")]
    private void OnPing()
    {
        Debug.Log("[Odin冒烟] 按钮可用");
    }

    [Button("② 打印当前段落", ButtonSizes.Medium)]
    [HorizontalGroup("btns")]
    private void OnDump()
    {
        for (int i = 0; i < segments.Count; i++)
        {
            SegRow r = segments[i];
            Debug.Log($"[Odin冒烟] 段 {r.SegIdx} timing={r.Timing} state={r.State}");
        }
    }

    // ── ③ ★★ 自绘 IMGUI 嵌入点 —— 真正的「施法时间轴」挂在这里 ──────
    [Title("③ 自绘 IMGUI 嵌入（时间轴挂载点）")]
    [OnInspectorGUI]
    private void DrawTimelineArea()
    {
        Rect whole = GUILayoutUtility.GetRect(10, 70, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(whole, new Color(0.13f, 0.14f, 0.17f));

        // 一条代表 0~1 归一化进度的轨道
        Rect track = new Rect(whole.x + 10, whole.y + 16, whole.width - 20, 20);
        EditorGUI.DrawRect(track, new Color(0.25f, 0.26f, 0.31f));

        // 按 segments 画 tick：已触发绿、未触发橙
        for (int i = 0; i < segments.Count; i++)
        {
            SegRow r = segments[i];
            Color c = r.State == SegState.Fired
                ? new Color(0.29f, 0.78f, 0.65f)
                : new Color(0.93f, 0.62f, 0.15f);
            Rect tick = new Rect(track.x + track.width * r.Timing - 2f, track.y, 4f, track.height);
            EditorGUI.DrawRect(tick, c);
        }

        // 播放头（红）—— Play 中每帧用 progress 刷新
        Rect head = new Rect(track.x + track.width * progress - 1f, track.y - 4f, 2f, track.height + 8f);
        EditorGUI.DrawRect(head, new Color(0.89f, 0.29f, 0.29f));

        EditorGUI.LabelField(new Rect(whole.x + 10, whole.y + 44, 320, 16),
            "自绘 IMGUI 区域内可自由画时间轴 —— 混合架构成立");
    }
}
