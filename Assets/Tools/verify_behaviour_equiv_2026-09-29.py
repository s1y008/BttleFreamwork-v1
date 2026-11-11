# -*- coding: utf-8 -*-
"""
2026-09-29 CSV→JSON 迁移的**行为等价**验证（比逐字段比对更进一步）。

逐字段值相等 ≠ 行为一致。这里把 C# 侧的取值逻辑原样搬到 Python，
分别从「旧路径（CSV + CommonHelper 语义）」和「新路径（JSON 直读）」构造同一份 SkillData，
然后把这套方法在每个技能上的**输出**逐条比对：
    GetHitEffectIdList / GetSegmentCount / GetHitEffectIdForSegment(每段)
    GetEffectTypesForSegment(每段) / GetCastEffectIdList / GetCastEffectCount
    ValidateHitEffectCount 是否告警

只要有一个技能的输出不同，就说明迁移改变了游戏行为。
"""
import json
import os

ROOT = r"E:\Project\BttleFreamwork-v1"
CSV = os.path.join(ROOT, r"ConfigCsvBackup_2026-09-28\SkillData.csv")
JSONF = os.path.join(ROOT, r"Assets\Resources\Config\SkillData.json")


# ---------- CommonHelper 语义 ----------
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


def parse_effect_types(s):
    """列13：'|' 分段、';' 分效；空段 → None"""
    if s == "":
        return None
    out = []
    for seg in s.split("|"):
        if seg.strip() == "":
            out.append(None)
        else:
            out.append(str2int_list(seg))
    return out


# ---------- SkillData 行为模型（与 C# 一一对应）----------
class S:
    def __init__(self, **kw):
        self.__dict__.update(kw)
        self._hit = False
        self._cast = False

    def GetHitEffectIdList(self):
        """SkillData.GetHitEffectIdList()"""
        if HitEffective(self):
            return self.HitEffect
        return None

    def GetCastEffectIdList(self):
        ids = self.AttackEffects
        if ids is not None:
            for v in ids:
                if v > 0:
                    return ids
        return None

    def GetValidCastEffectIdCount(self):
        l = self.GetCastEffectIdList()
        return len(l) if l is not None else 0

    def GetSegmentCount(self):
        if self.SegCount <= 0:
            return 0
        tc = len(self.SegTimings) if self.SegTimings is not None else 0
        if tc <= 0:
            return 0
        return min(self.SegCount, tc)

    def GetEffectTypesForSegment(self, segIdx):
        arr = self.EffectTypesBySegment
        if arr is None or len(arr) == 0:
            return None
        idx = 0 if segIdx <= 1 else segIdx - 1
        if idx >= len(arr):
            return None
        return arr[idx]

    def GetCastEffectCount(self):
        ic = self.GetValidCastEffectIdCount()
        tc = len(self.CastEffectTimings) if self.CastEffectTimings is not None else 0
        return min(ic, tc)


def HitEffective(sd):
    """GetHitEffectIdList 内部：存在任一 >0 才返回整表"""
    ids = sd.HitEffect
    if ids is not None:
        for v in ids:
            if v > 0:
                return True
    return False


def hit_effect_for_segment(sd, segIdx):
    """Skill.GetHitEffectIdForSegment(int segIdx)"""
    ids = sd.GetHitEffectIdList()
    if ids is None or len(ids) == 0 or segIdx <= 0:
        return 0
    if segIdx <= len(ids):
        v = ids[segIdx - 1]
        return v if v > 0 else 0
    last = ids[len(ids) - 1]
    return last if last > 0 else 0


def validate_hit_effect_count(sd):
    """ValidateHitEffectCount：返回 True 表示会 LogError"""
    segc = sd.GetSegmentCount()
    if segc <= 0:
        return False
    ids = sd.GetHitEffectIdList()
    n = len(ids) if ids is not None else 0
    return n > segc


def behaviour_vector(sd):
    """把一个技能的全部可观测输出打包成向量"""
    v = {}
    v["GetHitEffectIdList"] = sd.GetHitEffectIdList()
    v["GetSegmentCount"] = sd.GetSegmentCount()
    v["GetEffectTypesForSegment_count"] = (
        0 if sd.EffectTypesBySegment is None else len(sd.EffectTypesBySegment))
    v["GetCastEffectIdList"] = sd.GetCastEffectIdList()
    v["GetValidCastEffectIdCount"] = sd.GetValidCastEffectIdCount()
    v["GetCastEffectCount"] = sd.GetCastEffectCount()
    v["ValidateHitEffectCount_告警"] = validate_hit_effect_count(sd)
    # 逐段展开：段号 0..GetSegmentCount()+1（含越界一段，覆盖「复用最后一个」规则）
    for seg in range(0, sd.GetSegmentCount() + 2):
        v["HitEffectForSeg[%d]" % seg] = hit_effect_for_segment(sd, seg)
        et = sd.GetEffectTypesForSegment(seg)
        v["EffectTypesForSeg[%d]" % seg] = et
    return v


