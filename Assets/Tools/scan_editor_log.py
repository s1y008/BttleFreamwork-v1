# -*- coding: utf-8 -*-
"""扫描 Editor.log：确认工程归属、编译错误增量、dll 输出先后关系。"""
import io, os, sys

LOG = r"C:\Users\Administrator\AppData\Local\Unity\Editor\Editor.log"
OUT = r"E:\Project\BttleFreamwork-v1\Assets\_r3.txt"

lines = []
with io.open(LOG, "r", encoding="utf-8", errors="replace") as f:
    for i, line in enumerate(f, 1):
        lines.append((i, line.rstrip("\n")))

buf = []
buf.append("Editor.log 总行数 = %d" % len(lines))
buf.append("工程归属 BttleFreamwork-v1 命中 = %d" % sum(1 for _, l in lines if "BttleFreamwork-v1" in l))

err_rows = [(n, l) for n, l in lines if "error CS" in l]
buf.append("")
buf.append("=== error CS 总数 = %d ===" % len(err_rows))
for n, l in err_rows[-6:]:
    buf.append("  [%d] %s" % (n, l.strip()[:160]))

# 编译产物 / 编译动作的行号
def find(kw):
    return [(n, l) for n, l in lines if kw in l]

csc = find("Csc Library/Bee")
csproj = find("Assembly-CSharp.csproj")
dllcopy = find("CopyFiles Library/ScriptAssemblies/Assembly-CSharp.dll")
succ = find("Compilation completed")
fail = find("Compilation failed")

buf.append("")
buf.append("=== 最近 Csc 行号 = %s" % ([n for n, _ in csc][-3:],))
buf.append("=== 最近 dll CopyFiles 行号 = %s" % ([n for n, _ in dllcopy][-3:],))
buf.append("=== 最近 Compilation completed 行号 = %s" % ([n for n, _ in succ][-3:],))
buf.append("=== 最近 Compilation failed 行号 = %s" % ([n for n, _ in fail][-3:],))

last_err = err_rows[-1][0] if err_rows else -1
last_dll = dllcopy[-1][0] if dllcopy else -1
last_ok = succ[-1][0] if succ else -1
last_fail = fail[-1][0] if fail else -1

buf.append("")
buf.append("=== 判定 ===")
buf.append("最后 error CS    行号 = %d" % last_err)
buf.append("最后 dll 输出    行号 = %d" % last_dll)
buf.append("最后 completed   行号 = %d" % last_ok)
buf.append("最后 failed      行号 = %d" % last_fail)
if last_ok > last_err and last_ok > last_fail:
    buf.append("结论：最后一次编译 = 成功（成功标记晚于所有错误）")
elif last_fail > last_ok:
    buf.append("结论：最后一次编译 = 失败")
else:
    buf.append("结论：无法判定，需人工看日志")

with io.open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(buf))
print("\n".join(buf))
