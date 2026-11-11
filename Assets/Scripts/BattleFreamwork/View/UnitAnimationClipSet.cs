using System;
using UnityEngine;

/// <summary>
/// 一条采集到的动画片段记录 —— 纯数据，供 Inspector 展示与运行时按名查找。
///
/// 【为什么同时存 actionName 和 clipName】
///   两者是**两套键**，用途不同（沿用 ACT 项目的双键结构，见《ActionComponent 动画采集方案》6.9）：
///     · <c>actionName</c> = <c>@Die</c> —— 对外播放键，业务层说「播受击」时用它；
///     · <c>clipName</c>   = <c>clip.name</c> —— 资产自己的名字，播放引擎内部按它查找。
///   直接把 clipName 当动作名传进去是常见错误，两者必须分开存。
///
/// 【sourceFile 为什么也要存】
///   本工程的动画**全部内嵌在 FBX 里**（没有独立 .anim），clip.name 由 FBX 内部的 take 名决定，
///   可能与文件名不一致。存下来源文件名，才能在校验时把「名字对不上」这条问题指出来。
/// </summary>
[Serializable]
public class AnimationClipEntry
{
    public AnimationClip clip;

    /// <summary>对外动作名，形如 <c>@Die</c>（由文件名 <c>R_Monster_Gebuling@Die</c> 解析而来）</summary>
    public string actionName;

    /// <summary>资产名（<c>clip.name</c>）—— 播放引擎内部查找键</summary>
    public string clipName;

    /// <summary>来源文件名（去扩展名），如 <c>R_Monster_Gebuling@Die</c></summary>
    public string sourceFile;

    /// <summary>是否循环（取自 clip 上的 Loop Time 勾选）</summary>
    public bool isLooping;

    /// <summary>片段时长（秒）</summary>
    public float length;
}

/// <summary>
/// 角色动画片段集 —— 挂在角色模型根节点上，由编辑器工具「一键采集」把该模型目录下的动画片段全部装进来。
///
/// 【它解决什么问题】
///   本工程的动画以 <c>&lt;模型名&gt;@&lt;动作名&gt;.FBX</c> 的形式散在
///   <c>Assets/ArtResources/Model/&lt;角色&gt;/</c> 下，一个角色二十几个 FBX。要让代码播「受击」，
///   就得先在 Inspector 里把 <c>R_Monster_Gebuling@LightHit</c> 那条 clip 手工拖进来 —— 二十几个动作
///   拖二十几次，换皮/加动画时还要重来，漏拖一个就是运行时「打了没反应」。
///   采集器把这件事变成：**挂上组件 → 点一次按钮 → 全部到位**。
///
/// 【一键是怎么做到的 ——目录从哪来】
///   不需要手填任何路径。采集时按顺序自动推导（见 <c>AnimationFolderScanner.ResolveModelFolder</c>）：
///     ① 模型身上 <c>SkinnedMeshRenderer / MeshFilter</c> 的 Mesh 来自哪个 FBX → 那个 FBX 所在目录；
///     ② 退而求其次：<c>Animator.avatar</c> 所在目录；
///     ③ 都不行，才用本组件的 <c>ModelFolderPath</c> 兜底（可在 Inspector 里手动指定）。
///   所以「挂在角色模型身体上」这个动作本身就是配置 —— 组件跟着模型走，目录跟着组件走。
///
/// 【边界】
///   本组件只**持有数据**，不含任何采集逻辑（那部分在 Editor 目录下，打包时不进包）。
///   也不负责播放 —— 播放归 UnitView / Animator（或将来 Animancer）管，这里只是片段的**清单**。
/// </summary>
[AddComponentMenu("BattleFramework/Unit Animation Clip Set")]
public class UnitAnimationClipSet : MonoBehaviour
{
    /// <summary>
    /// 动画所在目录（形如 <c>Assets/ArtResources/Model/Enemy/哥布林NEW（小）</c>）。
    /// 留空 = 采集时从模型自身的 Mesh / Avatar 自动推导；推导失败或需要跨目录取动画时才手填。
    /// </summary>
    [Tooltip("留空 = 采集时从模型自动推导。需要指定时用编辑器面板上的选择框或直接填 Assets/ 开头的路径。")]
    public string ModelFolderPath = string.Empty;

    /// <summary>采集结果。由 <c>Assets/Editor/AnimationCollector/</c> 下的工具写入，运行时只读。</summary>
    public AnimationClipEntry[] Entries = new AnimationClipEntry[0];

    /// <summary>已采集条目数（空引用安全）</summary>
    public int Count
    {
        get { return Entries != null ? Entries.Length : 0; }
    }

    /// <summary>
    /// 按动作名取片段（大小写不敏感），如 <c>GetClip("@Die")</c>。找不到返回 null。
    /// </summary>
    public AnimationClip GetClip(string actionName)
    {
        AnimationClip clip;
        return TryGetClip(actionName, out clip) ? clip : null;
    }

    /// <summary>
    /// 按动作名取片段（大小写不敏感）。
    /// 为什么忽略大小写：资源里既有 <c>@idle</c> 也有 <c>@Idle</c>（哥布林用小写、野猪用 IdleC），
    /// 大小写不统一是既有事实，让调用方去记大小写只会制造 bug。
    /// </summary>
    public bool TryGetClip(string actionName, out AnimationClip clip)
    {
        clip = null;

        if (string.IsNullOrEmpty(actionName) || Entries == null)
        {
            return false;
        }

        for (int i = 0; i < Entries.Length; i++)
        {
            AnimationClipEntry e = Entries[i];
            if (e == null || e.clip == null)
            {
                continue;
            }
            if (string.Equals(e.actionName, actionName, StringComparison.OrdinalIgnoreCase))
            {
                clip = e.clip;
                return true;
            }
        }
        return false;
    }

    /// <summary>清空采集结果（编辑器工具调用）</summary>
    public void Clear()
    {
        Entries = new AnimationClipEntry[0];
    }
}
