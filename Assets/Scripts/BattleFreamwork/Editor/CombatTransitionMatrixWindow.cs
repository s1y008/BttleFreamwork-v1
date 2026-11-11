using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 战斗状态转换矩阵 · 编辑 / 保存 / 自检窗口（纯 Editor 工具）。
///
/// 【它的职责发生了什么变化】
///   v1（2026-10-10 上午）：只读推演 —— 反射读代码里的转换表，摊成矩阵，改完复制到剪贴板，
///   由人自己贴回 InitDefaultTransitions。工具不落盘，也就无法在运行时生效。
///   v2（本次）：转换关系**外置成 ScriptableObject 资产** ——
///     编辑 → 保存到 Resources/Config/CombatTransitionTable.asset → 运行时由
///     CombatStateMachine 构造时 Resources.Load 并整体覆盖代码内置表。
///
/// 【为什么仍然保留「从代码导入」】
///   代码内置表是**迁移起点**，也是资产缺失/校验失败时的保底。
///   没有它，一旦资产损坏，状态机就一张表都没有。
///   ⚠ 导入必须走 <c>new CombatStateMachine(false)</c>（关闭资产覆盖），
///     否则会读到资产内容 —— 变成「导入自己」，资产永远无法回到代码基线。
///
/// 【为什么校验调用资产自己的 ValidateRows 而不是在窗口里重写一套】
///   编辑器说「没问题」而运行时拒绝加载，是两套规则最典型的裂缝。
///   现在两边共用 CombatTransitionTable.ValidateRows，规则只有一份。
///
/// 打开方式：菜单栏 Tools → Battle Framework → Transition Matrix
/// </summary>
public class CombatTransitionMatrixWindow : EditorWindow
{
    // ★ 菜单路径必须全 ASCII：Unity 的 MenuItem 对非 ASCII 路径支持不稳定
    private const string MENU_PATH = "Tools/Battle Framework/Transition Matrix";

    private const string PREF_EXCEPTIONS = "BttleFreamwork.TransitionMatrix.KnownExceptions";
    private const string PREF_EDITMODE = "BttleFreamwork.TransitionMatrix.EditMode";
    private const string PREF_ASSET = "BttleFreamwork.TransitionMatrix.AssetPath";

    private const float ROW_HEAD_W = 86f;
    private const float CELL_W = 54f;
    private const float CELL_H = 22f;
    private const float COL_HEAD_H = 24f;

    private static readonly string[] s_LayerNames = { "行动层", "受控层", "终态" };

    private static readonly Color[] s_LayerColors =
    {
        new Color(0.71f, 0.83f, 0.96f),
        new Color(0.98f, 0.78f, 0.46f),
        new Color(0.97f, 0.76f, 0.76f),
    };

    // ───────────────────────── 窗体状态 ─────────────────────────
    private CombatState[] _all;
    private string[] _shortName;
    private bool[,] _matrix;
    private bool[,] _edited;
    private int[] _rowLayer;
    private HashSet<string> _known = new HashSet<string>();

    private CombatTransitionTable _asset;
    private string _sourceLabel = "";

    private bool _editMode;
    private bool _dirty;
    private bool _showOneWay = true;
    private bool _showChecks = true;
    private bool _showLayers = true;
    private Vector2 _scroll;

    private GUIStyle _headStyle;
    private GUIStyle _rowStyle;
    private GUIStyle _cellStyle;
    private string _msg;
    private MessageType _msgType = MessageType.Info;

    [MenuItem(MENU_PATH)]
    public static void Open()
    {
        CombatTransitionMatrixWindow win =
            GetWindow<CombatTransitionMatrixWindow>(false, "战斗状态转换矩阵", true);
        win.minSize = new Vector2(620f, 700f);
        win.Show();
    }

