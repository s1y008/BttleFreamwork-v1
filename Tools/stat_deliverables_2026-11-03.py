# -*- coding: utf-8 -*-
"""产出物精确统计（2026-11-03）—— 口径：本次演化直接产生/维护的文件，排除插件自带文档。"""
import os, json, collections
ROOT = r"E:\Project\BttleFreamwork-v1"
A = os.path.join(ROOT, "Assets")

def rel(p): return os.path.relpath(p, ROOT).replace("\\", "/")
def sz(p): return os.path.getsize(p)

def files_in(d, pred):
    out = []
    if not os.path.isdir(d): return out
    for f in os.listdir(d):
        p = os.path.join(d, f)
        if os.path.isfile(p) and pred(f): out.append(p)
    return out

def rec(fs):
    return {"n": len(fs), "bytes": sum(sz(p) for p in fs),
            "top": [{"name": os.path.basename(p), "bytes": sz(p)} for p in sorted(fs, key=sz, reverse=True)[:5]]}

cats = collections.OrderedDict()

fs = files_in(A, lambda f: f.lower().endswith(".html"))
cats["技术/教学 HTML 交付物"] = rec(fs)

fs = files_in(A, lambda f: f.lower().endswith(".md") and f != "CODELY.md")
fs += files_in(ROOT, lambda f: f.lower().endswith(".md") and f != "CODELY.md")
cats["Markdown 报告 / 方案 / 修改记录"] = rec(fs)

fs = []
for dp, dn, fn in os.walk(os.path.join(A, "Scripts")):
    for f in fn:
        if f.endswith(".cs"): fs.append(os.path.join(dp, f))
cats["运行时 C# 源码"] = rec(fs)

fs = []
for dp, dn, fn in os.walk(os.path.join(A, "Editor")):
    for f in fn:
        if f.endswith(".cs") and ".bak" not in f: fs.append(os.path.join(dp, f))
cats["Editor 工具 C# 源码"] = rec(fs)

fs = files_in(os.path.join(A, "Resources", "Config"),
              lambda f: f.lower().endswith((".json", ".asset")) and ".bak" not in f)
cats["配置表（JSON / SO 资产）"] = rec(fs)

fs = files_in(os.path.join(A, "Tools"), lambda f: f.lower().endswith((".py", ".js")))
fs += files_in(os.path.join(ROOT, "Tools"), lambda f: f.lower().endswith((".py", ".js")))
cats["改造 / 验证 / 冒烟脚本"] = rec(fs)

fs = files_in(os.path.join(A, ".workbuddy", "memory"),
              lambda f: f.lower().endswith(".md") and ".bak" not in f)
cats["工作日志与长期记忆"] = rec(fs)

fs = []
for dp, dn, fn in os.walk(ROOT):
    dn[:] = [d for d in dn if d not in ("Library", "Temp", ".git", "PackageCache")]
    for f in fn:
        if ".bak" in f.lower() and not f.endswith(".meta"): fs.append(os.path.join(dp, f))
cats["物理备份链（.bak*）"] = rec(fs)

fs = []
for d in ("_p18_unused", "_planA_unused", "_diagnose", "_unused_editor"):
    for dp, dn, fn in os.walk(os.path.join(A, d)):
        for f in fn:
            if not f.endswith(".meta"): fs.append(os.path.join(dp, f))
cats["回退隔离区 / 诊断产物"] = rec(fs)

total_n = sum(v["n"] for v in cats.values())
total_b = sum(v["bytes"] for v in cats.values())

print("=== 产出物统计（三重自检：抽样 / 总条数 / 总大小）===")
for k, v in cats.items():
    print(f"{k}: {v['n']} 个 / {v['bytes']:,} B")
    for t in v["top"][:3]:
        print(f"    · {t['name']}  {t['bytes']:,} B")
print(f"\n合计 {total_n} 个文件 / {total_b:,} 字节 / {total_b/1024/1024:.2f} MB")

mem = os.path.join(A, ".workbuddy", "memory")
logs = sorted([f for f in os.listdir(mem) if f.startswith("2026-") and f.endswith(".md")])
print(f"\n=== 工作日志 {len(logs)} 份，{logs[0]} → {logs[-1]} ===")
print(f"    总字节 {sum(sz(os.path.join(mem,f)) for f in logs):,}")

out = {"cats": [{"name": k, **v} for k, v in cats.items()],
       "total_n": total_n, "total_b": total_b,
       "logs": {"n": len(logs), "first": logs[0], "last": logs[-1],
                "bytes": sum(sz(os.path.join(mem, f)) for f in logs)}}
with open(os.path.join(ROOT, "Tools", "_star_stats.json"), "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=1)
print("\n[OK] 已写 Tools/_star_stats.json")
