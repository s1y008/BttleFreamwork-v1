# -*- coding: utf-8 -*-
"""
2026-09-29 CSV → JSON 配置迁移 **数据等价性验证**。

用法:
    python verify_json_vs_csv_2026-09-29.py

思路（不依赖已被别的AI删掉的旧解析代码，也不依赖人工列名比对）：
  对每个 JSON 键，遍历所有 CSV 列，模拟 C# CommonHelper 的解析规则，
  找出「所有行都匹配」的那一列 —— 匹配率 100% 即证明该键取值正确。
  这样做的副作用是：**列错位、被丢弃的列、值被改写**都会自动暴露。
  AttackNpcData 的 CSV 表头有 7 列是空列名，靠列名根本没法比，必须这么干。

Python 模拟的 C# 语义（CommonHelper.cs 原文核对过）：
  Str2Int(s)      = int.TryParse ? value : 0
  Str2Float(s)    = float.TryParse ? value : 0
  Str2IntList(s)  = s.Split(';') 每项 Str2Int     ⚠ 空串 "" → [""] → [0]
  Str2FloatList(s)= 同上，空串 → [0.0]
  Str2Boolean(s)  = "1"/"true"/"yes" → true，其余 false
"""
import json
import os
import sys

ROOT = r"E:\Project\BttleFreamwork-v1"
CSV_DIR = os.path.join(ROOT, "ConfigCsvBackup_2026-09-28")
JSON_DIR = os.path.join(ROOT, r"Assets\Resources\Config")

# (csv文件名, json文件名)
TABLES = [
    ("SkillData.csv", "SkillData.json"),
    ("WarriorData.csv", "WarriorData.json"),
    ("AttackNpcData.csv", "AttackNpcData.json"),
    ("BufferData.csv", "BufferData.json"),
    ("EffectData.csv", "EffectData.json"),
]


# ---------- C# 语义模拟 ----------
def str2int(s):
    try:
        return int(str(s).strip())
    except Exception:
        return 0


def str2float(s):
    try:
        return float(str(s).strip())
    except Exception:
        return 0.0


def str2int_list(s):
    return [str2int(x) for x in str(s).split(";")]


def str2float_list(s):
    return [str2float(x) for x in str(s).split(";")]


def str2bool(s):
    t = str(s).strip().lower()
    return t == "1" or t == "true" or t == "yes"


TOL = 1e-6


def value_eq(json_val, csv_cell, kind):
    """json_val 与 csv_cell 在某类型下是否等价"""
    if json_val is None:
        return csv_cell == ""
    if isinstance(json_val, bool):
        return str2bool(csv_cell) == json_val
    if isinstance(json_val, int):
        return str2int(csv_cell) == json_val
    if isinstance(json_val, float):
        return abs(str2float(csv_cell) - json_val) < TOL
    if isinstance(json_val, str):
        return csv_cell == json_val
    if isinstance(json_val, list):
        if not json_val:
            # JSON 空列表：CSV 可能是空串（旧路径解析成 [0]，二者语义不同，单独标记）
            return csv_cell == ""
        if isinstance(json_val[0], list):
            return seg_list_eq(json_val, csv_cell)
        if isinstance(json_val[0], bool):
            return [str2bool(x) for x in csv_cell.split(";")] == json_val
        if isinstance(json_val[0], int):
            return str2int_list(csv_cell) == json_val
        if isinstance(json_val[0], float):
            a = [round(x, 6) for x in str2float_list(csv_cell)]
            b = [round(x, 6) for x in json_val]
            return a == b
        if isinstance(json_val[0], str):
            return csv_cell.split(";") == json_val
    return False


def seg_list_eq(json_val, csv_cell):
    """列 13 SkillEffectTypes：'|' 分段、';' 分效"""
    if csv_cell == "":
        return False
    segs = csv_cell.split("|")
    if len(segs) != len(json_val):
        return False
    for a, b in zip(segs, json_val):
        if str2int_list(a) != b:
            return False
    return True


def read_csv_table(path):
    with open(path, "rb") as f:
        raw = f.read()
    txt = raw.decode("utf-8-sig" if raw[:3] == b"\xef\xbb\xbf" else "utf-8")
    lines = [l for l in txt.replace("\r\n", "\n").split("\n") if l != ""]
    header = lines[0].split(",")
    rows = []
    for i, l in enumerate(lines[1:], start=2):
        c = l.split(",")
        rows.append((i, c))
    return header, rows


