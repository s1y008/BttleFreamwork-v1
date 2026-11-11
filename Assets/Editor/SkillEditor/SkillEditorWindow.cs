// ★ 技能编辑器 · 主窗口 —— 基于 Odin 的 OdinMenuEditorWindow
//
// 【职责】把三个独立模块汇成一个可用的编辑器：
//   左侧 —— OdinMenuTree（白送的 236 技能树 + 搜索框）
//   右侧 —— 自绘表单（中文标签 + 分组）+ 自绘施法时间轴 + 校验面板
//   Scene —— 命中范围 Gizmo 可视化
//
// 【⚠ 为什么只有本文件允许引用 Odin】
//   Sirenix.OdinInspector.Editor.dll 位于 Assets/Plugins/Sirenix/Assemblies/（不是 Editor 文件夹），
//   且其 .meta 里 defineConstraints / includePlatforms 全空 ⇒ Unity 认为它对**所有** assembly 可见。
//   只要 Scripts/ 下有任意一行运行时代码引用了它，打包就会报「找不到 UnityEditor」。
//   ⇒ 纪律：Odin 只允许出现在本目录（Assets/Editor/SkillEditor/）的窗口文件里。
//
// 【为什么不直接用 base.DrawEditors() 画 SkillData 原生 inspector】
//   Odin 自动渲染出来的是英文属性名、按声明顺序平铺、没有分组也没有单位说明，
//   更看不出「段时机」和「段数」是一对。本工具的价值恰恰在那些"人才知道"的信息上，
//   所以右侧全部自绘 —— Odin 在这里被使用的方式是：**左树 + 视觉风格 + 布局骨架**。
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using SkillEditorKit;

namespace SkillEditorKit
{
    public class SkillEditorWindow : OdinMenuEditorWindow
    {
        // ⚠ 菜单路径必须 ASCII —— 要能经 UnitySkills 的 editor_execute_menu 远程调用
        [MenuItem("Tools/BattleFramework/Skill Editor")]
        private static void Open()
        {
            SkillEditorWindow w = GetWindow<SkillEditorWindow>();
            w.titleContent = new GUIContent("技能编辑器");
            w.Show();
        }

        // ── 运行时状态 ──────────────────────────────────────────
        private SkillData _selected;
        private DrawOptions _gizmoOpts = new DrawOptions();
        private bool _gizmoEnabled = true;
        private float _playhead = -1f;          // <0 表示不在播放、不画播放头
        private bool _simulatePlayhead;         // 未进 Play 时也打个播放头来回走，方便看清触发顺序
        private Vector2 _formScroll;
        /// <summary>「逐段效果号 · 高级文本编辑」的折叠状态 —— 默认折叠。</summary>
        private bool _effectTextOpen;
        /// <summary>「挂载到单位」卡片的自持状态（搜索词 / 滚动位置 / 可见行缓存）。</summary>
        private SkillUnitMountPanel _mountPanel = new SkillUnitMountPanel();
        private readonly HashSet<FieldGroup> _foldouts = new HashSet<FieldGroup>();
        private double _lastSceneTick;

        // 静态挂点：域重载后窗口会被重建，静态回调必须能被上一个实例摘掉
        private static SkillEditorWindow _sceneHookOwner;

        // ══════════════════════════════════════════════════════════
        // 生命周期
        // ══════════════════════════════════════════════════════════
        protected override void Initialize()
        {
            SkillEditorModel.Refresh();

            // 六个分组默认全部展开 —— 第一次进来要让人看清全貌，而不是一堆折叠符号
            for (int i = 0; i < SkillFieldMetaTable.GroupOrder.Length; i++)
            {
                _foldouts.Add(SkillFieldMetaTable.GroupOrder[i]);
            }

            SceneView.duringSceneGui -= StaticSceneGui;
            SceneView.duringSceneGui += StaticSceneGui;
            _sceneHookOwner = this;
        }

        protected override void OnDestroy()
        {
            SceneView.duringSceneGui -= StaticSceneGui;
            if (_sceneHookOwner == this) _sceneHookOwner = null;
            base.OnDestroy();
        }

        // ══════════════════════════════════════════════════════════
        // 左侧：OdinMenuTree
        // ══════════════════════════════════════════════════════════
        protected override OdinMenuTree BuildMenuTree()
        {
            OdinMenuTree tree = new OdinMenuTree();
            tree.Config.DrawSearchToolbar = true;

            IReadOnlyList<SkillData> all = SkillEditorModel.All();
            if (all == null || all.Count == 0)
            {
                tree.Add("（没有读到技能数据）", null);
                return tree;
            }

            // ① ★ 已配时间轴 —— 236 个里只有极少数，单独拎出来方便反复回来调
            int withAxis = 0;
            for (int i = 0; i < all.Count; i++)
            {
                SkillData d = all[i];
                if (d != null && d.GetSegmentCount() > 0)
                {
                    tree.Add("★ 已配时间轴/" + NodeName(d), d);
                    withAxis++;
                }
            }
            if (withAxis == 0) tree.Add("★ 已配时间轴/（无）", null);

            // ② 全部技能 —— 按 ID 每 10 个一桶，避免树太长
            Dictionary<int, List<SkillData>> buckets = new Dictionary<int, List<SkillData>>();
            List<int> bucketKeys = new List<int>();
            for (int i = 0; i < all.Count; i++)
            {
                SkillData d = all[i];
                if (d == null) continue;
                int key = d.ID - (d.ID % 10);
                List<SkillData> list;
                if (!buckets.TryGetValue(key, out list))
                {
                    list = new List<SkillData>();
                    buckets[key] = list;
                    bucketKeys.Add(key);
                }
                list.Add(d);
            }
            bucketKeys.Sort();
            for (int i = 0; i < bucketKeys.Count; i++)
            {
                List<SkillData> list = buckets[bucketKeys[i]];
                for (int j = 0; j < list.Count; j++)
                {
                    tree.Add("全部技能/" + bucketKeys[i] + "/" + NodeName(list[j]), list[j]);
                }
            }

            tree.Selection.SelectionChanged += OnTreeSelectionChanged;
            return tree;
        }