    private void OnEnable()
    {
        _all = (CombatState[])Enum.GetValues(typeof(CombatState));
        _shortName = new string[_all.Length];
        for (int i = 0; i < _all.Length; i++)
        {
            string s = _all[i].ToString();
            _shortName[i] = s.Length > 6 ? s.Substring(0, 5) : s;
        }
        _matrix = new bool[_all.Length, _all.Length];
        _edited = new bool[_all.Length, _all.Length];
        _rowLayer = new int[_all.Length];
        _editMode = EditorPrefs.GetBool(PREF_EDITMODE, false);
        LoadKnown();

        _asset = FindAsset();
        if (_asset != null) SyncFromAsset();
        else ImportFromCode(true);
    }

    private void OnDisable()
    {
        EditorPrefs.SetBool(PREF_EDITMODE, _editMode);
    }

    // ───────────────────────── 资产读写 ─────────────────────────

    /// <summary>按 EditorPrefs 记录的路径找资产；没有则回落到默认路径。</summary>
    private CombatTransitionTable FindAsset()
    {
        string path = EditorPrefs.GetString(PREF_ASSET, CombatTransitionTable.ASSET_PATH);
        CombatTransitionTable table = AssetDatabase.LoadAssetAtPath<CombatTransitionTable>(path);
        if (table != null) return table;
        return AssetDatabase.LoadAssetAtPath<CombatTransitionTable>(CombatTransitionTable.ASSET_PATH);
    }

    private void CreateAsset()
    {
        string dir = Path.GetDirectoryName(CombatTransitionTable.ASSET_PATH);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(CombatTransitionTable.ASSET_PATH))
        {
            SetMsg("资产已存在：" + CombatTransitionTable.ASSET_PATH, MessageType.Warning);
            _asset = FindAsset();
            SyncFromAsset();
            return;
        }

        // 新建时直接用代码内置表填充 —— 保证「资产起点 == 当前线上行为」，迁移零行为差异
        ImportFromCode(false);

        CombatTransitionTable table = ScriptableObject.CreateInstance<CombatTransitionTable>();
        table.Rows = BuildRowsSnapshot();
        table.Version = CombatTransitionTable.CURRENT_VERSION;
        table.Enabled = true;
        AssetDatabase.CreateAsset(table, CombatTransitionTable.ASSET_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        _asset = table;
        EditorPrefs.SetString(PREF_ASSET, CombatTransitionTable.ASSET_PATH);
        CombatTransitionTable.ClearCache();

        SetMsg("已创建资产（用代码内置表填充，共 " + CountEdges() + " 条边）："
               + CombatTransitionTable.ASSET_PATH, MessageType.Info);
        Debug.Log("[TransitionMatrix] 已创建转换表资产：" + CombatTransitionTable.ASSET_PATH
                  + "，边数 " + CountEdges());
    }

    /// <summary>把资产内容读进窗口内存。</summary>
    private void SyncFromAsset()
    {
        Array.Clear(_edited, 0, _edited.Length);
        Array.Clear(_matrix, 0, _matrix.Length);
        _dirty = false;

        if (_asset == null) { ImportFromCode(true); return; }

        for (int i = 0; i < _all.Length; i++)
        {
            _rowLayer[i] = _asset.LayerOf(_all[i]);
            if (_rowLayer[i] < 0) _rowLayer[i] = 0;
        }

        Dictionary<CombatState, HashSet<CombatState>> built =
            new Dictionary<CombatState, HashSet<CombatState>>();
        _asset.BuildTransitions(built);
        foreach (KeyValuePair<CombatState, HashSet<CombatState>> kv in built)
        {
            int fi = IndexOf(kv.Key);
            if (fi < 0) continue;
            foreach (CombatState to in kv.Value)
            {
                int ti = IndexOf(to);
                if (ti >= 0) _matrix[fi, ti] = true;
            }
        }
        _sourceLabel = "资产 " + _asset.name;
        SetMsg("已读取 " + CountEdges() + " 条边（来源：资产 " + _asset.name + "）", MessageType.Info);
    }

