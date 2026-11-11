# -*- coding: utf-8 -*-
"""
CombatStateMachine.cs —— 支持从 ScriptableObject 资产加载转换表（2026-10-10）

改动：
  1. 顶部加 using UnityEngine;（Debug.Log 需要）
  2. 构造函数加可选参数 applyAssetTable（默认 true，运行时行为不变地启用资产）
  3. 新增 ApplyTransitionTableAsset()：资产存在且校验通过 → 整体覆盖；
     不存在 → 保留代码内置表并只报一次警告（指名路径，不静默降级）；
     校验失败 → LogError 逐条指名后保留内置表（fail-fast）。

纪律：old 命中数必须 == 1；写回后重读复验；幂等（new 已存在即跳过）。
"""
import io, os, sys, time

TARGET = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\StateMachine\CombatStateMachine.cs"

# ---------------- 1) using UnityEngine ----------------
OLD_USING = "using System;\nusing System.Collections.Generic;\n"
NEW_USING = "using System;\nusing System.Collections.Generic;\nusing UnityEngine;\n"

# ---------------- 2) 构造函数 ----------------
OLD_CTOR = """    public CombatStateMachine()
    {
        InitDefaultTransitions();
    }
"""

NEW_CTOR = """    /// <summary>
    /// 构造：装载转换表。
    ///
    /// <param name="applyAssetTable">
    /// 是否用转换表资产（Resources/Config/CombatTransitionTable）覆盖代码内置表，默认 true。
    /// ★ 传 false 的用途只有一个：编辑器工具要读取「纯代码内置表」时
    ///   （矩阵窗口的「从代码导入」），否则它会读到资产内容，形成「导入自己」的循环。
    /// </param>
    /// </summary>
    public CombatStateMachine(bool applyAssetTable = true)
    {
        InitDefaultTransitions();
        if (applyAssetTable)
            ApplyTransitionTableAsset();
    }

    /// <summary>
    /// 用转换表资产整体覆盖代码内置表（★ 2026-10-10）。
    ///
    /// 【为什么是「整体覆盖」而不是「增量合并」】
    ///   合并会让最终生效的规则取决于「代码配了什么 + 资产配了什么」，
    ///   出问题时要同时看两处才知道一条边为什么存在 —— 这正是硬编码时代最难排查的地方。
    ///   整体覆盖保证：配了资产，资产就是唯一真相；没配资产，代码表就是唯一真相。
    ///
    /// 【为什么失败时保留内置表而不是直接抛异常】
    ///   状态机在 Controller.Start() 里构造，抛异常会让整个单位初始化失败、连锁 NRE。
    ///   但「保留内置表」必须伴随逐条 LogError 指名 —— 静默兜底是工程铁律明令禁止的。
    /// </summary>
    private void ApplyTransitionTableAsset()
    {
        CombatTransitionTable table = CombatTransitionTable.Load();
        if (table == null)
        {
            CombatTransitionTable.LogMissingOnce();
            return;
        }

        if (!table.Enabled)
        {
            LogDisabledOnce(table.name);
            return;
        }

        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        if (!table.Validate(errors, warnings))
        {
            // fail-fast 指名：逐条报错，并明确告知「沿用代码内置表」，不静默降级
            StringBuilder sb = new StringBuilder();
            sb.Append("[TransitionTable] 资产「").Append(table.name).Append("」校验失败，")
              .Append("本状态机沿用 CombatStateMachine.InitDefaultTransitions 内置表。问题如下：");
            for (int i = 0; i < errors.Count; i++)
                sb.Append("\\n  · ").Append(errors[i]);
            Debug.LogError(sb.ToString());
            return;
        }

        for (int i = 0; i < warnings.Count; i++)
            Debug.LogWarning("[TransitionTable] " + table.name + "：" + warnings[i]);

        Dictionary<CombatState, HashSet<CombatState>> built =
            new Dictionary<CombatState, HashSet<CombatState>>();
        table.BuildTransitions(built);

        _transitions.Clear();
        foreach (KeyValuePair<CombatState, HashSet<CombatState>> kv in built)
            _transitions[kv.Key] = kv.Value;
    }

    private static bool s_TableDisabledLogged;

    private static void LogDisabledOnce(string assetName)
    {
        if (s_TableDisabledLogged) return;
        s_TableDisabledLogged = true;
        Debug.LogWarning("[TransitionTable] 资产「" + assetName
            + "」的 Enabled 为 false，运行时沿用代码内置转换表。");
    }
"""

# ---------------- 3) 字段区加 StringBuilder 需要的 using? 不需要，用全名 System.Text ----------------
# 用 StringBuilder 需要 using System.Text; —— 改为并入 using 块
OLD_USING_FINAL = "using System;\nusing System.Collections.Generic;\nusing UnityEngine;\n"
NEW_USING_FINAL = "using System;\nusing System.Collections.Generic;\nusing System.Text;\nusing UnityEngine;\n"


def main():
    raw = open(TARGET, "rb").read()
    text = raw.decode("utf-8")

    # 幂等判定
    if "ApplyTransitionTableAsset" in text:
        print("SKIP: 已存在 ApplyTransitionTableAsset，判定为已完成。")
        return 0

    steps = []
    if NEW_USING_FINAL in text:
        steps.append(("using(已完成)", None, None))
    elif OLD_USING in text:
        steps.append(("using", OLD_USING, NEW_USING_FINAL))
    else:
        print("FAIL: using 块未命中，文件结构可能已变。")
        return 1

    steps.append(("ctor", OLD_CTOR, NEW_CTOR))

    cur = text
    for name, old, new in steps:
        if old is None:
            continue
        cnt = cur.count(old)
        if cnt != 1:
            print("FAIL: %s 命中 %d 次（要求 == 1）" % (name, cnt))
            return 1
        cur = cur.replace(old, new, 1)
        print("OK  : %s 替换 1 处" % name)

    open(TARGET, "wb").write(cur.encode("utf-8"))
    print("WRITE OK")

    # 复验
    time.sleep(1)
    again = open(TARGET, "rb").read().decode("utf-8")
    ok = True
    for name, old, new in steps:
        if new is None:
            continue
        if new not in again:
            print("VERIFY FAIL: %s 未落地" % name)
            ok = False
    if "ApplyTransitionTableAsset" not in again:
        print("VERIFY FAIL: 方法缺失")
        ok = False
    if ok:
        print("VERIFY OK (第一次)")
    else:
        return 1

    time.sleep(3)
    third = open(TARGET, "rb").read().decode("utf-8")
    if "ApplyTransitionTableAsset" in third and "CombatTransitionTable.Load()" in third:
        print("VERIFY OK (第二次，延迟 3 秒)")
    else:
        print("VERIFY FAIL (第二次)：磁盘可能回退")
        return 1

    print("lines =", third.count("\n") + 1)
    return 0


if __name__ == "__main__":
    sys.exit(main())