        /// <summary>树节点的显示名：ID + 名称 + 段数徽记。</summary>
        private static string NodeName(SkillData d)
        {
            string nm = string.IsNullOrEmpty(d.Name) ? "(未命名)" : d.Name;
            int seg = d.GetSegmentCount();
            return d.ID + " " + nm + (seg > 0 ? ("  [" + seg + "段]") : "  [无段]");
        }

        private void OnTreeSelectionChanged(SelectionChangedType type)
        {
            _selected = MenuTree != null ? MenuTree.Selection.SelectedValue as SkillData : null;
        }

        // ══════════════════════════════════════════════════════════
        // 右侧：自绘编辑区
        // ══════════════════════════════════════════════════════════
        protected override void DrawEditors()
        {
            DrawCreateButton();
            // 选中项以 tree 为准（BuildMenuTree 之后 Selection 才可用）
            if (MenuTree != null)
            {
                SkillData sel = MenuTree.Selection.SelectedValue as SkillData;
                if (!ReferenceEquals(sel, _selected)) _selected = sel;
            }

            if (_selected == null)
            {
                EditorGUILayout.HelpBox("请在左侧选择一个技能。上方搜索框支持按技能号与名称过滤。",
                    MessageType.Info);
                return;
            }

            SkillData data = _selected;

            DrawHeader(data);
            DrawSeparator();
            EditorGUILayout.Space(4);
            DrawValidation(data);
            DrawTimeline(data);
            DrawGroups(data);
            DrawMountPanel(data);
            DrawDirtyFooter();
        }

        // ── 顶部「＋ 新建技能」按钮 ──────────────────────────
        /// <summary>
        /// 画在**自绘区开头**而不是 Odin 工具条（OdinMenuEditorWindow.DrawToolbar）里，理由有三：
        ///   ① Odin 工具条那条横向空间要与左树搜索框共享，窗口变窄时按钮会被挤没；
        ///   ② 这里与下方「技能 id + 保存 / 放弃改动」那一横条是同一套视觉，风格连续；
        ///   ③ Odin 纪律只验证了「继承 OdinMenuEditorWindow / [MenuItem] / MenuTree」这三处，
        ///      自绘区 100% 用标准 UnityEditor 控件，不引入任何未验证的 Odin 绘制 API。
        /// </summary>
        private void DrawCreateButton()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 新建技能", GUILayout.Width(110)))
            {
                SkillCreateWizard.Open(this);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            DrawSeparator();
        }

        /// <summary>「挂载到单位」卡片 —— 具体绘制收在 SkillUnitMountPanel 里，本窗口只负责挂上去。</summary>
        private void DrawMountPanel(SkillData data)
        {
            // 域重载后非序列化字段可能已被清掉，这里补一次，比在 OnEnable 里兜更可靠
            if (_mountPanel == null) _mountPanel = new SkillUnitMountPanel();
            _mountPanel.Draw(data);
        }

        // ── 向导新建后的自动选中 ──────────────────────────────
        /// <summary>
        /// 向导新建技能后调用：刷新左树并自动选中，让用户直接在右侧继续编辑。
        ///
        /// 【为什么必须同时改 MenuTree.Selection】DrawEditors 每帧都以
        ///   MenuTree.Selection.SelectedValue 为准回写 _selected，只给 _selected 赋值
        ///   会在下一帧被树里的选中值盖掉，表现为「新建完右侧仍是一片空白」。
        /// </summary>
        public void SelectSkillById(int skillId)
        {
            SkillData d = SkillEditorModel.ById(skillId);
            if (d == null)
            {
                Debug.LogError("[技能编辑器] 新建技能 " + skillId + " 后仍取不到数据 —— 无法自动选中，"
                               + "请检查 SkillEditorModel 的 CreateClone / CreateBlank 是否把新技能注册进了缓存表。");
                return;
            }

            ForceMenuTreeRebuild();
            _selected = d;

            if (MenuTree == null) return;

            OdinMenuItem item = FindMenuItem(d);
            if (item == null)
            {
                Debug.LogError("[技能编辑器] 左树里找不到刚创建的技能 " + skillId
                               + " —— 请检查 BuildMenuTree 的节点加入逻辑。");
                return;
            }
            // ⚠ OdinMenuTreeSelection 是 IList<OdinMenuItem>：只有 Add / Clear / Remove，
            //   **没有 Select 方法**（2026-10-13 编译实锤 error CS1061）。
            //   所以「选中」= 先清空再把目标节点加进去。
            MenuTree.Selection.Clear();
            MenuTree.Selection.Add(item);
            Repaint();
        }

        /// <summary>
        /// 在 Odin 左树里找出 value 是该技能的节点。
        /// ⚠ Odin 只给了 EnumerateTree() 这个 IEnumerable 接口，没有下标访问；
        ///   这里用显式 MoveNext（不是 foreach）走一遍，递归由 EnumerateTree 内部完成。
        ///   这是「点一次按钮」的路径，不是每帧绘制，不给 GC 添负担。
        /// </summary>
        private OdinMenuItem FindMenuItem(SkillData data)
        {
            if (MenuTree == null || data == null) return null;

            IEnumerator<OdinMenuItem> it = MenuTree.EnumerateTree().GetEnumerator();
            while (it.MoveNext())
            {
                OdinMenuItem item = it.Current;
                if (item != null && ReferenceEquals(item.Value, data)) return item;
            }
            return null;
        }

        // ── 顶部工具条 ──────────────────────────────────────────
        private void DrawHeader(SkillData data)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("技能 " + data.ID + " · " + (string.IsNullOrEmpty(data.Name) ? "(未命名)" : data.Name),
                EditorStyles.boldLabel, GUILayout.ExpandWidth(false));