    /// <summary>
    /// 从代码内置表导入。
    /// ⚠ 必须传 false 关闭资产覆盖 —— 否则 new 出来的状态机会先读资产，导入到的还是资产内容。
    /// </summary>
    private void ImportFromCode(bool silent)
    {
        Array.Clear(_edited, 0, _edited.Length);
        Array.Clear(_matrix, 0, _matrix.Length);
        _dirty = false;

        for (int i = 0; i < _all.Length; i++)
        {
            int layer = CombatTransitionTable.DefaultLayerOf(_all[i]);
            _rowLayer[i] = layer < 0 ? 0 : layer;
        }

        try
        {
            CombatStateMachine fsm = new CombatStateMachine(false);
            FieldInfo field = typeof(CombatStateMachine).GetField(
                "_transitions", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
            {
                SetMsg("反射取不到 CombatStateMachine._transitions 字段（字段改名了？）", MessageType.Error);
                return;
            }
            IDictionary dict = (IDictionary)field.GetValue(fsm);
            if (dict == null) { SetMsg("_transitions 为空", MessageType.Warning); return; }

            foreach (DictionaryEntry entry in dict)
            {
                int fi = IndexOf((CombatState)entry.Key);
                if (fi < 0) continue;
                IEnumerable<CombatState> set = (IEnumerable<CombatState>)entry.Value;
                foreach (CombatState to in set)
                {
                    int ti = IndexOf(to);
                    if (ti >= 0) _matrix[fi, ti] = true;
                }
            }
            _sourceLabel = "代码内置表";
            if (!silent)
            {
                SetMsg("已从代码内置表导入 " + CountEdges() + " 条边（未写盘，点「保存到资产」生效）",
                    MessageType.Info);
            }
        }
        catch (Exception ex)
        {
            SetMsg("读取失败：" + ex.Message, MessageType.Error);
        }
    }

    /// <summary>把窗口内存里的矩阵 + 分层写回资产，并落盘。</summary>
    private void SaveToAsset()
    {
        if (_asset == null)
        {
            SetMsg("还没有资产可保存 —— 先点「新建资产」或指定一个已有的转换表资产。", MessageType.Warning);
            return;
        }

        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        List<CombatTransitionTable.StateRow> rows = BuildRowsSnapshot();
        bool ok = CombatTransitionTable.ValidateRows(rows, _asset.Version, _asset.name, errors, warnings);

        if (!ok)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("保存已中止：转换关系未通过校验（共 ").Append(errors.Count).Append(" 条）：");
            for (int i = 0; i < errors.Count; i++) sb.Append("\n  · ").Append(errors[i]);
            SetMsg(sb.ToString(), MessageType.Error);
            Debug.LogError("[TransitionMatrix] " + sb);
            return;
        }

        _asset.Rows = rows;
        EditorUtility.SetDirty(_asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CombatTransitionTable.ClearCache();

        Array.Clear(_edited, 0, _edited.Length);
        _dirty = false;

        StringBuilder note = new StringBuilder();
        note.Append("已保存 ").Append(CountEdges()).Append(" 条边 / ").Append(_all.Length)
            .Append(" 个状态到 ").Append(CombatTransitionTable.ASSET_PATH);
        if (warnings.Count > 0) note.Append("（有 ").Append(warnings.Count).Append(" 条警告，见 Console）");
        SetMsg(note.ToString(), MessageType.Info);
        Debug.Log("[TransitionMatrix] " + note
                  + "\n运行时将在下次进入 Play 时通过 Resources.Load(\"" + CombatTransitionTable.RESOURCE_PATH
                  + "\") 生效。");
    }

    /// <summary>把当前内存矩阵打包成资产行（保留已有行的 Note）。</summary>
    private List<CombatTransitionTable.StateRow> BuildRowsSnapshot()
    {
        List<CombatTransitionTable.StateRow> rows = new List<CombatTransitionTable.StateRow>();
        for (int i = 0; i < _all.Length; i++)
        {
            CombatTransitionTable.StateRow row = new CombatTransitionTable.StateRow();
            row.State = _all[i];
            row.Layer = (CombatStateLayer)_rowLayer[i];
            row.Targets = new List<CombatState>();
            for (int j = 0; j < _all.Length; j++)
                if (i != j && _matrix[i, j]) row.Targets.Add(_all[j]);

            row.Note = "";
            if (_asset != null && _asset.Rows != null)
            {
                for (int k = 0; k < _asset.Rows.Count; k++)
                {
                    CombatTransitionTable.StateRow old = _asset.Rows[k];
                    if (old != null && old.State == _all[i]) { row.Note = old.Note; break; }
                }
            }
            rows.Add(row);
        }
        return rows;
    }

    // ───────────────────────── 工具方法 ─────────────────────────

    private int IndexOf(CombatState s)
    {
        for (int i = 0; i < _all.Length; i++)
            if (_all[i] == s) return i;
        return -1;
    }

    private int CountEdges()
    {
        int c = 0;
        for (int i = 0; i < _all.Length; i++)
            for (int j = 0; j < _all.Length; j++)
                if (_matrix[i, j]) c++;
        return c;
    }

    private static string KeyOf(CombatState from, CombatState to)
    {
        return from + ">" + to;
    }

    /// <summary>
    /// 「有意不对称」的记录位置：有资产就写资产（可版本管理），没有资产才退回 EditorPrefs。
    /// </summary>
    private bool IsAcked(CombatState a, CombatState b)
    {
        if (_asset != null) return _asset.IsAcked(a, b);
        return _known.Contains(KeyOf(a, b)) || _known.Contains(KeyOf(b, a));
    }

    private void SetAck(CombatState a, CombatState b, bool on)
    {
        if (_asset != null)
        {
            if (_asset.AckedAsymmetries == null)
                _asset.AckedAsymmetries = new List<CombatTransitionTable.AsymmetryAck>();

            for (int i = _asset.AckedAsymmetries.Count - 1; i >= 0; i--)
            {
                CombatTransitionTable.AsymmetryAck ack = _asset.AckedAsymmetries[i];
                if (ack == null) { _asset.AckedAsymmetries.RemoveAt(i); continue; }
                if ((ack.From == a && ack.To == b) || (ack.From == b && ack.To == a))
                    _asset.AckedAsymmetries.RemoveAt(i);
            }
            if (on)
            {
                _asset.AckedAsymmetries.Add(new CombatTransitionTable.AsymmetryAck
                {
                    From = a, To = b,
                    Reason = "（窗口标记为有意，未填写理由）",
                });
            }
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            return;
        }

        string key = KeyOf(a, b);
        if (on) _known.Add(key); else _known.Remove(key);
        SaveKnown();
    }

    private void LoadKnown()
    {
        _known.Clear();
        string raw = EditorPrefs.GetString(PREF_EXCEPTIONS, "");
        if (string.IsNullOrEmpty(raw)) return;
        string[] parts = raw.Split('|');
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0) _known.Add(parts[i]);
    }

