# -*- coding: utf-8 -*-
"""
修正 B 档遗留：资产缺失的报错文案必须区分 Editor / 运行时。

原因（实测发现）：Editor 下 UNITY_EDITOR 为真，InitDefaultTransitions 仍然编译进来，
所以资产缺失时 Editor 里仍有内置表兜底（28 条），但打包后没有。
统一写成「将无任何合法转换」在 Editor 下是错误陈述 —— 必须按编译条件分开说。

改动：
  CombatTransitionTable.LogMissingOnce  → #if UNITY_EDITOR 两套文案
  CombatTransitionMatrixWindow 顶部 HelpBox → Editor 视角文案
  CombatTransitionMatrixWindow 来源指示灯 → 「打包后」视角文案
"""
import io, sys, time

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork"
TABLE = ROOT + r"\StateMachine\CombatTransitionTable.cs"
WIN = ROOT + r"\Editor\CombatTransitionMatrixWindow.cs"

STEPS = []

STEPS.append((TABLE, "missing-msg", """    public static void LogMissingOnce()
    {
        if (s_MissingLogged) return;
        s_MissingLogged = true;
        Debug.LogError("[TransitionTable] 未找到转换表资产 Resources/" + RESOURCE_PATH
            + "，运行时状态机将无任何合法转换（所有单位会卡死在当前状态）。"
            + "请在菜单 Tools → Battle Framework → Transition Matrix 中创建并保存转换表资产。");
    }
""", """    public static void LogMissingOnce()
    {
        if (s_MissingLogged) return;
        s_MissingLogged = true;

        string head = "[TransitionTable] 未找到转换表资产 Resources/" + RESOURCE_PATH + "。";
#if UNITY_EDITOR
        // Editor 下 InitDefaultTransitions 仍然编译进来（它是新建资产 / 重置基线的模板），
        // 所以这里其实是「能跑，但打包后会坏」—— 必须说清，否则人会以为什么事都没有。
        Debug.LogError(head
            + "当前在 Editor 下，状态机已回落到内置默认表（InitDefaultTransitions）可以继续编辑；"
            + "但**打包后该内置表不存在**，届时所有单位将无任何合法转换（卡死在当前状态）。"
            + "请在打包前用菜单 Tools → Battle Framework → Transition Matrix 创建并保存资产。");
#else
        Debug.LogError(head
            + "运行时状态机将无任何合法转换（所有单位卡死在当前状态）。"
            + "请用菜单 Tools → Battle Framework → Transition Matrix 创建并保存转换表资产。");
#endif
    }
"""))

STEPS.append((WIN, "hint", """                "运行时没有可用资产 ⇒ 状态机将无任何合法转换（所有单位卡死在当前状态）。"
                + "立即点「新建资产」→「保存到资产」（默认路径 "
                + CombatTransitionTable.ASSET_PATH + "）。", MessageType.None);
""", """                "没有可用资产 ⇒ **打包后**状态机将无任何合法转换（所有单位卡死在当前状态）。"
                + "当前 Editor 下会回落到内置默认表（可继续编辑，不代表打包后正常）。"
                + "立即点「新建资产」→「保存到资产」（默认路径 "
                + CombatTransitionTable.ASSET_PATH + "）。", MessageType.Warning);
"""))

STEPS.append((WIN, "lamp", """        EditorGUILayout.LabelField(hasAsset ? "● 运行时：资产生效" : "● 运行时：代码内置表",
            EditorStyles.boldLabel);
""", """        EditorGUILayout.LabelField(hasAsset ? "● 打包后：资产生效"
            : "● 打包后：无资产（Editor 暂用内置表）", EditorStyles.boldLabel);
"""))


def main():
    tbl = io.open(TABLE, encoding="utf-8").read()
    if "#if UNITY_EDITOR\n        // Editor 下 InitDefaultTransitions" in tbl:
        print("SKIP: 文案已修正。")
        return 0

    files = {}
    for path, name, old, new in STEPS:
        files.setdefault(path, []).append((name, old, new))

    for path, items in files.items():
        cur = io.open(path, encoding="utf-8").read()
        for name, old, new in items:
            cnt = cur.count(old)
            if cnt != 1:
                print("FAIL: %s / %s 命中 %d 次（要求 == 1）" % (path.split("\\")[-1], name, cnt))
                return 1
            cur = cur.replace(old, new, 1)
            print("OK  : %-28s %s" % (path.split("\\")[-1], name))
        io.open(path, "w", encoding="utf-8", newline="").write(cur)

    print("WRITE OK")

    time.sleep(1)
    ok = True
    for path, items in files.items():
        again = io.open(path, encoding="utf-8").read()
        for name, old, new in items:
            if new not in again:
                print("VERIFY FAIL: %s / %s" % (path, name))
                ok = False
    if not ok:
        return 1
    print("VERIFY OK (1)")

    time.sleep(3)
    tbl2 = io.open(TABLE, encoding="utf-8").read()
    win2 = io.open(WIN, encoding="utf-8").read()
    for label, good in [
        ("TABLE #if 分支", "#if UNITY_EDITOR\n        // Editor 下 InitDefaultTransitions" in tbl2),
        ("WIN 提示文案", "打包后**状态机将无任何合法转换" in win2 or "打包后**状态机" in win2
            or "没有可用资产 ⇒ **打包后**状态机将无任何合法转换" in win2),
        ("WIN 指示灯", "● 打包后：资产生效" in win2),
    ]:
        print(("OK  : " if good else "FAIL: ") + label)
        if not good:
            ok = False
    if not ok:
        return 1
    print("VERIFY OK (2, 延迟 3 秒)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
