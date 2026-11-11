#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UnitAnimationClipSet 的 Inspector —— 提供「一键采集」按钮、结果清单与校验。
///
/// 【为什么 entries 不交给 DrawDefaultInspector】
///   一条记录 6 个字段，二十几条就是一百多个控件，默认绘制会把面板撑成一堵墙，
///   而且里面大半是只读的辅助信息（来源文件、时长、是否循环）。这里改成只读清单：
///   一眼能看出「采到了什么、缺了什么、哪条名字对不上」，而不是一堆可随手改错的输入框。
/// </summary>
[CustomEditor(typeof(UnitAnimationClipSet))]
public class UnitAnimationClipSetEditor : Editor
{
    private UnitAnimationClipSet m_target;
    private bool m_showList = true;
    private string m_status = string.Empty;
    private MessageType m_statusType = MessageType.None;

    private void OnEnable()
    {
        m_target = target as UnitAnimationClipSet;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("角色动画片段集", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "挂在角色模型根节点上即可 —— 采集时会自动从模型的 Mesh（其次 Avatar）反查它来自哪个 FBX，" +
            "进而定位到该角色自己的动画目录，不需要手填路径。\n" +
            "仅在自动推导失败时，才用下面的「动画目录」手工指定。",
            MessageType.Info);

        // ---- 目录（可选的手工覆盖） ----
        SerializedProperty folderProp = serializedObject.FindProperty("ModelFolderPath");
        EditorGUILayout.PropertyField(folderProp, new GUIContent("动画目录（留空=自动推导）"));

        EditorGUILayout.BeginHorizontal();
        DefaultAsset current = string.IsNullOrEmpty(folderProp.stringValue)
            ? null
            : AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderProp.stringValue);
        DefaultAsset picked = (DefaultAsset)EditorGUILayout.ObjectField(
            "选择文件夹", current, typeof(DefaultAsset), false);

        if (picked != current)
        {
            string p = picked == null ? string.Empty : AssetDatabase.GetAssetPath(picked);
            if (string.IsNullOrEmpty(p) || AssetDatabase.IsValidFolder(p))
            {
                folderProp.stringValue = p;
            }
            else
            {
                Debug.LogWarning("[UnitAnimationClipSetEditor] 选择的不是文件夹: " + p);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // ---- 一键采集 ----
        GUI.backgroundColor = new Color(0.55f, 0.85f, 0.6f);
        if (GUILayout.Button("一键采集动画片段", GUILayout.Height(32)))
        {
            DoCollect();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("校验", GUILayout.Height(22)))
        {
            DoValidate();
        }
        if (GUILayout.Button("清空结果", GUILayout.Height(22)))
        {
            Undo.RecordObject(m_target, "Clear Animation Clips");
            m_target.Clear();
            EditorUtility.SetDirty(m_target);
            AssetDatabase.SaveAssets();
            SetStatus("已清空", MessageType.Info);
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(m_status))
        {
            EditorGUILayout.HelpBox(m_status, m_statusType);
        }

        EditorGUILayout.Space();
        DrawEntryList();

        serializedObject.ApplyModifiedProperties();
    }

    private void DoCollect()
    {
        if (m_target == null)
        {
            return;
        }

        // 目录：优先手工指定的，其次从模型自动推导
        string folder = m_target.ModelFolderPath;
        bool resolved = !string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder);

        if (!resolved)
        {
            folder = AnimationFolderScanner.ResolveModelFolder(m_target, m_target.ModelFolderPath);
        }

        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            SetStatus(
                "无法定位动画目录：模型的 Mesh / Avatar 都反查不到来源 FBX，且「动画目录」为空。\n" +
                "请在上面的选择框里指定该角色的动画文件夹。",
                MessageType.Error);
            return;
        }

        // 自动推导成功时把它回填，让用户看得见推导结果（下次也能直接改）
        if (folder != m_target.ModelFolderPath)
        {
            serializedObject.FindProperty("ModelFolderPath").stringValue = folder;
        }

        if (!AnimationFolderScanner.Apply(m_target, folder))
        {
            SetStatus("采集失败，详见 Console。", MessageType.Error);
            return;
        }

