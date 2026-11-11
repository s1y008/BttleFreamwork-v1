// ★ 技能编辑器 · 新建技能向导
//
// 【职责】把「新建一条技能」这件事的所有易错点收在一个窗口里：
//   ① 来源二选一（复制现有 / 空白模板）；② ID 冲突当场拦下；③ 名称不能空；④ 提醒落盘与重进 Play。
//
// 【为什么单开一个窗口而不是塞进右侧面板】
//   新建要占三行输入 + 说明文字，塞进右侧会把它挤到滚动区里看不见；
//   而且新建是**一次性动作**，做完就该消失 —— 用 EditorWindow 比常驻控件更贴合。
//
// 【⚠ Odin 纪律】本文件不使用任何 Odin 绘制 API（本工程 dll 无 SirenixEditorGUI，
//   见 SkillEditorWindow.DrawSeparator 的注释）。全部用标准 UnityEditor 控件。
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkillEditorKit
{
    public class SkillCreateWizard : EditorWindow
    {
        /// <summary>推荐复制源：全表 236 个里唯一配了完整时间轴的技能。</summary>
        private const int RecommendSourceId = 10002;

        private SkillEditorWindow _owner;

        // ── ① 来源 ────────────────────────────────────────────
        private bool _cloneMode = true;
        private int[] _sourceIds = new int[0];
        private string[] _sourceLabels = new string[0];
        private int _sourceIndex;

        // ── ②③ ID / 名称 ────────────────────────────────────
        private string _idText = "";
        private string _nameText = "";

        private bool _saveAfterCreate = true;
        private bool _bootstrapped;

        // ══════════════════════════════════════════════════════════
        // 入口
        // ══════════════════════════════════════════════════════════
        public static void Open(SkillEditorWindow owner)
        {
            SkillCreateWizard w = GetWindow<SkillCreateWizard>(true, "新建技能向导", true);
            if (w == null)
            {
                Debug.LogError("[技能编辑器] 无法创建「新建技能向导」窗口（GetWindow 返回 null）。");
                return;
            }
            w._owner = owner;
            w.minSize = new Vector2(430f, 330f);
            w.Bootstrap();
            w.Show();
        }

        private void Bootstrap()
        {
            _bootstrapped = true;

            BuildSourceList();

            int free = SkillEditorModel.NextFreeId();
            if (free < 0)
            {
                // fail-fast：取不到号必须指名，绝不给一个「看起来能用」的默认值
                Debug.LogError("[技能编辑器] SkillEditorModel.NextFreeId() 返回 -1 —— 10000 起的号段已被占满，"
                               + "无法给出默认 ID。请手动填写一个未占用的技能号（≥10000）。");
                _idText = "";
            }
            else
            {
                _idText = free.ToString();
                _nameText = "新技能 " + free;
            }
        }

        private void BuildSourceList()
        {
            IReadOnlyList<SkillData> all = SkillEditorModel.All();
            int n = all != null ? all.Count : 0;
            if (n == 0)
            {
                Debug.LogError("[技能编辑器] SkillEditorModel.All() 为空 —— 「复制现有技能」没有可选来源，请先用「重载配置」。");
                _sourceIds = new int[0];
                _sourceLabels = new string[0];
                _sourceIndex = 0;
                return;
            }

            _sourceIds = new int[n];
            _sourceLabels = new string[n];
            _sourceIndex = 0;

            for (int i = 0; i < n; i++)
            {
                SkillData d = all[i];
                if (d == null) continue;
                _sourceIds[i] = d.ID;
                string nm = string.IsNullOrEmpty(d.Name) ? "(未命名)" : d.Name;
                _sourceLabels[i] = d.ID + " " + nm + "  [" + d.GetSegmentCount() + "段]";
                if (d.ID == RecommendSourceId) _sourceIndex = i;
            }
        }

        // ══════════════════════════════════════════════════════════
        // 绘制
        // ══════════════════════════════════════════════════════════
        private void OnGUI()
        {
            if (!_bootstrapped) Bootstrap();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("新建技能", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawSourceBlock();
            DrawIdBlock();
            DrawNameBlock();
            DrawNotice();
            DrawButtons();
        }

        // ── ① 来源 ────────────────────────────────────────────
        private void DrawSourceBlock()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("① 来源", EditorStyles.boldLabel);

            bool c = EditorGUILayout.ToggleLeft("复制现有技能（默认 " + RecommendSourceId + " —— 全表唯一配了完整时间轴的）", _cloneMode);
            if (c != _cloneMode) _cloneMode = c;          // 点第一个 ⇒ 切到「复制」
            bool b = EditorGUILayout.ToggleLeft("空白模板（只填最小可打出一刀的默认值）", !_cloneMode);
            if (b) _cloneMode = false;                    // 点第二个 ⇒ 切到「空白」

            if (_cloneMode)
            {
                if (_sourceIds.Length == 0)
                {
                    EditorGUILayout.HelpBox("没有读到任何技能，无法复制。请先在主窗口点「重载配置」。", MessageType.Error);
                }
                else
                {
                    _sourceIndex = EditorGUILayout.Popup("复制来源", _sourceIndex, _sourceLabels);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("空白模板：SegCount=1 / 时机点 0.5 / 第 1 段 ImmedDamage(0) / HitNum=-1 / "
                                        + "AttackDist=5 / AttackRadius=3 / AttackAngle=180。其余列留默认值，需要在右侧自行补齐。",
                    MessageType.Info);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        // ── ② ID ──────────────────────────────────────────────
        private void DrawIdBlock()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("② 技能号 ID", EditorStyles.boldLabel);

            _idText = EditorGUILayout.TextField("ID", _idText);

            string err = Validate();
            if (err.Length > 0)
            {
                // 红字必须当场出现 —— ID 冲突是「静默覆盖别人技能」的唯一入口，绝不放行
                Color old = GUI.color;
                GUI.color = new Color(0.95f, 0.34f, 0.32f);
                EditorGUILayout.LabelField(err, EditorStyles.wordWrappedLabel);
                GUI.color = old;
            }
            else
            {
                EditorGUILayout.LabelField("该 ID 可用。", EditorStyles.miniLabel);
            }

            // 上次新建但没落盘、号还占着的情况：给一个显式回收入口，而不是让用户干瞪眼
            int id;
            if (err.Length == 0 && int.TryParse(_idText, out id))
            {
                if (SkillEditorModel.IsIdTaken(id) && SkillEditorModel.IsNew(id))
                {
                    EditorGUILayout.HelpBox("ID " + id + " 是本次会话新建、尚未落盘的技能。"
                        + "它只存在于编辑器内存里，一点「重载配置」/ 重编译就会丢。", MessageType.Warning);
                    if (GUILayout.Button("丢弃这个未落盘的新技能", GUILayout.Height(22)))
                    {
                        SkillEditorModel.DiscardNew(id);
                        Debug.Log("[技能编辑器] 已丢弃未落盘的新建技能 " + id + "，ID " + id + " 重新可用。");
                        BuildSourceList();
                        Repaint();
                    }
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        // ── ③ 名称 ────────────────────────────────────────────
        private void DrawNameBlock()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("③ 名称", EditorStyles.boldLabel);
            _nameText = EditorGUILayout.TextField("名称", _nameText);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        // ── 提示 ──────────────────────────────────────────────
        private void DrawNotice()
        {
            // ⚠ 这条是用户点名要写进向导的：配置只在进 Play 时 Load，保存后必须重进才生效
            EditorGUILayout.HelpBox("新建并保存后，仍需重新进入 Play 模式才生效 —— 配置只在进 Play 时 Load，"
                                    + "编辑器里改完不会热更新到正在运行的战斗。", MessageType.Warning);
            EditorGUILayout.HelpBox("未落盘的新建技能只存在于编辑器内存：重编译 / 进 Play / 点「重载配置」都会丢。"
                                    + "所以下面默认勾选「创建后立即保存」。", MessageType.Info);
            _saveAfterCreate = EditorGUILayout.ToggleLeft("创建后立即保存到 SkillData.json", _saveAfterCreate);
        }

        // ── 按钮 ──────────────────────────────────────────────
        private void DrawButtons()
        {
            EditorGUILayout.Space(6);
            string err = Validate();

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = err.Length == 0;
            if (GUILayout.Button("创建", GUILayout.Height(26)))
            {
                int id;
                if (int.TryParse(_idText, out id)) DoCreate(id);
                else Debug.LogError("[技能编辑器] 创建被点到了，但 ID 文本「" + _idText + "」解析不出整数 —— 这是界面状态与校验不一致，已拒绝创建。");
            }
            GUI.enabled = true;

            if (GUILayout.Button("关闭", GUILayout.Height(26), GUILayout.Width(70)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ══════════════════════════════════════════════════════════
        // 校验与创建
        // ══════════════════════════════════════════════════════════
        /// <summary>返回空串 = 可以创建；否则返回要显示的红字（第一行即原因）。</summary>
        private string Validate()
        {
            if (_idText == null || _idText.Trim().Length == 0) return "ID 不能为空。";

            int id;
            if (!int.TryParse(_idText.Trim(), out id)) return "「" + _idText + "」不是整数，ID 只能是数字。";
            if (id < 10000) return "ID 必须 ≥ 10000 —— 10000 以下是内置技能号段，SkillEditorModel.NextFreeId() 也不会给出。";
            if (SkillEditorModel.IsIdTaken(id)) return "ID " + id + " 已被占用，不能覆盖已有技能 —— 请换一个号（可点上方「丢弃」回收未落盘的）。";
            if (_nameText == null || _nameText.Trim().Length == 0) return "名称不能为空。";
            if (_cloneMode && (_sourceIds.Length == 0 || _sourceIndex < 0 || _sourceIndex >= _sourceIds.Length))
                return "没有可选的复制来源 —— 请先在主窗口点「重载配置」。";
            return "";
        }

        private void DoCreate(int id)
        {
            string name = _nameText.Trim();
            bool clone = _cloneMode;
            int sourceId = clone ? _sourceIds[_sourceIndex] : 0;

            SkillData created = clone
                ? SkillEditorModel.CreateClone(sourceId, id, name)
                : SkillEditorModel.CreateBlank(id, name);

            if (created == null)
            {
                // fail-fast：模型层已 LogError 指名，这里再补一条带来源上下文的，方便一眼定位
                Debug.LogError("[技能编辑器] 新建技能 " + id + " 失败：SkillEditorModel."
                               + (clone ? "CreateClone(源 " + sourceId + ")" : "CreateBlank")
                               + " 返回 null。没有写入任何东西，向导保持打开。");
                return;
            }

            Debug.Log("[技能编辑器] 已新建技能 " + id + "「" + name + "」，来源="
                      + (clone ? "复制 " + sourceId : "空白模板"));

            if (_saveAfterCreate)
            {
                if (!SkillEditorModel.Save(id))
                {
                    Debug.LogError("[技能编辑器] 新建技能 " + id + " 已生成，但保存失败 —— "
                                   + "它目前只在内存里，重编译 / 进 Play 会丢。请在主窗口再点一次「保存」。");
                }
            }

            if (_owner != null) _owner.SelectSkillById(id);
            else Debug.LogWarning("[技能编辑器] 主窗口引用已失效（可能发生过域重载），新建技能 " + id + " 未自动选中，请在左树里手动点开。");

            Close();
        }
    }
}
