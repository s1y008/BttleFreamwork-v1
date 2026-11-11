#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 采集器的菜单入口 —— 不想打开 Inspector 时，选中模型直接一键采集。
///
/// 【和 Inspector 按钮的关系】
///   两条入口走的是同一个 <c>AnimationFolderScanner</c>，行为完全一致，只是触发方式不同：
///   Inspector 按钮适合边调边看，菜单适合批量（多选一次采集多个角色）。
/// </summary>
public static class AnimationCollectorMenu
{
    private const string Root = "BattleFramework/动画/";

    /// <summary>对 Hierarchy 中选中的全部对象一键采集（支持多选）</summary>
    [MenuItem(Root + "采集选中角色的动画片段", false, 100)]
    private static void CollectForSelection()
    {
        GameObject[] gos = Selection.gameObjects;
        if (gos == null || gos.Length == 0)
        {
            Debug.LogError("[AnimationCollector] 请先在 Hierarchy 里选中至少一个角色模型");
            return;
        }

        int ok = 0;
        for (int i = 0; i < gos.Length; i++)
        {
            if (CollectOne(gos[i]))
            {
                ok++;
            }
        }

        Debug.Log(string.Format("[AnimationCollector] 批量采集结束：选中 {0} 个，成功 {1} 个",
            gos.Length, ok));
    }

    [MenuItem(Root + "采集选中角色的动画片段", true)]
    private static bool CollectForSelectionValidate()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    /// <summary>只读打印选中角色的动画清单 —— 不写入组件，用于快速核对资源</summary>
    [MenuItem(Root + "打印选中角色的动画清单（不写入）", false, 101)]
    private static void PrintForSelection()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogError("[AnimationCollector] 请先选中一个角色模型");
            return;
        }

        string folder = AnimationFolderScanner.ResolveModelFolder(go.transform);
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError("[AnimationCollector] 无法定位 " + go.name + " 的动画目录");
            return;
        }

        List<AnimationClipEntry> list = AnimationFolderScanner.Collect(folder);
        Debug.Log(string.Format("[AnimationCollector] {0} 目录 {1} 共 {2} 条：",
            go.name, folder, list.Count));
        for (int i = 0; i < list.Count; i++)
        {
            AnimationClipEntry e = list[i];
            Debug.Log(string.Format("  {0,-12} clip={1,-32} loop={2,-5} {3:F2}s",
                e.actionName, e.clipName, e.isLooping, e.length));
        }
    }

    [MenuItem(Root + "打印选中角色的动画清单（不写入）", true)]
    private static bool PrintForSelectionValidate()
    {
        return Selection.activeGameObject != null;
    }

    private static bool CollectOne(GameObject go)
    {
        if (go == null)
        {
            return false;
        }

        UnitAnimationClipSet set = go.GetComponent<UnitAnimationClipSet>();
        if (set == null)
        {
            // 没有组件就自动挂一个 —— 采集工具的价值就在于「挂上就能用」
            set = Undo.AddComponent<UnitAnimationClipSet>(go);
            Debug.Log("[AnimationCollector] 已为 " + go.name + " 自动挂载 UnitAnimationClipSet");
        }

        string folder = set.ModelFolderPath;
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            folder = AnimationFolderScanner.ResolveModelFolder(set, set.ModelFolderPath);
        }

        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogError("[AnimationCollector] " + go.name + " 无法定位动画目录：模型 Mesh/Avatar 反查失败，" +
                           "请在 Inspector 上手工指定「动画目录」");
            return false;
        }

        set.ModelFolderPath = folder;
        return AnimationFolderScanner.Apply(set, folder);
    }
}
#endif
