using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BF.Diagnose
{
    /// <summary>
    /// ★ 运行时「画面快照」诊断工具（2026-09-19 新增）
    ///
    /// 存在理由：AI 排查只能读文本，**看不到游戏画面**，过去因此把「画面上有两个特效」
    /// 误翻译成「时序上同一帧播了两个」，白跑一整天。本工具把画面翻译成文本 ——
    /// 即「用户自己一眼看穿问题所用的方法」：看场景里现在有哪些特效对象、挂在谁身上。
    ///
    /// 两个菜单（路径刻意用 ASCII：中文菜单在 PowerShell→Python→HTTP→Unity 链路会被转码）：
    ///   Tools/Effect/Dump Effect Objects   —— 导出当前场景所有特效实例的名称 / 层级路径 /
    ///                                         挂点 / 是否可见 / 世界坐标 / 粒子播放状态
    ///   Tools/Effect/Capture Screenshot    —— 截一张 Game 视图图，存到工程根 _diagnose/
    ///
    /// 输出到 **工程根 _diagnose/**（不写 Assets，避免触发资源导入与 guid 污染）。
    /// ⚠ 纯诊断用途，不参与任何游戏逻辑；不需要可直接删除本文件（走 asset_delete）。
    /// </summary>
    public static class EffectSnapshotTool
    {
        private const string MENU_ROOT = "Tools/Effect/";

        /// <summary>工程根下的诊断输出目录（Assets 的上一级，不参与 Unity 资源导入）</summary>
        private static string DiagnoseDir
        {
            get
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "_diagnose"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 1) 快照：把「画面上现在有什么特效」导出成文本
        // ══════════════════════════════════════════════════════════════════════
        [MenuItem(MENU_ROOT + "Dump Effect Objects")]
        public static void DumpEffectObjects()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Effect Snapshot ===");
            sb.Append("time=").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("playing=").AppendLine(EditorApplication.isPlaying ? "1" : "0");
            sb.AppendLine();

            // Resources.FindObjectsOfTypeAll 会带上 Project 里的 prefab 资源，
            // 用 scene.IsValid() 只保留真实存在于场景中的实例。
            ParticleSystem[] all = Resources.FindObjectsOfTypeAll<ParticleSystem>();
            Dictionary<Transform, List<ParticleSystem>> grouped =
                new Dictionary<Transform, List<ParticleSystem>>();

            foreach (ParticleSystem ps in all)
            {
                if (ps == null) continue;
                if (!ps.gameObject.scene.IsValid()) continue;

                Transform root = FindEffectRoot(ps.transform);
                if (!grouped.ContainsKey(root)) grouped[root] = new List<ParticleSystem>();
                grouped[root].Add(ps);
            }

            sb.Append("场景中的特效实例数 = ").AppendLine(grouped.Count.ToString());
            sb.AppendLine();

            int idx = 0;
            foreach (KeyValuePair<Transform, List<ParticleSystem>> kv in grouped)
            {
                Transform root = kv.Key;
                idx++;

                sb.Append("--- #").Append(idx).AppendLine(" ---");
                sb.Append("  name     = ").AppendLine(root.name);
                sb.Append("  path     = ").AppendLine(GetPath(root));
                sb.Append("  parent   = ").AppendLine(root.parent != null ? root.parent.name : "<null>");
                sb.Append("  activeSelf       = ").AppendLine(root.gameObject.activeSelf ? "1" : "0");
                sb.Append("  activeInHierarchy= ").AppendLine(root.gameObject.activeInHierarchy ? "1" : "0");
                sb.Append("  worldPos = ").AppendLine(root.position.ToString("F2"));

                List<ParticleSystem> list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    ParticleSystem ps = list[i];
                    ParticleSystem.MainModule main = ps.main;
                    sb.Append("  ps[").Append(i).Append("] ").Append(ps.name)
                      .Append(" playing=").Append(ps.isPlaying ? "1" : "0")
                      .Append(" t=").Append(ps.time.ToString("F2"))
                      .Append("/").Append(main.duration.ToString("F2"))
                      .Append(" alive=").Append(ps.particleCount)
                      .Append(" looping=").Append(main.loop ? "1" : "0")
                      .AppendLine();
                }
                sb.AppendLine();
            }

            string path = Path.Combine(DiagnoseDir, "effects_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            Debug.Log($"[Snapshot] 特效快照已导出：{path}（实例数={grouped.Count}）");
            EditorUtility.RevealInFinder(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // 2) 截图：让「看不见画面」的一方也能看到画面
        // ══════════════════════════════════════════════════════════════════════
        [MenuItem(MENU_ROOT + "Capture Screenshot")]
        public static void CaptureScreenshot()
        {
            string path = Path.Combine(DiagnoseDir, "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            // ⚠ CaptureScreenshot 在下一帧才真正落盘，所以这里只能预告路径。
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[Snapshot] 截图请求已提交，稍后落盘：{path}");
        }

        // ══════════════════════════════════════════════════════════════════════
        // 内部辅助
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 从某个粒子系统往上找，直到「再上一层就不只属于本特效」为止，
        /// 得到这个特效实例的根节点（即 EffectManager 创建出来的那个 GameObject）。
        /// </summary>
        private static Transform FindEffectRoot(Transform t)
        {
            Transform root = t;
            while (root.parent != null && root.parent.GetComponentInChildren<ParticleSystem>(true) != null)
            {
                root = root.parent;
            }
            return root;
        }

        private static string GetPath(Transform t)
        {
            StringBuilder sb = new StringBuilder();
            while (t != null)
            {
                if (sb.Length > 0) sb.Insert(0, "/");
                sb.Insert(0, t.name);
                t = t.parent;
            }
            return sb.ToString();
        }
    }
}
