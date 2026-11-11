// ★ 技能编辑器 · 「挂载到单位」卡片（右侧面板的一张卡）
//
// 【职责】把「哪个单位拥有当前技能」这件事摆到编辑技能的地方：
//   列出 WarriorData.json / AttackNpcData.json 的全部单位，勾上即挂载、取消即卸载，最后统一落盘。
//
// 【为什么必须过滤 + 限流】两张表一共 1481 个单位（21 角色 + 1460 NPC）。
//   OnGUI 一帧把 1481 行全画出来会直接把窗口拖死，所以：
//     ① 建一次「通过过滤的下标表」（搜索变化 / 刷新时才重建，不是每帧）；
//     ② 滚动区里最多只画 MaxRowsDrawn 行，超出的明确告诉用户「请用搜索框缩小范围」；
//     ③ 行文本同样只在重建时生成一次 —— 每帧拼 1481 个字符串是纯浪费。
//
// 【⚠ Odin 纪律】本文件全部使用标准 UnityEditor 控件，不碰任何 Odin 绘制 API。
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkillEditorKit
{
    public sealed class SkillUnitMountPanel
    {
        /// <summary>滚动区里一帧最多画多少行 —— 上限而不是「全部」，防止几百行把窗口卡死。</summary>
        private const int MaxRowsDrawn = 120;

        private const float ViewHeight = 200f;

        private string _search = "";
        private Vector2 _scroll;
        private bool _loaded;

        private readonly List<int> _visible = new List<int>(256);              // SkillUnitMountModel.All() 的下标
        private readonly List<string> _visibleLabels = new List<string>(256);   // 与 _visible 逐下标对应的行文本

        // ══════════════════════════════════════════════════════════
        // 生命周期
        // ══════════════════════════════════════════════════════════
        /// <summary>外部（如「重载配置」）想让面板下次重读两张表时调用。</summary>
        public void Invalidate()
        {
            _loaded = false;
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            SkillUnitMountModel.Refresh();
            _loaded = true;
            RebuildVisible();
        }

        /// <summary>按搜索框重建可见行与其文本 —— 只在「刷新 / 搜索变化」时跑，不在每帧跑。</summary>
        private void RebuildVisible()
        {
            _visible.Clear();
            _visibleLabels.Clear();

            IReadOnlyList<UnitMountEntry> all = SkillUnitMountModel.All();
            if (all == null)
            {
                Debug.LogError("[技能编辑器] SkillUnitMountModel.All() 返回 null —— 挂载面板拿不到单位表，请检查两张单位配置是否可读。");
                return;
            }

            string key = _search != null ? _search.Trim() : "";
            for (int i = 0; i < all.Count; i++)
            {
                UnitMountEntry e = all[i];
                if (e == null) continue;
                if (key.Length > 0 && !Match(e, key)) continue;

                _visible.Add(i);
                string nm = string.IsNullOrEmpty(e.UnitName) ? "(未命名)" : e.UnitName;
                _visibleLabels.Add((e.IsWarrior ? "角色" : "NPC") + " " + e.UnitId + " · " + nm);
            }
        }

        /// <summary>按单位号 / 名称过滤，大小写不敏感。</summary>
        private static bool Match(UnitMountEntry e, string key)
        {
            string nm = e.UnitName != null ? e.UnitName : "";
            return e.UnitId.ToString().IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0
                   || nm.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ══════════════════════════════════════════════════════════
        // 绘制
        // ══════════════════════════════════════════════════════════
        public void Draw(SkillData data)
        {
            if (data == null) return;
            EnsureLoaded();

            string skillName = string.IsNullOrEmpty(data.Name) ? "(未命名)" : data.Name;

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("挂载到单位 · 技能 " + data.ID + " " + skillName, EditorStyles.boldLabel);

            DrawSearchBar(data);

            IReadOnlyList<UnitMountEntry> all = SkillUnitMountModel.All();
            int total = all != null ? all.Count : 0;
            int drawCount = _visible.Count < MaxRowsDrawn ? _visible.Count : MaxRowsDrawn;

            EditorGUILayout.LabelField("单位共 " + total + " 个，匹配 " + _visible.Count + " 个",
                EditorStyles.miniLabel);

            // ── 滚动区 ────────────────────────────────────────
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(ViewHeight));
            if (all != null)
            {
                for (int i = 0; i < drawCount; i++)
                {
                    int idx = _visible[i];
                    if (idx < 0 || idx >= all.Count) continue;
                    DrawRow(all[idx], data);
                }
            }
            EditorGUILayout.EndScrollView();

            if (_visible.Count > drawCount)
            {
                EditorGUILayout.HelpBox("匹配到 " + _visible.Count + " 个单位，只画了前 " + drawCount
                                        + " 个 —— 请用上面的搜索框按单位号或名称缩小范围。", MessageType.Info);
            }
            else if (_visible.Count == 0)
            {
                EditorGUILayout.HelpBox("没有匹配的单位。", MessageType.None);
            }

            DrawFooter();
            EditorGUILayout.EndVertical();
        }

        private void DrawSearchBar(SkillData data)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜索", GUILayout.Width(36));
            // ⚠ TextField 每帧回写：只有文本真的变了才重建可见行，否则 1481 行会被每帧重排
            string typed = EditorGUILayout.TextField(_search);
            if (typed != _search)
            {
                _search = typed;
                RebuildVisible();
            }
            if (GUILayout.Button("刷新", GUILayout.Width(50)))
            {
                SkillUnitMountModel.Refresh();
                RebuildVisible();
            }
            EditorGUILayout.EndHorizontal();

            // 新建但没落盘就挂载，会把一个「技能表里根本不存在」的 id 写进单位表 —— 必须当场拦
            if (SkillEditorModel.IsNew(data.ID))
            {
                EditorGUILayout.HelpBox("技能 " + data.ID + " 是本次会话新建、尚未保存的技能：现在挂载会把一个"
                                        + " SkillData.json 里还不存在的 id 写进单位表，运行时取不到技能就什么都不放。"
                                        + "请先在主窗口点「保存」，再回来挂载。", MessageType.Warning);
            }
        }

        private static void DrawRow(UnitMountEntry e, SkillData data)
        {
            if (e == null) return;

            bool mounted = SkillUnitMountModel.IsMounted(e, data.ID);
            string nm = string.IsNullOrEmpty(e.UnitName) ? "(未命名)" : e.UnitName;

            EditorGUI.BeginChangeCheck();
            bool next = EditorGUILayout.ToggleLeft(
                (e.IsWarrior ? "角色" : "NPC") + " " + e.UnitId + " · " + nm, mounted);
            if (!EditorGUI.EndChangeCheck()) return;

            bool ok = next
                ? SkillUnitMountModel.Mount(e, data.ID)
                : SkillUnitMountModel.Unmount(e, data.ID);

            if (!ok)
            {
                // fail-fast：挂载失败必须指名到「哪个单位、哪张表」，否则用户只会看到勾选框弹回去
                Debug.LogError("[技能编辑器] " + (next ? "挂载" : "卸载") + "失败：技能 " + data.ID
                               + " ↔ 单位 " + e.UnitId + " " + nm + "（" + e.FileName + "）"
                               + " —— SkillUnitMountModel." + (next ? "Mount" : "Unmount") + " 返回 false。");
                return;
            }

            Debug.Log("[技能编辑器] 已" + (next ? "挂载" : "卸载") + "：技能 " + data.ID
                      + " ↔ 单位 " + e.UnitId + " " + nm + "（" + e.FileName + "）");
        }

        private void DrawFooter()
        {
            bool dirty = SkillUnitMountModel.HasAnyDirty();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(dirty ? "挂载有未保存改动" : "挂载已保存", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            GUI.enabled = dirty;
            if (GUILayout.Button("保存挂载", GUILayout.Width(90)))
            {
                int n = SkillUnitMountModel.SaveAll();
                Debug.Log("[技能编辑器] 已保存 " + n + " 个单位的挂载改动（写回 WarriorData.json / AttackNpcData.json，只改 SkillArray 一列）。");
                RebuildVisible();
            }
            if (GUILayout.Button("撤销", GUILayout.Width(60)))
            {
                SkillUnitMountModel.Revert();
                RebuildVisible();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("保存挂载会写回 WarriorData.json / AttackNpcData.json（最小 diff，只动 SkillArray）。"
                                    + "与技能配置一样：写完仍需重新进入 Play 模式才生效。", MessageType.None);

            // ⚠ 用户点名要写进面板的运行时事实：挂上 ≠ 按得出来
            EditorGUILayout.HelpBox("⚠ 挂载只解决「单位拥有这个技能」。玩家按 A 实际放出哪个技能，"
                                    + "目前被 WarrorController.cs:74 硬编码成 10002 —— 那是运行时代码（Assets/Scripts/**），"
                                    + "编辑器改不了。想放别的技能必须动运行时脚本，并由人评审后再改。", MessageType.Warning);
        }
    }
}
