# -*- coding: utf-8 -*-
"""
2026-09-29 CSV→JSON 迁移的**代码侧静态检查**：
  Newtonsoft 反序列化到 C# 时，若 JSON 里的键在目标类中
    ① 不存在同名成员        → 该键被静默丢弃（值丢失，无报错）
    ② 是 readonly 字段      → 写不进去，保持默认值
    ③ 是无 setter 的属性    → 写不进去，保持默认值
  这三类都不报编译错误，也不抛异常，只有运行时数据悄悄不对 —— CSV 时代不存在这个问题。

本脚本把每张表的 JSON 键与目标类的成员声明交叉核对，把这三类风险一次性揪出来。
"""
import json
import os
import re

ROOT = r"E:\Project\BttleFreamwork-v1\Assets"
JSON_DIR = os.path.join(ROOT, r"Resources\Config")

TABLES = [
    ("SkillData.json", r"Scripts\BattleFreamwork\ConfigData\SkillData.cs", "SkillData"),
    ("WarriorData.json", r"Scripts\BattleFreamwork\ConfigData\WarriorData.cs", "WarriorData"),
    ("AttackNpcData.json", r"Scripts\BattleFreamwork\ConfigData\AttackNpcData.cs", "AttackNpcData"),
    ("BufferData.json", r"Scripts\BattleFreamwork\BattleData\BuffData.cs", "BuffData"),
    ("EffectData.json", r"Scripts\ConfigData\EffectData.cs", "EffectData"),
]

# 匹配： public/private/internal  (static) (readonly) Type Name { get; set; }  或  ...;
MEMBER_RE = re.compile(
    r'^\s*(?:public|private|protected|internal)\s+'
    r'(?:(?:static|readonly|const)\s+)*'
    r'(?P<type>[A-Za-z_][\w\.<>\[\]]*)\s+'
    r'(?P<name>\w+)\s*(?P<tail>[{;=].*)?$'
)


def members_of(path, class_name):
    """粗略提取类的成员：返回 {成员名: (type, 是否可写, 原始行)}"""
    with open(path, "rb") as f:
        raw = f.read()
    txt = raw.decode("utf-8-sig" if raw[:3] == b"\xef\xbb\xbf" else "utf-8").replace("\r\n", "\n")
    lines = txt.split("\n")

    # 找类声明起点
    start = None
    cls_re = re.compile(r'^\s*(?:public\s+)?(?:sealed\s+|partial\s+)*class\s+' + class_name + r'\b')
    for i, l in enumerate(lines):
        if cls_re.match(l):
            start = i
            break
    if start is None:
        raise SystemExit("找不到类 %s 的定义: %s" % (class_name, path))

    out = {}
    depth = 0
    started = False
    for n in range(start, len(lines)):
        line = lines[n]
        depth += line.count("{") - line.count("}")
        if not started and "{" in line:
            started = True
        if started and depth == 0 and n > start:
            break
        m = MEMBER_RE.match(line)
        if not m:
            continue
        name = m.group("name")
        tail = (m.group("tail") or "").strip()
        if "readonly" in line or "const " in line:
            writeable = False
        elif tail.startswith("{"):
            # 属性：必须有 set; （init 也算，Newtonsoft 支持）
            writeable = bool(re.search(r'\b(?:set|init)\s*;', tail)) or "=>" not in tail
            if "=>" in tail:  # 表达式体只读属性
                writeable = False
        else:
            writeable = True  # 普通字段
        out[name] = (m.group("type"), writeable, line.strip()[:80])
    return out


def main():
    all_ok = True
    for jf, cs_rel, cls in TABLES:
        jpath = os.path.join(JSON_DIR, jf)
        cspath = os.path.join(ROOT, cs_rel)
        with open(jpath, "rb") as f:
            jraw = f.read()
        rows = json.loads(jraw.decode("utf-8-sig" if jraw[:3] == b"\xef\xbb\xbf" else "utf-8"))
        mem = members_of(cspath, cls)

        print("\n" + "=" * 76)
        print("■ %s  →  class %s  (%s)" % (jf, cls, os.path.basename(cspath)))
        print("=" * 76)
        print("   JSON 键 %d 个 / 类成员 %d 个" % (len(rows[0]), len(mem)))

        missing, readonly, ok = [], [], []
        for key in rows[0].keys():
            if key not in mem:
                missing.append(key)
            elif not mem[key][1]:
                readonly.append((key, mem[key][2]))
            else:
                ok.append((key, mem[key][0]))

        print("   ✅ 可正常写入: %d" % len(ok))
        for k, t in ok:
            print("       %-24s : %s" % (k, t))

        if missing:
            all_ok = False
            print("   ⚠⚠ JSON 键在类中**不存在** → 值会被静默丢弃: %d" % len(missing))
            for k in missing:
                print("       %s" % k)
        if readonly:
            all_ok = False
            print("   ⚠⚠ 成员不可写（readonly/无setter）→ 反序列化后保持默认值: %d" % len(readonly))
            for k, decl in readonly:
                print("       %-20s %s" % (k, decl))

        # 反向：类的成员没出现在 JSON 里（会保持默认值，需确认是否有意）
        extra = [m for m in mem if m not in rows[0]]
        if extra:
            print("   ℹ 类成员未在 JSON 中出现（保持默认值，通常是运行期字段）: %r" % extra)

    print("\n" + "=" * 76)
    print("结论:", "全部键均可正确反序列化 ✔" if all_ok else "存在需要修复的问题 ✘")
    print("=" * 76)


if __name__ == "__main__":
    main()