    private void SaveKnown()
    {
        List<string> list = new List<string>(_known);
        EditorPrefs.SetString(PREF_EXCEPTIONS, string.Join("|", list.ToArray()));
    }

    private void SetMsg(string text, MessageType type)
    {
        _msg = text;
        _msgType = type;
    }

    // ───────────────────────── 绘制 ─────────────────────────

    private void OnGUI()
    {
        EnsureStyles();
        DrawAssetBar();
        DrawToolbar();

        if (!string.IsNullOrEmpty(_msg))
            EditorGUILayout.HelpBox(_msg, _msgType);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        DrawMatrix();
        DrawLegend();
        EditorGUILayout.Space();
        DrawLayerEditor();
        EditorGUILayout.Space();
        DrawChecks();
        EditorGUILayout.Space();
        DrawOneWayList();
        EditorGUILayout.EndScrollView();
    }

    private void EnsureStyles()
    {
        if (_headStyle != null) return;
        _headStyle = new GUIStyle(GUI.skin.box);
        _headStyle.alignment = TextAnchor.MiddleCenter;
        _headStyle.fontSize = 10;
        _headStyle.fontStyle = FontStyle.Bold;

        _rowStyle = new GUIStyle(GUI.skin.box);
        _rowStyle.alignment = TextAnchor.MiddleRight;
        _rowStyle.fontSize = 10;

        _cellStyle = new GUIStyle(GUI.skin.button);
        _cellStyle.alignment = TextAnchor.MiddleCenter;
        _cellStyle.fontSize = 11;
    }

