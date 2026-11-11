# -*- coding: utf-8 -*-
"""给矩阵窗口补 using System.Collections;（IDictionary / DictionaryEntry 需要）。"""
import io, sys, time

TARGET = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Editor\CombatTransitionMatrixWindow.cs"

OLD = "using System;\nusing System.Collections.Generic;\nusing System.IO;\n"
NEW = "using System;\nusing System.Collections;\nusing System.Collections.Generic;\nusing System.IO;\n"


def main():
    text = io.open(TARGET, encoding="utf-8").read()

    if "using System.Collections;" in text:
        print("SKIP: using System.Collections 已存在。")
        return 0

    cnt = text.count(OLD)
    if cnt != 1:
        print("FAIL: using 块命中 %d 次（要求 == 1）" % cnt)
        return 1

    text = text.replace(OLD, NEW, 1)
    io.open(TARGET, "w", encoding="utf-8", newline="").write(text)
    print("WRITE OK")

    time.sleep(1)
    again = io.open(TARGET, encoding="utf-8").read()
    if NEW not in again:
        print("VERIFY FAIL")
        return 1
    print("VERIFY OK (1)")

    time.sleep(3)
    third = io.open(TARGET, encoding="utf-8").read()
    if "using System.Collections;" in third:
        print("VERIFY OK (2, 延迟 3 秒)")
    else:
        print("VERIFY FAIL (2)")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