            bool dirty = SkillEditorModel.IsDirty(data.ID);
            GUI.backgroundColor = dirty ? new Color(0.94f, 0.62f, 0.15f) : new Color(0.29f, 0.78f, 0.65f);
            GUILayout.Label(dirty ? "有未保存改动" : "已保存", GUI.skin.box, GUILayout.Width(90));
            GUI.backgroundColor = Color.white;

            GUILayout.FlexibleSpace();

            GUI.enabled = dirty;
            if (GUILayout.Button("保存", GUILayout.Width(60)))
            {
                DoSave(data);
            }
            if (GUILayout.Button("放弃改动", GUILayout.Width(80)))
            {
                SkillEditorModel.Revert(data.ID);
                Repaint();
            }
            GUI.enabled = true;

            if (GUILayout.Button("重载配置", GUILayout.Width(80)))
            {
                SkillEditorModel.Refresh();
                ForceMenuTreeRebuild();
                _selected = null;
            }
            EditorGUILayout.EndHorizontal();

            // 第二行：Scene Gizmo 与播放头模拟
            EditorGUILayout.BeginHorizontal();
            _gizmoEnabled = EditorGUILayout.ToggleLeft("Scene 显示命中范围", _gizmoEnabled, GUILayout.Width(150));
            GUI.enabled = _gizmoEnabled;
            _gizmoOpts.ShowAttackDist = EditorGUILayout.ToggleLeft("进攻距离", _gizmoOpts.ShowAttackDist, GUILayout.Width(80));
            _gizmoOpts.ShowRange = EditorGUILayout.ToggleLeft("命中范围", _gizmoOpts.ShowRange, GUILayout.Width(80));
            _gizmoOpts.ShowWarning = EditorGUILayout.ToggleLeft("警示圈", _gizmoOpts.ShowWarning, GUILayout.Width(70));
            _gizmoOpts.ShowLabels = EditorGUILayout.ToggleLeft("数值标注", _gizmoOpts.ShowLabels, GUILayout.Width(80));
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            _simulatePlayhead = EditorGUILayout.ToggleLeft("模拟播放头", _simulatePlayhead, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
        }

        // ── 校验面板：把「进 Play 才看得到」的红字提前到这里 ────
        private void DrawValidation(SkillData data)
        {
            List<string> errs = new List<string>();
            List<string> warns = new List<string>();

            int segCount = data.SegCount;
            int timingCount = data.SegTimings != null ? data.SegTimings.Length : 0;

            // 段数 vs 时机点（SkillData.cs:165 注释里明确的两个告警分支）
            if (segCount > 0 && timingCount == 0)
                errs.Add("配了 SegCount=" + segCount + " 但没有 SegTimings，没有时机就无法推进，实际结算 0 段。");
            else if (segCount > timingCount && timingCount > 0)
                warns.Add("SegCount(" + segCount + ") 多于时机点(" + timingCount + ")，多余的段不会出现。");
            else if (segCount < timingCount)
                warns.Add("时机点(" + timingCount + ") 多于 SegCount(" + segCount + ")，多余的时机点会被忽略。");

            // 时机必须升序（SkillData.cs:224 注释：不自动排序，只警告）
            if (timingCount > 1)
            {
                for (int i = 1; i < timingCount; i++)
                {
                    if (data.SegTimings[i] < data.SegTimings[i - 1])
                    {
                        errs.Add("SegTimings 第 " + (i + 1) + " 点(" + data.SegTimings[i] + ") 小于前一点，必须升序。");
                        break;
                    }
                }
                for (int i = 0; i < timingCount; i++)
                {
                    float v = data.SegTimings[i];
                    if (v < 0f || v > 1f)
                    {
                        errs.Add("SegTimings 第 " + (i + 1) + " 点 = " + v + "，超出归一化范围 0~1。");
                        break;
                    }
                }
            }

            // 出手特效配对（SkillData.cs:343 GetCastEffectCount 取两者较小值）
            int effIdCount = 0;
            if (data.AttackEffects != null)
            {
                for (int i = 0; i < data.AttackEffects.Count; i++)
                    if (data.AttackEffects[i] > 0) effIdCount++;
            }
            int effTimingCount = data.CastEffectTimings != null ? data.CastEffectTimings.Length : 0;
            if (effTimingCount > effIdCount)
                warns.Add("出手特效时机有 " + effTimingCount + " 条，但有效特效 id 只有 " + effIdCount + " 个 —— 多出的时机点不会有东西可播。");
            else if (effIdCount > effTimingCount)
                warns.Add("出手特效 id 有 " + effIdCount + " 个，但时机只有 " + effTimingCount + " 条 —— 多出的特效不会播出。");

            // 弹道绑定（SkillDataManager.ValidateProjectileBindings 的对应项）
            if (data.DeliveryType == SkillDeliveryType.Projectile && data.ProjectileId <= 0)
                errs.Add("投递方式为「弹道」，但 ProjectileId 为 0，运行时会拿不到弹道配置。");

            // 逐段效果组数 vs 段数（对齐 SkillData.ValidateEffectTypes②）
            // ⚠ 只有 1 组时是「没写 |、全部效果集中在第 1 段」的合法常规写法，不做段数校验
            int groupCount = data.EffectTypesBySegment != null ? data.EffectTypesBySegment.Length : 0;
            if (groupCount > 1 && data.SegCount > 0 && groupCount != data.SegCount)
            {
                warns.Add("逐段效果有 " + groupCount + " 组，但 SegCount=" + data.SegCount + " —— " +
                          (groupCount > data.SegCount
                              ? "多出的组用不上（可在时间轴下方一键截断）"
                              : "少掉的段没有任何效果（GetEffectTypesForSegment 返回 null，不复用第 1 组）"));
            }

            if (errs.Count == 0 && warns.Count == 0) return;

            for (int i = 0; i < errs.Count; i++)
                EditorGUILayout.HelpBox(errs[i], MessageType.Error);
            for (int i = 0; i < warns.Count; i++)
                EditorGUILayout.HelpBox(warns[i], MessageType.Warning);
            EditorGUILayout.Space(4);
        }