    /// <summary>资产区：绑定 / 新建 / 保存 / 来源提示。</summary>
    private void DrawAssetBar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("转换表资产", EditorStyles.boldLabel, GUILayout.Width(80f));
        CombatTransitionTable picked = (CombatTransitionTable)EditorGUILayout.ObjectField(
            _asset, typeof(CombatTransitionTable), false);
        if (picked != _asset)
        {
            _asset = picked;
            if (_asset != null)
            {
                EditorPrefs.SetString(PREF_ASSET, AssetDatabase.GetAssetPath(_asset));
                SyncFromAsset();
            }
            else
            {
                SetMsg("已解除资产绑定（窗口内仍是内存副本，可继续推演）", MessageType.Info);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("新建资产", GUILayout.Width(80f))) CreateAsset();

        GUI.enabled = _asset != null;
        if (GUILayout.Button("保存到资产", GUILayout.Width(90f))) SaveToAsset();
        if (GUILayout.Button("丢弃改动重载", GUILayout.Width(100f))) SyncFromAsset();
        GUI.enabled = true;

        if (GUILayout.Button("重置为代码基线", GUILayout.Width(110f)))
        {
            ImportFromCode(false);
            _dirty = true;
        }
        EditorGUILayout.EndHorizontal();

        // 来源与运行时生效状态
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("当前编辑来源", GUILayout.Width(80f));
        EditorGUILayout.LabelField(_sourceLabel, EditorStyles.boldLabel, GUILayout.Width(160f));

        bool hasAsset = CombatTransitionTable.HasAsset();
        Color old = GUI.color;
        GUI.color = hasAsset ? new Color(0.45f, 0.85f, 0.55f) : new Color(0.95f, 0.72f, 0.35f);
        EditorGUILayout.LabelField(hasAsset ? "● 打包后：资产生效"
            : "● 打包后：无资产（Editor 暂用内置表）", EditorStyles.boldLabel);
        GUI.color = old;
        EditorGUILayout.EndHorizontal();

        if (!hasAsset)
        {
            EditorGUILayout.HelpBox(
                "没有可用资产 ⇒ **打包后**状态机将无任何合法转换（所有单位卡死在当前状态）。"
                + "当前 Editor 下会回落到内置默认表（可继续编辑，不代表打包后正常）。"
                + "立即点「新建资产」→「保存到资产」（默认路径 "
                + CombatTransitionTable.ASSET_PATH + "）。", MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        bool newEdit = GUILayout.Toggle(_editMode, "编辑模式", EditorStyles.toolbarButton, GUILayout.Width(70f));
        if (newEdit != _editMode)
        {
            _editMode = newEdit;
            EditorPrefs.SetBool(PREF_EDITMODE, _editMode);
        }

        GUILayout.Space(8f);

        if (GUILayout.Button("复制 Markdown", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            GenerateMarkdown();

        GUILayout.FlexibleSpace();

        if (_dirty)
        {
            Color old = GUI.color;
            GUI.color = new Color(1f, 0.72f, 0.35f);
            GUILayout.Label("● 已编辑（未保存到资产）", EditorStyles.toolbarButton);
            GUI.color = old;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawMatrix()
    {
        int n = _all.Length;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Box("from \\ to", _headStyle, GUILayout.Width(ROW_HEAD_W), GUILayout.Height(COL_HEAD_H));
        for (int j = 0; j < n; j++)
        {
            GUI.backgroundColor = _rowLayer[j] >= 0 && _rowLayer[j] < s_LayerColors.Length
                ? s_LayerColors[_rowLayer[j]] : Color.gray;
            GUILayout.Box(_shortName[j], _headStyle, GUILayout.Width(CELL_W), GUILayout.Height(COL_HEAD_H));
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < n; i++)
        {
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = _rowLayer[i] >= 0 && _rowLayer[i] < s_LayerColors.Length
                ? s_LayerColors[_rowLayer[i]] : Color.gray;
            GUILayout.Box(_all[i].ToString(), _rowStyle, GUILayout.Width(ROW_HEAD_W), GUILayout.Height(CELL_H));
            GUI.backgroundColor = Color.white;

            for (int j = 0; j < n; j++)
            {
                bool self = i == j;
                bool on = _matrix[i, j];
                bool suspect = !self && IsSuspect(i, j);

                string text;
                Color bg;
                if (self)
                {
                    text = "—";
                    bg = new Color(0.62f, 0.62f, 0.62f);
                }
                else if (on)
                {
                    text = "●";
                    bg = suspect ? new Color(0.98f, 0.70f, 0.32f) : new Color(0.55f, 0.85f, 0.66f);
                }
                else
                {
                    text = "";
                    bg = Color.white;
                }

                if (_edited[i, j]) text = text + "*";

                GUI.backgroundColor = bg;
                if (GUILayout.Button(text, _cellStyle, GUILayout.Width(CELL_W), GUILayout.Height(CELL_H)))
                    OnCellClicked(i, j);
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// 判定这条边「说不清」：同层之内 A→B 与 B→A 不一致，且未被登记为已知例外。
    /// 跨层单向（行动层 → 受控层）是正常打断，不算可疑。
    /// </summary>
    private bool IsSuspect(int i, int j)
    {
        if (i == j) return false;
        if (_rowLayer[i] != _rowLayer[j]) return false;
        if (_matrix[i, j] == _matrix[j, i]) return false;
        return !IsAcked(_all[i], _all[j]);
    }

    private void OnCellClicked(int i, int j)
    {
        if (i == j) return;
        if (!_editMode)
        {
            bool back = _matrix[j, i];
            SetMsg(_all[i] + " → " + _all[j] + "：" + (_matrix[i, j] ? "允许" : "禁止")
                   + "　｜　反向 " + _all[j] + " → " + _all[i] + "：" + (back ? "允许" : "禁止")
                   + "　（开启编辑模式可在此修改）", MessageType.Info);
            return;
        }
        _matrix[i, j] = !_matrix[i, j];
        _edited[i, j] = true;
        _dirty = true;
        Repaint();
    }

    private void DrawLegend()
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("图例：", EditorStyles.miniLabel, GUILayout.Width(40f));
        Swatch(new Color(0.55f, 0.85f, 0.66f), "● 允许");
        Swatch(Color.white, "空 禁止");
        Swatch(new Color(0.98f, 0.70f, 0.32f), "● 同层不对称（需确认）");
        Swatch(new Color(0.62f, 0.62f, 0.62f), "— 自身");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("分层：", EditorStyles.miniLabel, GUILayout.Width(40f));
        for (int l = 0; l < s_LayerNames.Length; l++)
            Swatch(s_LayerColors[l], s_LayerNames[l]);
        EditorGUILayout.EndHorizontal();
    }

    private void Swatch(Color c, string label)
    {
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = c;
        GUILayout.Box("", _cellStyle, GUILayout.Width(16f), GUILayout.Height(14f));
        GUI.backgroundColor = old;
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(150f));
    }

    /// <summary>分层编辑：新增状态时最容易漏登记的地方，单独列出来。</summary>
    private void DrawLayerEditor()
    {
        _showLayers = EditorGUILayout.Foldout(_showLayers, "状态分层（影响同层对称性判定）", true);
        if (!_showLayers) return;

        EditorGUI.indentLevel++;
        for (int i = 0; i < _all.Length; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_all[i].ToString(), GUILayout.Width(100f));
            CombatStateLayer now = (CombatStateLayer)EditorGUILayout.EnumPopup(
                (CombatStateLayer)_rowLayer[i], GUILayout.Width(110f));
            if ((int)now != _rowLayer[i])
            {
                _rowLayer[i] = (int)now;
                _dirty = true;
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// 四类客观检查 —— 直接复用 CombatTransitionTable.ValidateRows（与运行时同一套规则）。
    /// 前三类是「真 bug 级」，第四类是「需确认」。
    /// </summary>
    private void DrawChecks()
    {
        _showChecks = EditorGUILayout.Foldout(_showChecks, "自动检查（与运行时加载共用同一套规则）", true);
        if (!_showChecks) return;

        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        List<CombatTransitionTable.StateRow> rows = BuildRowsSnapshot();
        string name = _asset != null ? _asset.name : "（未保存的内存副本）";
        CombatTransitionTable.ValidateRows(rows, CombatTransitionTable.CURRENT_VERSION, name, errors, warnings);

        for (int i = 0; i < errors.Count; i++)
            EditorGUILayout.HelpBox(errors[i], MessageType.Error);
        for (int i = 0; i < warnings.Count; i++)
            EditorGUILayout.HelpBox(warnings[i], MessageType.Warning);

        if (errors.Count == 0 && warnings.Count == 0)
        {
            EditorGUILayout.HelpBox("检查全部通过（" + CountEdges() + " 条边 / " + _all.Length
                + " 个状态）。", MessageType.Info);
        }
    }

    /// <summary>单向边清单：跨层属正常打断，同层需逐条确认并可登记为「有意」。</summary>
    private void DrawOneWayList()
    {
        int n = _all.Length;
        List<string> cross = new List<string>();
        List<int[]> same = new List<int[]>();

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (_matrix[i, j] == _matrix[j, i]) continue;
                int from = _matrix[i, j] ? i : j;
                int to = _matrix[i, j] ? j : i;
                if (_rowLayer[i] == _rowLayer[j])
                    same.Add(new int[] { from, to });
                else
                    cross.Add(_all[from] + " → " + _all[to]);
            }
        }

        _showOneWay = EditorGUILayout.Foldout(_showOneWay,
            "单向边（" + (same.Count + cross.Count) + " 条，其中同层 " + same.Count + " 条）", true);
        if (!_showOneWay) return;

        EditorGUI.indentLevel++;

        if (cross.Count > 0)
        {
            EditorGUILayout.LabelField("跨层单向 · 属正常打断，无需处理", EditorStyles.miniLabel);
            for (int k = 0; k < cross.Count; k++)
                EditorGUILayout.LabelField("　" + cross[k], EditorStyles.miniLabel);
        }

        if (same.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("同层单向 · 需确认是有意还是漏写", EditorStyles.boldLabel);
            for (int k = 0; k < same.Count; k++)
            {
                int i = same[k][0];
                int j = same[k][1];
                EditorGUILayout.BeginHorizontal();

                Color old = GUI.color;
                bool acked = IsAcked(_all[i], _all[j]);
                GUI.color = acked ? Color.gray : new Color(0.95f, 0.62f, 0.25f);
                EditorGUILayout.LabelField(_all[i] + " → " + _all[j] + "　（反向无）",
                    EditorStyles.label, GUILayout.Width(230f));
                GUI.color = old;

                bool now = EditorGUILayout.Toggle("标记为有意", acked, GUILayout.Width(110f));
                if (now != acked)
                {
                    SetAck(_all[i], _all[j], now);
                    Repaint();
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.HelpBox(
                _asset != null
                    ? "标记写入资产字段 AckedAsymmetries（随资产一起保存，可补充理由文本）。"
                    : "未绑定资产，标记暂存在 EditorPrefs（键 " + PREF_EXCEPTIONS + "）。",
                MessageType.None);
        }
        else
        {
            EditorGUILayout.LabelField("同层之内全部对称，没有说不清的边。", EditorStyles.miniLabel);
        }

        EditorGUI.indentLevel--;
    }

    // ───────────────────────── 导出 ─────────────────────────

    private void GenerateMarkdown()
    {
        StringBuilder sb = new StringBuilder();
        int n = _all.Length;
        sb.Append("| from \\ to |");
        for (int j = 0; j < n; j++) sb.Append(" " + _all[j] + " |");
        sb.AppendLine();
        sb.Append("|---|");
        for (int j = 0; j < n; j++) sb.Append("---|");
        sb.AppendLine();
        for (int i = 0; i < n; i++)
        {
            sb.Append("| **" + _all[i] + "** |");
            for (int j = 0; j < n; j++)
            {
                if (i == j) sb.Append(" — |");
                else sb.Append(_matrix[i, j] ? " ✓ |" : " ✗ |");
            }
            sb.AppendLine();
        }
        EditorGUIUtility.systemCopyBuffer = sb.ToString();
        SetMsg("Markdown 矩阵已复制到剪贴板。", MessageType.Info);
    }
}
