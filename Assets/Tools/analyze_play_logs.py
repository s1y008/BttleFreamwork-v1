# -*- coding: utf-8 -*-
"""解析 console_get_logs 输出：统计 Error 数、校验三条事实链、检查 Buff 流血是否生效。"""
import io, json, re, sys, collections

SRC = sys.argv[1] if len(sys.argv) > 1 else r"E:\Project\BttleFreamwork-v1\Assets\_logs1.json"
OUT = r"E:\Project\BttleFreamwork-v1\Assets\_logsana.txt"

raw = io.open(SRC, "r", encoding="utf-8").read()
try:
    obj = json.loads(raw)
except Exception as e:
    io.open(OUT, "w", encoding="utf-8").write("JSON 解析失败: %s\n%s" % (e, raw[:500]))
    sys.exit(1)

res = obj.get("result", obj)
logs = res.get("logs", res.get("entries", []))
buf = []
buf.append("日志条数 = %d" % len(logs))

if logs and isinstance(logs[0], dict):
    keys = sorted({k for l in logs for k in l.keys()})
    buf.append("日志字段 = %s" % keys)

# 统一取消息
def msg(l):
    if isinstance(l, str):
        return l
    for k in ("message", "msg", "text", "log", "content", "condition"):
        if k in l and isinstance(l[k], str):
            return l[k]
    return json.dumps(l, ensure_ascii=False)

def lvl(l):
    if isinstance(l, dict):
        for k in ("level", "type", "logType", "severity"):
            if k in l:
                return str(l[k])
    return ""

msgs = [msg(l) for l in logs]
lvls = [lvl(l) for l in logs]

# 级别分布
buf.append("")
buf.append("=== 级别分布 ===")
for k, v in collections.Counter(lvls).most_common():
    buf.append("  %-12s %d" % (k, v))

# Error 明细（去重）
errs = [m for m, lv in zip(msgs, lvls) if "rror" in lv or "Exception" in lv or "ssert" in lv]
buf.append("")
buf.append("=== Error/Exception 条数 = %d ===" % len(errs))
cnt = collections.Counter(re.sub(r"\d+", "N", e.strip()[:150]) for e in errs)
for k, v in cnt.most_common(20):
    buf.append("  x%-4d %s" % (v, k))

# 关键标记计数
marks = [
    "[JsonConfig]", "[SkillLoad]", "[HitEffectCalc]", "[HitEffect]", "[SegEffect]",
    "[HitReact]", "[CastCtx]", "[AddBuff]", "[BuffAdd]", "[BuffTick]", "[DoT]",
    "[HitEffectWarn]", "流血", "ValidateBuffBindings", "ValidateHitEffectCount",
    "[ConfigLoad]", "[Config]",
]
buf.append("")
buf.append("=== 关键标记计数 ===")
for mk in marks:
    hits = [m for m in msgs if mk in m]
    buf.append("  %-24s %d" % (mk, len(hits)))
    if 0 < len(hits) <= 6:
        for h in hits:
            buf.append("        %s" % h.strip()[:170])

# 三链是否 1:1
for a, b in (("[HitEffectCalc]", "[HitEffect]"), ("[SegEffect]", "[HitReact]")):
    na = sum(1 for m in msgs if a in m)
    nb = sum(1 for m in msgs if b in m)
    buf.append("")
    buf.append("=== %s vs %s : %d / %d %s" % (a, b, na, nb, "(1:1 OK)" if na == nb else "(差异! 死亡后可能出现)"))

io.open(OUT, "w", encoding="utf-8").write("\n".join(buf))
print("\n".join(buf))
