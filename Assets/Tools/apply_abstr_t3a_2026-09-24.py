# -*- coding: utf-8 -*-
"""T3-a：GetHitEffectIdForSegment private → public（效果类需要调用）。扩大可见性，无破坏性。"""
import io, sys, os, shutil

P = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Skill.cs"
BAK = P + ".bak.2026-09-24-abstr"

raw = io.open(P, encoding="utf-8", newline="").read()
src = raw.replace("\r\n", "\n")

old = "    private int GetHitEffectIdForSegment(int segIdx)"
new = "    public int GetHitEffectIdForSegment(int segIdx)"

cnt = src.count(old)
if cnt != 1:
    print("[FAIL] 命中 %d 次（期望 1）" % cnt)
    sys.exit(1)

src = src.replace(old, new, 1)
io.open(P, "w", encoding="utf-8", newline="").write(src.replace("\n", "\r\n"))

chk = io.open(P, encoding="utf-8", newline="").read().replace("\r\n", "\n")
if new not in chk:
    print("[FAIL] 复验失败")
    if os.path.exists(BAK):
        shutil.copyfile(BAK, P)
        print("[ROLLBACK] 已还原")
    sys.exit(1)

print("[OK] GetHitEffectIdForSegment 已改为 public")
print("残留 private int GetHitEffectIdForSegment =", chk.count("private int GetHitEffectIdForSegment"))
print("CRLF =", io.open(P, encoding="utf-8", newline="").read().count("\r\n"), "/ 行数 =", chk.count("\n"))
