"""UnitySkills (HTTP 8090) 辅助脚本 —— 编译 / Play / 日志验证一条龙。

【为什么需要它】本机 Bash 沙箱的 PATH 不完整：cp / grep / dirname 全部报 command not found，
PowerShell 工具的输出通道也会失效（exit 0 但 stdout 为空）。所以统一改成
「绝对路径直调解释器」，且所有过滤逻辑放在脚本内部，不用 shell 管道。

【调用方式】
  PY="C:/Users/Administrator/.workbuddy/binaries/python/versions/3.13.12/python.exe"
  "$PY" "E:/Project/BttleFreamwork-v1/Assets/Tools/unity_skills.py" <子命令> [参数...]

【子命令】
  state                  GET / —— 打印服务状态与编译状态（服务没起会报 502，说明要先启动）
  call <skill> [json]    POST /skill/<skill>，参数写错会返回 allowedParams
  recompile              强制重编译 + 轮询到空闲（默认最多等 300s）
  errors                 打印 debug_get_errors（count + logs）
  logs [limit]           归类打印 console 日志：去重 + 计数 + Error/Warning 分列
  play | stop            进入 / 退出 Play 模式

【典型流程】改完代码 →
  recompile → errors → logs 前先 call console_start_capture {}
  → play → 等 20~30s → logs → stop
⚠ console_start_capture 必须在 play 之前调用，否则会漏掉加载期的防呆 Warning。
⚠ 若 play 返回 "Already in play mode"，Unity 用的仍是内存里的旧数据 → 必须先 stop 再 play。
"""

import json
import sys
import time
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:8091"   # ★ 2026-09-19 实测：8090 超时、8091 返回 UnitySkills v1.9.4 健康响应。端口以 GET / 实测为准

KEY_MARKERS = (
    "[AttackState]", "[SegEffect]", "[Cast]", "[SkillData]", "CurSelectedSkill",
    # 2026-09-16 补：受击/治疗特效链路（v5 P10 加的两条日志，此前该链路完全不可观测）
    "[HitEffectCalc]", "[HitEffect]", "[Combat]",
)


def _request(path, payload=None, timeout=900):
    data = None
    headers = {}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(
        BASE + path, data=data, headers=headers,
        method="POST" if payload is not None else "GET",
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read().decode("utf-8", "replace")


def skill(name, args=None, timeout=900):
    raw = _request("/skill/" + name, args if args is not None else {}, timeout)
    try:
        return json.loads(raw)
    except ValueError:
        return {"raw": raw}


def state():
    return json.loads(_request("/", None, 30))


def busy(st):
    c = (st or {}).get("compilation", {}) or {}
    return bool(c.get("isCompiling") or c.get("isUpdating") or c.get("domainReloadPending"))


# ---------------------------------------------------------------- 子命令

def cmd_state():
    st = state()
    print(json.dumps(st, ensure_ascii=False, indent=2))
    print("\nCOMPILING:", busy(st))


def cmd_call():
    name = sys.argv[2]
    args = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
    print(json.dumps(skill(name, args), ensure_ascii=False, indent=2))


def cmd_recompile(limit=300):
    print(json.dumps(skill("debug_force_recompile"), ensure_ascii=False))
    start = time.time()
    saw = False
    last = None
    while time.time() - start < limit:
        try:
            b = busy(state())
            if b:
                saw = True
            if ("busy" if b else "idle") != last:
                print("[%s] %s (%.0fs)" % (time.strftime("%H:%M:%S"), "busy" if b else "idle", time.time() - start))
                last = "busy" if b else "idle"
            if saw and not b:
                time.sleep(3)
                if not busy(state()):
                    print("COMPILE DONE")
                    return 0
            if not saw and time.time() - start > 40:
                print("COMPILE DONE (never observed compiling)")
                return 0
        except Exception as e:  # noqa: BLE001
            print("[%s] waiting server... (%s)" % (time.strftime("%H:%M:%S"), type(e).__name__))
        time.sleep(2)
    print("TIMEOUT after %ds" % limit)
    return 1


def cmd_errors():
    res = (skill("debug_get_errors").get("result") or {})
    cnt = res.get("count", 0)
    print("ERROR COUNT =", cnt)
    for lg in res.get("logs") or []:
        print("  -", (lg.get("message") or "").split("\n")[0].strip())
    return 0 if cnt == 0 else 1


def cmd_logs(limit=1000):
    res = (skill("console_get_logs", {"type": "All", "limit": int(limit)}).get("result") or {})
    logs = res.get("logs") or []
    print("TOTAL LOGS:", len(logs))

    types = {}
    errors, buckets, warns = [], {}, {}
    for lg in logs:
        t = lg.get("type", "?")
        types[t] = types.get(t, 0) + 1
        first = (lg.get("message") or "").split("\n")[0].strip()
        if t in ("Error", "Exception", "Assert"):
            errors.append(first)
        if t == "Warning":
            warns[first] = warns.get(first, 0) + 1
        if any(k in first for k in KEY_MARKERS):
            buckets[first] = buckets.get(first, 0) + 1

    print("TYPE COUNT:", types)
    print()
    if errors:
        print("!!!! ERRORS !!!!")
        for e in errors[:40]:
            print("  ", e)
    else:
        print("NO ERRORS")
    print("\n---- KEY LOGS (deduped) ----")
    for k in sorted(buckets):
        print("%5d x  %s" % (buckets[k], k))
    print("\n---- WARNINGS (deduped) ----")
    for k in sorted(warns):
        print("%5d x  %s" % (warns[k], k))
    if not warns:
        print("(none)")
    return 0 if not errors else 1


def cmd_play():
    print(json.dumps(skill("editor_play"), ensure_ascii=False))


def cmd_stop():
    print(json.dumps(skill("editor_stop"), ensure_ascii=False))


USAGE = __doc__

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(USAGE)
        sys.exit(2)
    cmd = sys.argv[1]
    try:
        if cmd == "state":
            cmd_state()
        elif cmd == "call":
            cmd_call()
        elif cmd == "recompile":
            sys.exit(cmd_recompile())
        elif cmd == "errors":
            sys.exit(cmd_errors())
        elif cmd == "logs":
            sys.exit(cmd_logs(sys.argv[2] if len(sys.argv) > 2 else 1000))
        elif cmd == "play":
            cmd_play()
        elif cmd == "stop":
            cmd_stop()
        else:
            print("unknown command:", cmd)
            print(USAGE)
            sys.exit(2)
    except urllib.error.HTTPError as e:
        print("HTTP %s: %s" % (e.code, e.read().decode("utf-8", "replace")))
        print("提示：8090 无服务时会报 502，请先启动 UnitySkills。")
        sys.exit(1)
    except Exception as e:  # noqa: BLE001
        print("ERR %s: %s" % (type(e).__name__, e))
        sys.exit(1)