        // ── 施法时间轴 ──────────────────────────────────────────
        private void DrawTimeline(SkillData data)
        {
            bool hasData = data.SegCount > 0 &&
                           ((data.SegTimings != null && data.SegTimings.Length > 0) ||
                            (data.CastEffectTimings != null && data.CastEffectTimings.Length > 0));

            float h = SkillTimelineDrawer.GetHeight(hasData);
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(h), GUILayout.ExpandWidth(true));

            float head = _playhead;
            if (_simulatePlayhead)
            {
                // 未进 Play 时也让播放头走一遍，纯为了让学员看清触发顺序
                double t = EditorApplication.timeSinceStartup;
                head = (float)((t % 2.0) / 2.0);
            }

            bool changed = SkillTimelineDrawer.Draw(r, data, head, true, true);
            if (changed)
            {
                // 时机点被拖动 ⇒ 必须让 SkillData 的一次性特效缓存失效，
                // 否则会出现「改了 A、播的还是 B 且不报错」这种最难查的 bug
                SkillEditorModel.InvalidateEffectCache(data);
                Repaint();
            }
            EditorGUILayout.Space(4);
            DrawSegmentTools(data);
        }

        // ── 六张分组卡 ──────────────────────────────────────────
        private void DrawGroups(SkillData data)
        {
            _formScroll = EditorGUILayout.BeginScrollView(_formScroll);

            FieldGroup[] order = SkillFieldMetaTable.GroupOrder;
            for (int g = 0; g < order.Length; g++)
            {
                FieldGroup group = order[g];
                List<SkillFieldMeta> rows = SkillFieldMetaTable.ByGroup(group);

                bool open = _foldouts.Contains(group);
                bool next = BeginBox(SkillFieldMetaTable.GroupName(group), open);
                if (next != open)
                {
                    if (next) _foldouts.Add(group); else _foldouts.Remove(group);
                }

                if (next)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        DrawField(data, rows[i]);
                    }
                }
                EndBox(next);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>带标题的折叠卡片；返回 true = 展开。</summary>
        private static bool BeginBox(string title, bool open)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            bool next = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            return next;
        }

        private static void EndBox(bool wasOpen)
        {
            if (wasOpen) EditorGUILayout.Space(2);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        // ── 单个字段的控件分派 ──────────────────────────────────
        private void DrawField(SkillData data, SkillFieldMeta meta)
        {
            object cur = SkillEditorModel.Get(data, meta);

            EditorGUI.BeginChangeCheck();
            object next = null;

            if (cur == null)
            {
                EditorGUILayout.LabelField(meta.Label, "(未配置)");
            }
            else if (cur is Enum)
            {
                Enum e = (Enum)cur;
                next = EditorGUILayout.EnumPopup(meta.Label, e);
            }
            else if (cur is long || cur is int)
            {
                int v = Convert.ToInt32(cur);
                next = meta.HasRange
                    ? EditorGUILayout.IntSlider(meta.Label, v, (int)meta.Min, (int)meta.Max)
                    : EditorGUILayout.IntField(meta.Label, v);
            }
            else if (cur is float || cur is double)
            {
                float v = Convert.ToSingle(cur);
                next = meta.HasRange
                    ? EditorGUILayout.Slider(meta.Label, v, meta.Min, meta.Max)
                    : EditorGUILayout.FloatField(meta.Label, v);
            }
            else if (cur is string)
            {
                next = EditorGUILayout.TextField(meta.Label, (string)cur);
            }
            else if (cur is List<int>)
            {
                DrawIntListLine(data, meta, (List<int>)cur);
            }
            else if (cur is float[])
            {
                DrawFloatArrayLine(data, meta, (float[])cur);
            }
            else if (cur is List<int>[])
            {
                DrawSegmentEffects(data, meta, (List<int>[])cur);
            }
            else
            {
                EditorGUILayout.LabelField(meta.Label, "(暂不支持的类型：" + cur.GetType().Name + ")");
            }

            if (EditorGUI.EndChangeCheck() && next != null)
            {
                SkillEditorModel.Set(data, meta, next);
                SkillEditorModel.InvalidateEffectCache(data);

                // ★ 段数是「上游」：改它必须同步逐段效果表的组数。
                //   否则 SkillData.GetEffectTypesForSegment 规则②（超出组数返回 null、不复用第 1 组）
                //   会让新加的段「什么都不做」，且加载期 ValidateEffectTypes② 指名告警。
                if (meta.MemberName == "SegCount") SyncSegmentEffects(data);
            }

            if (!string.IsNullOrEmpty(meta.Tooltip))
            {
                Rect last = GUILayoutUtility.GetLastRect();
                GUI.Label(last, new GUIContent("", meta.Tooltip));
            }
        }

        /// <summary>
        /// List&lt;int&gt; 用「分号分隔文本」编辑 —— 与 CSV 原始填写形式一致，
        /// 学员改完一列就知道 JSON 里长什么样。
        /// </summary>
        private void DrawIntListLine(SkillData data, SkillFieldMeta meta, List<int> list)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(list[i]);
            }
            string txt = EditorGUILayout.TextField(meta.Label, sb.ToString());
            List<int> parsed = ParseIntList(txt);
            if (!SameIntList(list, parsed))
            {
                SkillEditorModel.Set(data, meta, parsed);
                SkillEditorModel.InvalidateEffectCache(data);
            }
        }

        private void DrawFloatArrayLine(SkillData data, SkillFieldMeta meta, float[] arr)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(arr[i].ToString("0.###"));
            }
            string txt = EditorGUILayout.TextField(meta.Label, sb.ToString());
            float[] parsed = ParseFloatArray(txt);
            if (!SameFloatArray(arr, parsed))
            {
                SkillEditorModel.Set(data, meta, parsed);
                SkillEditorModel.InvalidateEffectCache(data);
            }
        }

        /// <summary>
        /// 逐段效果号 —— 每段一行：段号标签 + 已选效果号 chips + 「＋」下拉添加。
        /// ⚠ 空段必须显示出来，不能留白（SkillData.cs:77 注释：空段 = 什么都不做）。
        ///
        /// 【为什么必须是 chips + 下拉，而不是一个裸文本框】
        ///   SkillEffectTypes 共 23 个值，其中 19 个目前是空桩（SkillEffectRegistry.s_KnownStubIds）。
        ///   手填数字等于让策划在 23 个数字里盲选，选中空桩后的表现是「配了却什么都没发生」——
        ///   这属于铁律 5 明令禁止的静默降级。
        ///
        /// 【为什么未实现的一律标红】不是装饰，而是把「这个号目前打出去不会有任何反应」
        ///   这条信息从「进 Play 才发现」前置到「配置当场就能看见」。
        ///
        /// 【为什么还留着文本框】批量粘贴 / 整串改写时打字比点下拉快；
        ///   折叠起来不占视线，但老路径一条都不删。
        /// </summary>
        private void DrawSegmentEffects(SkillData data, SkillFieldMeta meta, List<int>[] segs)
        {
            // 取不到 meta 就是契约破损，必须指名 —— 静默跳过会让「改了没生效」无从查起
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] DrawSegmentEffects 收到空的字段元数据，无法绘制 —— "
                               + "请检查 SkillFieldMetaTable 是否仍含 EffectTypesBySegment。");
                return;
            }

            EditorGUILayout.LabelField(meta.Label, GUILayout.Width(90));
            EditorGUI.indentLevel++;
            if (segs == null || segs.Length == 0)
            {
                EditorGUILayout.LabelField("（整条都没配 —— 所有段都不会有任何效果）");
            }
            else
            {
                for (int s = 0; s < segs.Length; s++)
                {
                    DrawOneSegmentEffectRow(data, meta, segs, s);
                }
            }

            // 高级 · 文本编辑：与 chips 完全同源，只是换一种输入方式
            _effectTextOpen = EditorGUILayout.Foldout(_effectTextOpen, "高级 · 文本编辑", true);
            if (_effectTextOpen && segs != null)
            {
                for (int s = 0; s < segs.Length; s++)
                {
                    DrawSegmentEffectTextField(data, meta, segs, s);
                }
            }
            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 一段 = 一行：段号标签 + 已选效果 chip 一排 + 行尾「＋」。
        /// 段号标签上的两个标注（超出段数 / 空段）是「为什么这一行不起作用」的唯一线索，不许省。
        /// </summary>
        private void DrawOneSegmentEffectRow(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s)
        {
            List<int> picked = segs[s];

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(SegmentRowLabel(data, segs, s), EditorStyles.miniLabel,
                GUILayout.Width(RowLabelWidth));

            if (picked != null)
            {
                for (int i = 0; i < picked.Count; i++)
                {
                    DrawEffectChip(data, meta, segs, s, picked[i]);
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("＋", GUILayout.Width(22)))
            {
                ShowEffectPicker(data, meta, segs, s);
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 段号标签 —— 「第 N 段」+ 两个不生效原因标注。
        /// ① 超出段数：GetEffectTypesForSegment 返回 null，这一组永远不会被读到。
        /// ② 空段：SkillData.cs:77 注释，空段 = 什么都不做。
        /// </summary>
        private static string SegmentRowLabel(SkillData data, List<int>[] segs, int s)
        {
            string label = "第 " + (s + 1) + " 段";
            if (s >= data.SegCount) label += "（超出段数·不生效）";
            else if (segs[s] == null || segs[s].Count == 0) label += "（空·不结算）";
            return label;
        }

        /// <summary>
        /// 单个效果号 chip —— 未实现 / 越界的一律染红，悬停给出原因，点一下即删。
        ///
        /// 【为什么用 GUI.backgroundColor 而不是自绘 DrawRect】
        ///   本工程这份 Odin dll 里没有 SirenixEditorGUI（2026-10-12 编译实锤 CS0103），
        ///   一律只用标准 UnityEditor API。backgroundColor 对 EditorStyles.miniButton 的底图是
        ///   正片叠底，深浅皮肤下都还能看清文字，比自绘 rect 更不容易踩对比度的坑。
        /// </summary>
        private void DrawEffectChip(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s, int value)
        {
            bool valid = value >= 0 && value < SkillEffectTypeCatalog.Count;
            if (!valid) ReportBadEffectValue(data, s, value);

            bool stub = valid && SkillEffectTypeCatalog.IsStub(value);
            string label = valid ? SkillEffectTypeCatalog.OptionLabel(value) : (value + " 未知（越界）");

            Color old = GUI.backgroundColor;
            if (stub || !valid) GUI.backgroundColor = StubColor;

            GUIContent content = ChipContent(value, label, stub);
            float w = EditorStyles.miniButton.CalcSize(content).x + 10f;
            bool clicked = GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(w));

            GUI.backgroundColor = old;

            if (clicked)
            {
                ApplyEffectToggle(data, meta, segs, s, value, true);
            }
        }

        /// <summary>
        /// chip 的 GUIContent 缓存 —— 23 个号的文本是静态的，没必要每帧 new 一个（零 GC 铁律）。
        /// 空桩集合 s_KnownStubIds 是编译期常量，域重载时静态字段会一起清掉，缓存不会腐化。
        /// </summary>
        private static GUIContent ChipContent(int value, string label, bool stub)
        {
            if (value < 0 || value >= _chipContents.Length)
            {
                return new GUIContent(label, BadValueTip);
            }
            GUIContent cached = _chipContents[value];
            if (cached == null)
            {
                cached = new GUIContent(label, stub ? StubTip : "");
                _chipContents[value] = cached;
            }
            return cached;
        }

        /// <summary>
        /// 「＋」的下拉：已实现的在前，未实现的压在「── 未实现 ──」分隔条下面。
        ///
        /// 【为什么必须分组】GenericMenu 改不了条目颜色，只靠 OptionLabel 自带的「（未实现）」
        ///   后缀在 23 条里扫一眼仍容易漏 —— 分组之后「往下越过分隔条 = 进去就是空桩」，
        ///   和后缀一起构成双保险。
        /// 【为什么点已勾选项 = 取消】下拉是这一组效果号的开关，与「点 chip = 删除」是同一套语义。
        /// </summary>
        private void ShowEffectPicker(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s)
        {
            GenericMenu menu = new GenericMenu();
            List<int> picked = segs[s];

            for (int v = 0; v < SkillEffectTypeCatalog.Count; v++)
            {
                if (SkillEffectTypeCatalog.IsStub(v)) continue;
                menu.AddItem(new GUIContent(SkillEffectTypeCatalog.OptionLabel(v)),
                    ContainsValue(picked, v), OnPickEffect,
                    new EffectPick(data, meta, segs, s, v));
            }

            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent("── 未实现 ──"));
            menu.AddSeparator("");

            for (int v = 0; v < SkillEffectTypeCatalog.Count; v++)
            {
                if (!SkillEffectTypeCatalog.IsStub(v)) continue;
                menu.AddItem(new GUIContent(SkillEffectTypeCatalog.OptionLabel(v)),
                    ContainsValue(picked, v), OnPickEffect,
                    new EffectPick(data, meta, segs, s, v));
            }

            menu.ShowAsContext();
        }

        private void OnPickEffect(object userData)
        {
            EffectPick p = userData as EffectPick;
            if (p == null)
            {
                Debug.LogError("[技能编辑器] 效果号下拉回调收到空的上下文，本次选择已丢弃 —— 这是编辑器内部错误。");
                return;
            }
            // toggle：已选中再点一次 = 取消，与点 chip 删除一致
            ApplyEffectToggle(p.Data, p.Meta, p.Segs, p.Seg, p.Value, ContainsValue(p.Segs[p.Seg], p.Value));
        }

        /// <summary>
        /// 增删一个效果号 —— 一律走 CloneSegments 写时拷贝。
        /// ⚠ 不能就地改 segs：SkillEditorModel 的快照是深拷贝，就地改会让「原始值」跟着变，
        ///   脏标记与最小 diff 全部失效（表现为「改了却保存不进去」）。
        /// </summary>
        private void ApplyEffectToggle(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s,
                                       int value, bool remove)
        {
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] 技能 " + data.ID + "：逐段效果号缺少字段元数据，改动未生效。");
                return;
            }

            List<int>[] copy = CloneSegments(segs);
            if (copy[s] == null) copy[s] = new List<int>();
            bool had = ContainsValue(copy[s], value);

            if (remove)
            {
                if (!had) return;
                copy[s].Remove(value);
            }
            else
            {
                if (had) return;
                copy[s].Add(value);
            }

            SkillEditorModel.Set(data, meta, copy);
            Repaint();
        }

        /// <summary>高级 · 文本编辑：与旧版同样的「; 分隔」写法，只是空段显示空串而不是「无效果」。</summary>
        private void DrawSegmentEffectTextField(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s)
        {
            string body = JoinIntList(segs[s]);
            string next = EditorGUILayout.TextField(SegmentRowLabel(data, segs, s), body);
            if (next == body) return;

            List<int>[] copy = CloneSegments(segs);
            copy[s] = ParseIntList(next);
            SkillEditorModel.Set(data, meta, copy);
        }

        /// <summary>越界效果号只报一次 —— OnGUI 每秒跑几十帧，同一次报错刷屏等于没人看。</summary>
        private void ReportBadEffectValue(SkillData data, int seg, int value)
        {
            long key = ((long)data.ID << 32) | (uint)value;
            if (_reportedBadEffect.Contains(key)) return;
            _reportedBadEffect.Add(key);

            Debug.LogError("[技能编辑器] 技能 " + data.ID + " 第 " + (seg + 1) + " 段：效果号 "
                + value + " 超出合法范围 0~" + (SkillEffectTypeCatalog.Count - 1)
                + " —— 运行时不会有任何效果，请改配置或补 SkillEffectTypes 枚举。");
        }

        private static bool ContainsValue(List<int> list, int value)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
            return false;
        }

        private static string JoinIntList(List<int> list)
        {
            if (list == null || list.Count == 0) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(list[i]);
            }
            return sb.ToString();
        }

        /// <summary>下拉回调的上下文 —— GenericMenu 的 userData 只能带一个 object。</summary>
        private sealed class EffectPick
        {
            public SkillData Data;
            public SkillFieldMeta Meta;
            public List<int>[] Segs;
            public int Seg;
            public int Value;

            public EffectPick(SkillData data, SkillFieldMeta meta, List<int>[] segs, int seg, int value)
            {
                Data = data;
                Meta = meta;
                Segs = segs;
                Seg = seg;
                Value = value;
            }
        }

        // ── 段数联动（2026-10-12 用户要求）────────────────────
        /// <summary>
        /// ★ 改「伤害段数」时把逐段效果表补齐到同样组数。
        ///
        /// 【为什么必须联动】GetEffectTypesForSegment 规则②：段号超出已配组数返回 null，
        ///   且刻意**不复用第 1 组** ⇒ 只改 SegCount 不加组，新段就是什么都不做。
        ///
        /// 【为什么新段默认填 ImmedDamage(0) 而不是留空】ValidateEffectTypes③：某段效果不含 0
        ///   就不结算伤害。留空虽不报错，但学员会看到「加了段却没掉血」这种最难描述的画面；
        ///   0 是段的基础语义（先掉血，再往上叠别的）。这是**可见、可撤销**的默认值，不是运行时静默降级。
        ///
        /// 【为什么不截断多余的组】段数调小时截断会丢配置且不可复原。
        ///   保留 + 界面标注「不生效」+ 一键截断按钮，比自动删数据安全。
        /// </summary>
        private void SyncSegmentEffects(SkillData data)
        {
            int want = data.SegCount;
            if (want <= 0) return;   // 段数归零 = 不分段；逐段表留着也不会被执行，不动它（ValidateEffectTypes① 会告警点名）

            SkillFieldMeta meta = SkillFieldMetaTable.Find("EffectTypesBySegment");
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] 契约表缺 EffectTypesBySegment，无法同步段数 —— 请检查 SkillFieldMetaTable");
                return;
            }

            List<int>[] cur = SkillEditorModel.Get(data, meta) as List<int>[];
            if (cur != null && cur.Length >= want) return;   // 已经够了（长度超出段数的情况交给界面标注，不自动删）

            List<int>[] next = new List<int>[want];
            int added = 0;
            for (int i = 0; i < want; i++)
            {
                bool hasOld = cur != null && i < cur.Length && cur[i] != null && cur[i].Count > 0;
                if (hasOld)
                {
                    next[i] = new List<int>(cur[i]);
                }
                else
                {
                    next[i] = new List<int> { 0 };   // 新增段：默认先结算一次伤害
                    added++;
                }
            }

            if (SkillEditorModel.Set(data, meta, next))
            {
                Debug.Log($"[技能编辑器] 技能 {data.ID}：段数 → {want}，逐段效果表已补齐到 {want} 组" +
                          (added > 0 ? $"（新增 {added} 组，默认 ImmedDamage(0)，可再改）" : ""));
                Repaint();
            }
        }

        /// <summary>
        /// 段数 / 时机点 / 效果组 的三方核对条 —— 这三列不一致是「加了段却没伤害」的头号原因。
        ///
        /// ⚠ 为什么时机点不自动补：生成时机是策划意图（均分只是其中一种写法），
        ///   自动写会让「我明明配过 0.3/0.6/0.9」被悄悄覆盖 —— 那是数据失真，比少一个按钮严重得多。
        ///   所以只给按钮，由人点，且点完会打日志留下痕。
        /// </summary>
        private void DrawSegmentTools(SkillData data)
        {
            int segs = data.SegCount;
            int timing = data.SegTimings != null ? data.SegTimings.Length : 0;
            int group = data.EffectTypesBySegment != null ? data.EffectTypesBySegment.Length : 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"段数 {segs} · 时机点 {timing} · 效果组 {group}",
                EditorStyles.miniLabel, GUILayout.Width(210));

            if (segs > 0 && timing != segs && GUILayout.Button("按段数均分时机点", GUILayout.Width(130)))
            {
                EvenSplitTimings(data, segs);
            }
            // 存量不一致的修复入口：段数已被改过（或旧数据本来就不齐）时，联动不会自动补，必须给按钮
            if (segs > 0 && group < segs && GUILayout.Button("补齐效果组到 " + segs + " 段", GUILayout.Width(150)))
            {
                SyncSegmentEffects(data);
            }
            if (segs > 0 && group > segs && GUILayout.Button("截断效果组到 " + segs + " 段", GUILayout.Width(140)))
            {
                TrimEffectGroups(data, segs);
            }
            if (segs > 0 && timing == segs && group == segs)
            {
                EditorGUILayout.LabelField("三列一致 ✓", EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (segs > 0 && timing == 0)
            {
                EditorGUILayout.HelpBox("段数 > 0 但一个时机点都没有 —— 没有时机就无法推进，实际结算 0 段。点上面按钮补齐。",
                    MessageType.Error);
            }
        }

        /// <summary>
        /// 按段数均分时机点 —— 取 0~0.9 而不是 0~1：
        /// 末段留 10% 给收招（动画播完走 TryFinishSkill），贴到 1.0 有被 OnExit 打断、段被吞掉的风险。
        /// 现表 10002 就是 0.3/0.6/0.9，正好是这个算法的 3 段结果 —— 与既有配置风格一致。
        /// </summary>
        private void EvenSplitTimings(SkillData data, int n)
        {
            SkillFieldMeta meta = SkillFieldMetaTable.Find("SegTimings");
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] 契约表缺 SegTimings，无法均分 —— 请检查 SkillFieldMetaTable");
                return;
            }

            float[] arr = new float[n];
            for (int i = 0; i < n; i++) arr[i] = (i + 1) * 0.9f / n;

            string before = data.SegTimings != null ? string.Join(";", data.SegTimings) : "(空)";
            if (SkillEditorModel.Set(data, meta, arr))
            {
                SkillEditorModel.InvalidateEffectCache(data);
                Debug.Log($"[技能编辑器] 技能 {data.ID}：段时机 {before} → {string.Join(";", arr)}（按 {n} 段均分 0~0.9）");
                Repaint();
            }
        }

        /// <summary>把多出来的效果组截掉 —— 只在人点按钮时发生，且会打日志留痕。</summary>
        private void TrimEffectGroups(SkillData data, int n)
        {
            SkillFieldMeta meta = SkillFieldMetaTable.Find("EffectTypesBySegment");
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] 契约表缺 EffectTypesBySegment，无法截断 —— 请检查 SkillFieldMetaTable");
                return;
            }

            List<int>[] cur = SkillEditorModel.Get(data, meta) as List<int>[];
            if (cur == null || cur.Length <= n) return;

            List<int>[] next = new List<int>[n];
            for (int i = 0; i < n; i++) next[i] = cur[i] != null ? new List<int>(cur[i]) : null;

            if (SkillEditorModel.Set(data, meta, next))
            {
                Debug.Log($"[技能编辑器] 技能 {data.ID}：逐段效果表 {cur.Length} 组 → {n} 组（截断）");
                Repaint();
            }
        }

        // ── 底部：未保存清单 ────────────────────────────────────
        private void DrawDirtyFooter()
        {
            List<int> ids = SkillEditorModel.DirtyIds();
            if (ids == null || ids.Count == 0) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("未保存改动（" + ids.Count + " 个技能）", EditorStyles.boldLabel);
            bool anyNew = false;
            for (int i = 0; i < ids.Count && i < 12; i++)
            {
                int id = ids[i];

                // ★ 区分「改了没存」与「新建未落盘」：
                //   后者不在磁盘上，Refresh() 重建缓存时会连同用户填好的字段一起消失且不报错，
                //   必须在清单里单独标红，否则用户以为它还在、继续往下配。
                bool isNew = SkillEditorModel.IsNew(id);
                if (isNew) anyNew = true;

                EditorGUILayout.BeginHorizontal();
                Color prevColor = GUI.color;
                if (isNew) GUI.color = StubColor;
                EditorGUILayout.LabelField(isNew ? ("技能 " + id + "（新建·未落盘）") : ("技能 " + id));
                GUI.color = prevColor;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("保存", GUILayout.Width(50)))
                {
                    SkillData d = SkillEditorModel.ById(id);
                    if (d != null) DoSave(d);
                }
                EditorGUILayout.EndHorizontal();
            }
            if (ids.Count > 12) EditorGUILayout.LabelField("... 还有 " + (ids.Count - 12) + " 个");

            // ★ 最后一道闸：新建技能只活在编辑器内存里。
            //   重编译 / 进 Play / 点「重载配置」都会按磁盘数据重建缓存 ⇒ 它会被静默丢掉。
            //   这句话必须在「全部保存」按钮之前说清楚，否则用户点了保存也没意识到紧迫性。
            if (anyNew)
            {
                EditorGUILayout.HelpBox("带「新建·未落盘」标记的技能目前只存在于编辑器内存，"
                    + "重编译 / 进入 Play / 重载配置都会丢失。请先点「保存」把它写进 SkillData.json。",
                    MessageType.Warning);
            }
            if (GUILayout.Button("全部保存 (" + ids.Count + ")"))
            {
                int n = SkillEditorModel.SaveAll();
                if (n > 0) ForceMenuTreeRebuild();
            }
            EditorGUILayout.EndVertical();
        }

        private void DoSave(SkillData data)
        {
            if (SkillEditorModel.Save(data.ID))
            {
                SkillEditorModel.InvalidateEffectCache(data);
                ForceMenuTreeRebuild();
                Debug.Log("[技能编辑器] 已保存技能 " + data.ID);
            }
        }

        // ══════════════════════════════════════════════════════════
        // Scene 视图 Gizmo
        // ══════════════════════════════════════════════════════════
        // ⚠ 必须是静态方法：域重载后实例字段会被重置，若用实例方法回调会跟着丢，
        //   而委托还挂在 SceneView 上 ⇒ 空引用。静态方法 + 静态 owner 引用最稳。
        private static void StaticSceneGui(SceneView view)
        {
            SkillEditorWindow w = _sceneHookOwner;
            if (w == null || !w._gizmoEnabled || w._selected == null) return;

            Transform anchor = SkillGizmoDrawer.ResolveAnchor();
            if (anchor == null) return;
            SkillGizmoDrawer.Draw(anchor, w._selected, w._gizmoOpts);
        }

        // ══════════════════════════════════════════════════════════
        // 小工具
        // ══════════════════════════════════════════════════════════
        /// <summary>
        /// 一条细分隔线。
        /// ⚠ 刻意不用 Odin 的 SirenixEditorGUI —— 本工程这份 Odin dll 里没有这个类型
        ///   （2026-10-12 编译实锤：error CS0103）。Odin 在这里能用的只有窗口类与菜单树，
        ///   界面绘制 API 一律用标准 UnityEditor 的。
        /// </summary>
        private static void DrawSeparator()
        {
            Rect r = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(r, new Color(0.38f, 0.39f, 0.43f, 0.7f));
        }

        /// <summary>未实现 / 越界效果号的 chip 底色。</summary>
        private static readonly Color StubColor = new Color(0.95f, 0.34f, 0.32f);
        private const string StubTip = "该效果号尚未实现，配了不会有任何反应";
        private const string BadValueTip = "效果号超出 SkillEffectTypes 的合法范围，配了不会有任何反应";
        private const float RowLabelWidth = 190f;

        /// <summary>效果号 chip 的 GUIContent 缓存（下标 = 效果号）。</summary>
        private static readonly GUIContent[] _chipContents = new GUIContent[SkillEffectTypeCatalog.Count];

        /// <summary>已报过警的越界效果号（技能 ID << 32 | 值），避免每帧刷屏。</summary>
        private static readonly HashSet<long> _reportedBadEffect = new HashSet<long>();

        private static List<int> ParseIntList(string txt)
        {
            List<int> list = new List<int>();
            if (string.IsNullOrEmpty(txt)) return list;
            string[] parts = txt.Split(';', ',', ' ');
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.Length == 0) continue;
                int v;
                if (int.TryParse(s, out v)) list.Add(v);
                else Debug.LogWarning("[技能编辑器] 无法解析为整数，已忽略：" + s);
            }
            return list;
        }

        private static float[] ParseFloatArray(string txt)
        {
            float[] empty = new float[0];
            if (string.IsNullOrEmpty(txt)) return empty;
            string[] parts = txt.Split(';', ',', ' ');
            List<float> tmp = new List<float>();
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.Length == 0) continue;
                float v;
                if (float.TryParse(s, out v)) tmp.Add(v);
                else Debug.LogWarning("[技能编辑器] 无法解析为小数，已忽略：" + s);
            }
            return tmp.ToArray();
        }

        private static bool SameIntList(List<int> a, List<int> b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool SameFloatArray(float[] a, float[] b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-4f) return false;
            return true;
        }

        private static List<int>[] CloneSegments(List<int>[] src)
        {
            List<int>[] copy = new List<int>[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                copy[i] = src[i] != null ? new List<int>(src[i]) : null;
            }
            return copy;
        }
    }
}
