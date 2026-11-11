# -*- coding: utf-8 -*-
"""检查 UnitBuffComponent 是否被场景/预制体持久化引用，以及是否存在外部 GetComponent。

用途：把 MonoBehaviour 组件改成普通类（无 [SerializeField]、不再挂载）前，
必须先确认没有 prefab/scene 引用它的 GUID —— 否则改完会变成 Missing Script（不可逆）。
"""
import os
import re
import sys

TARGET = 'Assets/Scripts/BattleFreamwork/Controller/UnitBuffComponent.cs'
ROOT = 'Assets'
SKIP = ('.codely-cli', 'Library', 'Temp', 'obj')


def read(path):
    with open(path, encoding='utf-8', errors='ignore') as f:
        return f.read()


def main():
    meta = TARGET + '.meta'
    if not os.path.exists(meta):
        print('找不到 meta:', meta)
        return 1
    m = re.search(r'guid:\s*([0-9a-f]{32})', read(meta))
    if not m:
        print('meta 里没有 guid:', meta)
        return 1
    guid = m.group(1)
    print('UnitBuffComponent GUID =', guid)

    hits = []
    for dp, dn, fn in os.walk(ROOT):
        if any(s in dp for s in SKIP):
            continue
        for f in fn:
            if f.lower().endswith(('.prefab', '.unity', '.asset')):
                p = os.path.join(dp, f)
                try:
                    if guid in read(p):
                        hits.append(p)
                except Exception:
                    pass
    print('场景/预制体引用 :', hits if hits else '零命中 -> 无 Missing Script 风险')

    print()
    print('=== 外部 GetComponent<UnitBuffComponent> 检查 ===')
    found = []
    for dp, dn, fn in os.walk(ROOT):
        if any(s in dp for s in SKIP):
            continue
        for f in fn:
            if f.endswith('.cs') and '.bak' not in f.lower():
                p = os.path.join(dp, f)
                for i, line in enumerate(read(p).split('\n'), 1):
                    if 'UnitBuffComponent' in line and 'GetComponent' in line:
                        found.append('  %s:%d  %s' % (p, i, line.strip()[:110]))
    print('\n'.join(found) if found else '  (无)')

    print()
    print('=== 已删除 MonoBehaviour 能力的实际使用情况 ===')
    body = read(TARGET)
    for kw in ['gameObject', 'transform', 'GetComponent<', 'Destroy(', 'Invoke(', 'StartCoroutine',
               'OnEnable', 'OnDisable', 'Awake', 'Update(', 'OnDestroy']:
        n = body.count(kw)
        print('  %-16s %d' % (kw, n))


if __name__ == '__main__':
    sys.exit(main())