        Debug.Log(string.Format("[UnitAnimationClipSet] {0} 采集完成：目录 {1}，共 {2} 条",
            m_target.name, folder, m_target.Count));

        SetStatus(string.Format("采集完成：{0} 条（目录：{1}）", m_target.Count, folder), MessageType.Info);
        DoValidate();
    }

    private void DoValidate()
    {
        if (m_target == null)
        {
            return;
        }

        List<string> errors = new List<string>();
        List<string> warns = new List<string>();

        if (m_target.Count == 0)
        {
            SetStatus("还没有采集到任何片段。", MessageType.Warning);
            return;
        }

        var seen = new HashSet<string>();
        bool hasIdle = false;

        for (int i = 0; i < m_target.Entries.Length; i++)
        {
            AnimationClipEntry e = m_target.Entries[i];
            if (e == null)
            {
                errors.Add(string.Format("第 {0} 项为 null", i));
                continue;
            }
            if (e.clip == null)
            {
                errors.Add(string.Format("{0} 的 clip 丢失（来源 {1}）", e.actionName, e.sourceFile));
                continue;
            }
            // FBX 内嵌 clip 的 name 由 take 名决定，常与文件名不一致 —— 只提示，不报错
            if (e.clipName != e.sourceFile)
            {
                warns.Add(string.Format("{0}：clip.name({1}) 与文件名({2}) 不一致",
                    e.actionName, e.clipName, e.sourceFile));
            }
            if (!string.IsNullOrEmpty(e.actionName) && !seen.Add(e.actionName))
            {
                warns.Add("动作名重复: " + e.actionName);
            }
            if (string.Equals(e.actionName, AnimationFolderScanner.IdleAction,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                hasIdle = true;
            }
        }

        if (!hasIdle)
        {
            warns.Add("没有找到待机动作（@idle / @Idle / @IdleC）—— 若该角色确实有待机动画，请检查命名");
        }

        if (errors.Count > 0)
        {
            for (int i = 0; i < errors.Count; i++)
            {
                Debug.LogError(string.Format("[{0}] {1}", m_target.name, errors[i]));
            }
        }
        for (int i = 0; i < warns.Count; i++)
        {
            Debug.LogWarning(string.Format("[{0}] {1}", m_target.name, warns[i]));
        }

        string head = string.Format("校验完成：{0} 条，错误 {1}，提示 {2}",
            m_target.Count, errors.Count, warns.Count);
        string tail = string.Empty;
        if (errors.Count > 0)
        {
            tail += "\n" + string.Join("\n", errors.ToArray());
        }
        if (warns.Count > 0)
        {
            tail += "\n" + string.Join("\n", warns.ToArray());
        }

        SetStatus(head + tail, errors.Count > 0 ? MessageType.Error
            : warns.Count > 0 ? MessageType.Warning : MessageType.Info);
    }

    private void DrawEntryList()
    {
        if (m_target == null || m_target.Entries == null || m_target.Entries.Length == 0)
        {
            return;
        }

        m_showList = EditorGUILayout.Foldout(m_showList,
            string.Format("已采集片段（{0}）", m_target.Entries.Length), true);
        if (!m_showList)
        {
            return;
        }

        EditorGUI.indentLevel++;
        for (int i = 0; i < m_target.Entries.Length; i++)
        {
            AnimationClipEntry e = m_target.Entries[i];
            if (e == null)
            {
                EditorGUILayout.LabelField(i + ".", "(null)");
                continue;
            }

            string label = string.Format("{0}  [{1}]  {2}s{3}",
                string.IsNullOrEmpty(e.actionName) ? "(无动作名)" : e.actionName,
                e.clip == null ? "丢失" : e.clipName,
                e.length.ToString("F2"),
                e.isLooping ? "  Loop" : string.Empty);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, e.clip == null ? EditorStyles.boldLabel : EditorStyles.label);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUI.indentLevel--;
    }

    private void SetStatus(string text, MessageType type)
    {
        m_status = text;
        m_statusType = type;
        Repaint();
    }
}
#endif
