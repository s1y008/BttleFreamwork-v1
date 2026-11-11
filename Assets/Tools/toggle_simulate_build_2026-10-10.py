# -*- coding: utf-8 -*-
"""
开关：临时模拟「非 Editor 编译条件」，用来在 Editor 内证明打包后确实没有内置表。

on  = 定义 SIMULATE_PLAYER_BUILD，并把 InitDefaultTransitions 的条件改成
      #if UNITY_EDITOR && !SIMULATE_PLAYER_BUILD  ⇒ Editor 下也走 #else 空实现
off = 还原（删 #define、条件改回 #if UNITY_EDITOR）

⚠ 这只是验证手段，验完必须 off。
"""
import io, sys, time

TARGET = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\StateMachine\CombatStateMachine.cs"

DEFINE = "#define SIMULATE_PLAYER_BUILD\n\n"
OLD_IF = "#if UNITY_EDITOR\n    /// <summary>\n    /// 默认转换表"
NEW_IF = "#if UNITY_EDITOR && !SIMULATE_PLAYER_BUILD\n    /// <summary>\n    /// 默认转换表"


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "on"
    text = io.open(TARGET, encoding="utf-8").read()

    if mode == "on":
        if DEFINE in text:
            print("SKIP: 已处于模拟状态。")
            return 0
        if text.count(OLD_IF) != 1:
            print("FAIL: #if 条件命中 %d 次" % text.count(OLD_IF))
            return 1
        text = text.replace(OLD_IF, NEW_IF, 1)
        text = DEFINE + text
        print("OK  : 已开启模拟（#define + 条件改 && !SIMULATE_PLAYER_BUILD）")
    else:
        if DEFINE not in text:
            print("SKIP: 不处于模拟状态。")
            return 0
        text = text.replace(DEFINE, "", 1)
        if text.count(NEW_IF) != 1:
            print("FAIL: 还原时未找到模拟条件")
            return 1
        text = text.replace(NEW_IF, OLD_IF, 1)
        print("OK  : 已还原（删 #define + 条件改回 #if UNITY_EDITOR）")

    io.open(TARGET, "w", encoding="utf-8", newline="").write(text)

    time.sleep(1)
    again = io.open(TARGET, encoding="utf-8").read()
    if mode == "on":
        ok = DEFINE in again and NEW_IF in again
    else:
        ok = DEFINE not in again and OLD_IF in again
    print("VERIFY:", "OK (1)" if ok else "FAIL")
    if not ok:
        return 1

    time.sleep(3)
    third = io.open(TARGET, encoding="utf-8").read()
    if mode == "on":
        ok2 = DEFINE in third and NEW_IF in third
    else:
        ok2 = DEFINE not in third and OLD_IF in third
    print("VERIFY:", "OK (2, 延迟 3 秒)" if ok2 else "FAIL")
    return 0 if ok2 else 1


if __name__ == "__main__":
    sys.exit(main())
