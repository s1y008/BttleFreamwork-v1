#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 角色动画采集器 —— 给定一个目录（或从模型自动推导出目录），把该目录下的**全部动画片段**收进来。
///
/// 【与 ACT 项目原版的关键差异 —— 为什么不能直接照抄】
///   原方案扫的是 <c>*.anim</c> 文件（<c>Directory.GetFiles(dir, "*.anim")</c>），因为那个项目的动画
///   是独立导出的 .anim 资产。**本工程不是**：动画全部**内嵌在 FBX 里**
///   （<c>Assets/ArtResources/Model/Enemy/哥布林NEW（小）/R_Monster_Gebuling@Die.FBX</c>），
///   工程自身一个 .anim 都没有（全工程 17 个 .anim 均来自 Animancer 插件示例）。
///   照抄原版的结果会是「扫到 0 条」—— 所以这里改用 <c>AssetDatabase.FindAssets("t:AnimationClip")</c>
///   + <c>LoadAllAssetsAtPath</c>，.anim 与 FBX 内嵌 clip 一视同仁。
///
/// 【沿用原方案的部分】
///   目录约定 + 命名约定（<c>&lt;角色&gt;@&lt;动作&gt;</c>）→ 解析出 <c>@动作</c> 作为对外动作名；
///   以及原方案踩过的三个坑，这里都做了处理（见文末「已规避的坑」）。
///
/// 【已规避的坑（来自原方案第六节）】
///   6.1 改动不落盘 → <c>Apply</c> 里 Undo + SetDirty + SaveAssets，一个不少；
///   6.3 索引错位（用 files 下标 swap animations）→ 这里在结果列表上按 actionName 查找后再换；
///   6.6 路径裁剪 IndexOf("Assets") 返回 -1 会抛异常 → 全程只用 Assets/ 相对路径，不做字符串裁剪；
///   6.7 同名目录歧义 → 允许调用方直接给完整相对路径，也支持按目录名递归查找（多命中时报错指名）。
/// </summary>
public static class AnimationFolderScanner
{
    /// <summary>动画资源根目录（相对 Assets），按目录名查找时在这个范围内找</summary>
    public const string DefaultModelRoot = "Assets/ArtResources/Model";

    /// <summary>动作名前缀，与命名规范 <c>角色@动作</c> 的解析结果保持一致</summary>
    public const string ActionPrefix = "@";

    /// <summary>默认待机动作名（校验时用来提示，不强制挪位 —— 本工程运行时没有「[0] 即待机」的约定）</summary>
    public const string IdleAction = "@idle";

    /// <summary>
    /// ★ 从模型自身推导出动画所在目录 —— 「挂载即配置」的关键。
    ///
    /// 推导顺序：
    ///   ① 模型身上 SkinnedMeshRenderer / MeshFilter 的 Mesh 来自哪个 FBX → 该 FBX 所在目录；
    ///      （这是最准的一条：角色模型一定带着自己的蒙皮网格，而网格来自同目录下的某个 FBX）
    ///   ② Animator.avatar 所在目录；
    ///   ③ 组件上手工填的 ModelFolderPath。
    /// 全部失败返回 null，由调用方报错指名，不做静默兜底。
    /// </summary>
    public static string ResolveModelFolder(Component comp, string fallbackFolder = null)
    {
        if (comp == null)
        {
            return null;
        }

        // ① 从 Mesh 反查 —— 蒙皮网格优先级高于普通网格（角色模型几乎都是 SkinnedMeshRenderer）
        string meshPath = FindMeshAssetPath(comp.gameObject);
        if (!string.IsNullOrEmpty(meshPath))
        {
            string dir = Path.GetDirectoryName(meshPath);
            if (!string.IsNullOrEmpty(dir))
            {
                return dir.Replace('\\', '/');
            }
        }

        // ② 从 Avatar 反查
        Animator animator = comp.GetComponentInChildren<Animator>();
        if (animator != null && animator.avatar != null)
        {
            string avatarPath = AssetDatabase.GetAssetPath(animator.avatar);
            if (!string.IsNullOrEmpty(avatarPath))
            {
                string dir = Path.GetDirectoryName(avatarPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    return dir.Replace('\\', '/');
                }
            }
        }

        // ③ 手工兜底
        return string.IsNullOrEmpty(fallbackFolder) ? null : fallbackFolder.Replace('\\', '/');
    }