def build_matrix(header, rows, json_rows):
    """返回 {key: [(col_idx, match_ratio)]}"""
    n = len(header)
    result = {}
    for key in json_rows[0].keys():
        cands = []
        for ci in range(n):
            ok = 0
            bad_examples = []
            for ri, (_, c) in enumerate(rows):
                if ri >= len(json_rows):
                    break
                cell = c[ci] if ci < len(c) else ""
                if value_eq(json_rows[ri].get(key), cell, None):
                    ok += 1
                elif len(bad_examples) < 3:
                    bad_examples.append((json_rows[ri].get(key), cell))
            ratio = ok / max(1, min(len(rows), len(json_rows)))
            cands.append((ci, ratio, bad_examples))
        cands.sort(key=lambda x: -x[1])
        result[key] = cands
    return result


def report(name, csv_file, json_file):
    print("\n" + "=" * 78)
    print("■", name)
    print("=" * 78)
    csv_path = os.path.join(CSV_DIR, csv_file)
    json_path = os.path.join(JSON_DIR, json_file)
    header, rows = read_csv_table(csv_path)
    with open(json_path, "rb") as f:
        jraw = f.read()
    jtxt = jraw.decode("utf-8-sig" if jraw[:3] == b"\xef\xbb\xbf" else "utf-8")
    json_rows = json.loads(jtxt)

    print("CSV  %d 列 / %d 行   |   JSON %d 键 / %d 行"
          % (len(header), len(rows), len(json_rows[0]), len(json_rows)))

    if len(rows) != len(json_rows):
        print("!!! 行数不一致：CSV %d vs JSON %d" % (len(rows), len(json_rows)))

    # 列数不齐的行（旧代码 Length-1 上界会静默丢尾行）
    bad_len = [(i, len(c)) for i, c in rows if len(c) != len(header)]
    if bad_len:
        print("!!! CSV 列数不齐的行 %d 个（前5）: %r" % (len(bad_len), bad_len[:5]))

    matrix = build_matrix(header, rows, json_rows)

    perfect, partial, none_ = [], [], []
    for key, cands in matrix.items():
        best = cands[0]
        if best[1] >= 1.0:
            perfect.append((key, best[0], [c for c in cands if c[1] >= 1.0]))
        elif best[1] > 0:
            partial.append((key, best))
        else:
            none_.append((key, best))

    print("\n-- 完全匹配的键: %d / %d" % (len(perfect), len(matrix)))
    for key, ci, allc in perfect:
        multi = "  ⚠同值候选列=%r" % [("[" + str(c[0]) + "]" + header[c[0]]) for c in allc
                                    if c[0] != ci] if len(allc) > 1 else ""
        print("   %-24s <- CSV[%2d] %-22s%s" % (key, ci, header[ci][:22], multi))

    if partial:
        print("\n-- ⚠ 部分匹配（有差异，必须逐条查）: %d 个" % len(partial))
        for key, (ci, ratio, ex) in partial:
            print("   %-24s 最佳列 CSV[%2d] %-22s 匹配率=%.4f" % (key, ci, header[ci][:22], ratio))
            for jv, cv in ex:
                print("        json=%r  csv=%r" % (jv, cv))

    if none_:
        print("\n-- ⚠ 零匹配（找不到任何对应列）: %d 个" % len(none_))
        for key, (ci, ratio, ex) in none_:
            print("   %-24s 最佳列 CSV[%2d] %-22s 匹配率=%.4f" % (key, ci, header[ci][:22], ratio))
            for jv, cv in ex:
                print("        json=%r  csv=%r" % (jv, cv))

    # 未被任何键消费掉的 CSV 列
    used = set()
    for key, cands in matrix.items():
        if cands[0][1] >= 1.0:
            used.add(cands[0][0])
    unused = [i for i in range(len(header)) if i not in used]
    if unused:
        print("\n-- CSV 中未被任何 JSON 键取用的列 %d 个:" % len(unused))
        for i in unused:
            vals = set((c[i] if i < len(c) else "") for _, c in rows)
            sample = list(vals)[:4]
            all_empty = all(v == "" for v in vals)
            print("   [%2d] %-24s %s  取值样本=%r"
                  % (i, header[i][:24], "（全空）" if all_empty else "", sample))

    return {"perfect": len(perfect), "partial": len(partial), "none": len(none_),
            "total": len(matrix), "unused": len(unused)}


def main():
    summary = {}
    for csv_file, json_file in TABLES:
        summary[json_file] = report(json_file, csv_file, json_file)

    print("\n" + "=" * 78)
    print("汇总")
    print("=" * 78)
    print("%-24s %8s %8s %8s %8s" % ("表", "键数", "全匹配", "有差异", "未用列"))
    for k, v in summary.items():
        flag = "OK" if (v["partial"] == 0 and v["none"] == 0) else "⚠ 需复查"
        print("%-24s %8d %8d %8d %8d   %s"
              % (k, v["total"], v["perfect"], v["partial"] + v["none"], v["unused"], flag))


if __name__ == "__main__":
    main()
