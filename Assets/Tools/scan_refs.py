# -*- coding: utf-8 -*-
"""
引用扫描器（2026-09-17 方案③ UnitView / UnitHitReaction 抽离前置）

为什么不用 grep：
  2026-09-16 两次因为 grep --head_limit 结果被 .bak 文件占满，
  误下「该字段是死代码」的结论（m_CurSkill 事件），白查一轮。
  本脚本做三件事来避免同类误判：
    1. 限定工程源码目录，显式排除 .bak* / Library / .codely-cli / obj
    2. 不限条数，全量输出（调用方自己看）
    3. 输出文件名 + 行号 + 原文，便于人工复核
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # .../Assets
EXCLUDE_DIR_MARKERS = ('.codely-cli', 'Library', 'Temp', 'obj', '.workbuddy', 'Resources')
EXCLUDE_FILE_SUFFIX = ('.bak',)
EXCLUDE_FILE_CONTAINS = ('.bak.',)

SOURCE_EXTS = ('.cs', '.prefab', '.unity', '.asset')


def iter_source_files():
    for dirpath, dirnames, filenames in os.walk(ROOT):
        rel = os.path.relpath(dirpath, ROOT)
        if any(m in rel.replace('\\', '/').split('/') for m in EXCLUDE_DIR_MARKERS):
            continue
        for fn in filenames:
            low = fn.lower()
            if any(low.endswith(s) for s in EXCLUDE_FILE_SUFFIX):
                continue
            if any(c in low for c in EXCLUDE_FILE_CONTAINS):
                continue
            if any(low.endswith(e) for e in SOURCE_EXTS):
                yield os.path.join(dirpath, fn)


def scan(symbols):
    files = list(iter_source_files())
    hits = {s: [] for s in symbols}
    for path in files:
        try:
            with open(path, 'r', encoding='utf-8', errors='ignore') as f:
                lines = f.readlines()
        except Exception:
            continue
        for i, line in enumerate(lines, 1):
            for s in symbols:
                if s in line:
                    hits[s].append((path.replace(ROOT, 'Assets'), i, line.rstrip('\n')))
    return hits, len(files)


if __name__ == '__main__':
    symbols = sys.argv[1:] or [
        'OnDamaged', 'OnHeal', 'OnAttackEffect', 'OnHitOut', 'OnAirHit', 'OnPlayerAirHit',
        'OnDizz', 'OnSilence', 'OnAddBuff', 'OnSneer', 'OnHurt', 'EffectGo', '_SubHpCount',
        '_IsHitPoint', 'IsIdleToUseSkill', 'IsUseAttSkill', 'SetMoveAnim', 'NavStop',
        'ResetSkillId', 'SetDead', 'OnDead', 'DoDead', 'KnockbackDuration', 'RequestState',
    ]
    hits, total = scan(symbols)
    print('=' * 70)
    print('扫描源码文件数：%d（已排除 .bak / Library / Temp / .codely-cli / obj）' % total)
    print('=' * 70)
    for s in symbols:
        lst = hits[s]
        print('\n【%s】 命中 %d 处' % (s, len(lst)))
        for p, i, line in lst:
            print('   %s:%d  %s' % (p, i, line.strip()[:150]))