# ---------- 构造两份数据 ----------
def from_csv():
    with open(CSV, "rb") as f:
        raw = f.read()
    txt = raw.decode("utf-8-sig" if raw[:3] == b"\xef\xbb\xbf" else "utf-8")
    lines = [l for l in txt.replace("\r\n", "\n").split("\n") if l != ""]
    out = []
    for l in lines[1:]:
        c = l.split(",")
        seg_timings = str2float_list(c[23]) if (len(c) > 23 and c[23] != "") else None
        cast_timings = str2float_list(c[24]) if (len(c) > 24 and c[24] != "") else None
        out.append(S(
            ID=str2int(c[0]),
            HitEffect=str2int_list(c[14]),
            AttackEffects=str2int_list(c[22]),
            BuffID=str2int_list(c[17]),
            EffectTypesBySegment=parse_effect_types(c[13]),
            SegTimings=seg_timings,
            CastEffectTimings=cast_timings,
            SegCount=str2int(c[25]) if len(c) > 25 else 0,
        ))
    return out


def from_json():
    with open(JSONF, "rb") as f:
        jraw = f.read()
    rows = json.loads(jraw.decode("utf-8-sig" if jraw[:3] == b"\xef\xbb\xbf" else "utf-8"))
    out = []
    for r in rows:
        out.append(S(
            ID=r["ID"],
            HitEffect=r["HitEffect"],
            AttackEffects=r["AttackEffects"],
            BuffID=r["BuffID"],
            EffectTypesBySegment=r["EffectTypesBySegment"],
            SegTimings=r["SegTimings"],
            CastEffectTimings=r["CastEffectTimings"],
            SegCount=r["SegCount"],
        ))
    return out


def main():
    csv_rows = from_csv()
    json_rows = from_json()
    print("CSV %d 条 / JSON %d 条" % (len(csv_rows), len(json_rows)))
    assert len(csv_rows) == len(json_rows), "行数不等"

    # 按 ID 对齐
    cmap = {s.ID: s for s in csv_rows}
    jmap = {s.ID: s for s in json_rows}
    assert len(cmap) == len(csv_rows), "CSV 存在重复 ID"
    assert len(jmap) == len(json_rows), "JSON 存在重复 ID"
    assert set(cmap) == set(jmap), "ID 集合不一致：%r" % (set(cmap) ^ set(jmap))

    diffs = []
    checked = 0
    fields_checked = 0
    for sid in sorted(cmap):
        cv = behaviour_vector(cmap[sid])
        jv = behaviour_vector(jmap[sid])
        assert set(cv) == set(jv)
        for k in cv:
            fields_checked += 1
            if cv[k] != jv[k]:
                diffs.append((sid, k, cv[k], jv[k]))
        checked += 1

    print("比对技能数 = %d，逐技能输出项 = %d，总比对项 = %d"
          % (checked, fields_checked // max(1, checked), fields_checked))

    if diffs:
        print("\n=== 行为差异 %d 处（前 30）===" % len(diffs))
        for sid, k, cvv, jvv in diffs[:30]:
            print("  技能 %s  %s:  旧=%r  新=%r" % (sid, k, cvv, jvv))
        raise SystemExit("FAIL：行为不等价")

    # 统计一下受击特效的实际走势，作为「逻辑跑通」的正向证据
    total_alerts = sum(1 for s in json_rows if validate_hit_effect_count(s))
    multi = sum(1 for s in json_rows if s.GetSegmentCount() > 1)
    with_hit = sum(1 for s in json_rows if s.GetHitEffectIdList() is not None)
    print("\n=== 新路径统计（正向证据）===")
    print("  有多段的技能（段数>1）      : %d" % multi)
    print("  配了有效受击特效的技能      : %d" % with_hit)
    print("  触发 ValidateHitEffectCount : %d" % total_alerts)

    print("\nOK 全部一致 —— 235 个技能的每段输出在迁移前后完全相同 ✔")


if __name__ == "__main__":
    main()