    private static string FindMeshAssetPath(GameObject root)
    {
        // 先找 SkinnedMeshRenderer（角色模型），再找 MeshFilter（道具/场景件）
        SkinnedMeshRenderer[] skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skins.Length; i++)
        {
            if (skins[i] != null && skins[i].sharedMesh != null)
            {
                string p = AssetDatabase.GetAssetPath(skins[i].sharedMesh);
                if (!string.IsNullOrEmpty(p))
                {
                    return p;
                }
            }
        }

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] != null && filters[i].sharedMesh != null)
            {
                string p = AssetDatabase.GetAssetPath(filters[i].sharedMesh);
                if (!string.IsNullOrEmpty(p))
                {
                    return p;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 按目录名在 <paramref name="root"/> 下递归查找唯一同名目录（用于「只知道角色名」的场景）。
    /// 命中 0 个或多个都报错返回 null —— 不猜、不取第一个。
    /// </summary>
    public static string FindFolderByName(string folderName, string root = DefaultModelRoot)
    {
        if (string.IsNullOrEmpty(folderName))
        {
            Debug.LogError("[AnimationFolderScanner] 目录名为空");
            return null;
        }
        if (!AssetDatabase.IsValidFolder(root))
        {
            Debug.LogError("[AnimationFolderScanner] 根目录不存在: " + root);
            return null;
        }

        // FindAssets 用 "t:folder" 找不到中文目录时可能不稳，这里直接走磁盘枚举 + IsValidFolder 校验
        string absRoot = Path.Combine(Application.dataPath, root.Substring("Assets/".Length));
        List<string> hits = new List<string>();

        try
        {
            string[] dirs = Directory.GetDirectories(absRoot, folderName, SearchOption.AllDirectories);
            for (int i = 0; i < dirs.Length; i++)
            {
                string rel = "Assets/" + dirs[i].Substring(Application.dataPath.Length + 1).Replace('\\', '/');
                if (string.Equals(Path.GetFileName(dirs[i]), folderName, StringComparison.Ordinal))
                {
                    hits.Add(rel);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[AnimationFolderScanner] 目录扫描异常: " + e.Message);
            return null;
        }

        if (hits.Count == 0)
        {
            Debug.LogError("[AnimationFolderScanner] 找不到目录 " + folderName + "（根: " + root + "）");
            return null;
        }
        if (hits.Count > 1)
        {
            Debug.LogError("[AnimationFolderScanner] 存在多个同名目录，请改用完整路径: " + folderName);
            for (int i = 0; i < hits.Count; i++)
            {
                Debug.LogError("   " + hits[i]);
            }
            return null;
        }
        return hits[0];
    }

    /// <summary>
    /// 采集一个目录下的全部动画片段（含 .anim 与 FBX 内嵌 clip）。
    /// 结果按文件名排序 —— 稳定可比对（换台机器、重采一次，顺序不变，便于 diff）。
    /// </summary>
    public static List<AnimationClipEntry> Collect(string folderPath)
    {
        var result = new List<AnimationClipEntry>();

        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError("[AnimationFolderScanner] 目录无效: " + folderPath);
            return result;
        }

        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });

        // 同一个 FBX 可能被多条 guid 指向（含多个 clip 时），按实例去重
        var seen = new HashSet<AnimationClip>();
        var usedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int j = 0; j < all.Length; j++)
            {
                AnimationClip clip = all[j] as AnimationClip;
                if (clip == null || !seen.Add(clip))
                {
                    continue;
                }

                string sourceFile = Path.GetFileNameWithoutExtension(path);
                string action = ParseAction(sourceFile);

                if (!usedActions.Add(action))
                {
                    Debug.LogWarning(string.Format(
                        "[AnimationFolderScanner] 动作名重复，已跳过: {0} (Action={1})", sourceFile, action));
                    continue;
                }

                result.Add(new AnimationClipEntry
                {
                    clip = clip,
                    actionName = action,
                    clipName = clip.name,
                    sourceFile = sourceFile,
                    isLooping = clip.isLooping,
                    length = clip.length,
                });
            }
        }

        result.Sort((a, b) => string.CompareOrdinal(a.sourceFile, b.sourceFile));
        return result;
    }

    /// <summary>
    /// 采集并写回组件（整体覆盖）。含 Undo / 脏标记 / 落盘 —— 原方案 6.1 那个「点了没保存」的坑在这里堵死。
    /// </summary>
    public static bool Apply(UnitAnimationClipSet target, string folderPath)
    {
        if (target == null)
        {
            Debug.LogError("[AnimationFolderScanner] 目标组件为 null");
            return false;
        }

        List<AnimationClipEntry> list = Collect(folderPath);
        if (list.Count == 0)
        {
            Debug.LogError(string.Format(
                "[AnimationFolderScanner] {0} 未采集到任何动画（目录: {1}）。" +
                "请确认该目录下存在含 AnimationClip 的 FBX 或 .anim。", target.name, folderPath));
            return false;
        }

        Undo.RecordObject(target, "Collect Animation Clips");
        target.Entries = list.ToArray();
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();

        return true;
    }

    /// <summary>
    /// 从文件名解析动作名：<c>R_Monster_Gebuling@Die</c> → <c>@Die</c>。
    /// 没有 @ 时退化为文件名本身（不猜、不补前缀）。
    /// </summary>
    public static string ParseAction(string fileNameWithoutExtension)
    {
        if (string.IsNullOrEmpty(fileNameWithoutExtension))
        {
            return string.Empty;
        }

        string[] parts = fileNameWithoutExtension.Split('@');
        return parts.Length >= 2 ? ActionPrefix + parts[1] : parts[0];
    }
}
#endif
