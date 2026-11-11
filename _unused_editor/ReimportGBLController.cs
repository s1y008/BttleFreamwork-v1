using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 用 AnimatorController API 补齐 GBL 的受击 / 死亡协议参数（★ 2026-09-18）
///
/// 【为什么要它】磁盘 YAML 里 GBL.controller 明明有 HitType（第 613 行），
///   但运行时 Animator.parameters 只有 [Die, Run, SkillId, Hit, Move] —— 少一个。
///   且 ImportAsset(ForceUpdate) 也拉不回来：编辑器内存里持有的是旧实例，
///   改 YAML 不等于改 Unity 认识的那个对象。
///   ⇒ 结论：**改 Unity 资源必须走 Editor API 改内存实例再保存，不能只改磁盘文本。**
///
/// 【顺带诊断】把 Hit 子状态机的 Entry 入边打出来 —— 若 YAML 里加的那条
///   `Entry → LightHit [HitType Equals 1]` 同样没进内存，受击动作就仍然播不出来。
///
/// 【幂等】参数已存在就跳过；重复执行只重新打印诊断。
/// </summary>
public class ReimportGBLController
{
    private const string PATH = "Assets/ArtResources/AnimatorController/Enemy/GBL.controller";

    [MenuItem("BattleFramework/Reimport GBL Controller (Force)")]
    public static void Run()
    {
        AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(PATH);
        if (ac == null)
        {
            Debug.LogError("[Reimport] 加载失败：" + PATH);
            return;
        }

        EnsureParam(ac, "Hit", AnimatorControllerParameterType.Trigger);
        EnsureParam(ac, "HitType", AnimatorControllerParameterType.Int);
        EnsureParam(ac, "Die", AnimatorControllerParameterType.Bool);

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < ac.parameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(ac.parameters[i].name);
        }
        Debug.Log("[Reimport] 保存后参数 = [" + sb + "]");

        // 诊断：子状态机的 Entry 入边
        ChildAnimatorStateMachine[] subs = ac.layers[0].stateMachine.stateMachines;
        Debug.Log("[Reimport] 子状态机数量 = " + subs.Length);
        for (int i = 0; i < subs.Length; i++)
        {
            AnimatorStateMachine sm = subs[i].stateMachine;
            Debug.Log("[Reimport] · 子状态机 " + sm.name + "（默认状态=" +
                      (sm.defaultState != null ? sm.defaultState.name : "无") + "）");

            AnimatorTransition[] entries = sm.entryTransitions;
            Debug.Log("[Reimport]   Entry 入边数 = " + (entries != null ? entries.Length : 0));
            if (entries == null) continue;

            for (int j = 0; j < entries.Length; j++)
            {
                AnimatorTransition t = entries[j];
                string dst = t.dstState != null
                    ? t.dstState.name
                    : (t.dstStateMachine != null ? t.dstStateMachine.name : "(未指向)");

                System.Text.StringBuilder cond = new System.Text.StringBuilder();
                for (int k = 0; k < t.conditions.Length; k++)
                {
                    if (k > 0) cond.Append(" & ");
                    cond.Append(t.conditions[k].parameter)
                        .Append(" ").Append(t.conditions[k].mode)
                        .Append(" ").Append(t.conditions[k].threshold);
                }
                Debug.Log("[Reimport]   Entry → " + dst + "  条件=[" + cond + "]");
            }
        }
    }

    private static void EnsureParam(AnimatorController ac, string name, AnimatorControllerParameterType type)
    {
        for (int i = 0; i < ac.parameters.Length; i++)
        {
            if (ac.parameters[i].name == name)
            {
                Debug.Log("[Reimport] 参数已存在，跳过：" + name);
                return;
            }
        }
        ac.AddParameter(name, type);
        Debug.Log("[Reimport] 已补加参数：" + name + " (" + type + ")");
    }
}
