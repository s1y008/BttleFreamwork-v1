"""HTML 标签配平检查 —— 改完文档后的最后一道防线。

【为什么需要它】批量改 HTML 时最容易犯的错是「吃掉某个闭合标签」，而浏览器会容错渲染、
肉眼也看不出来（历史上曾误删 `<tbody>` 而未被发现）。本脚本用标准库 HTMLParser 逐标签压栈，
一旦出现错配 / 多余闭合 / EOF 时栈不空，就直接报出「哪个标签在哪一行被谁提前闭合」。

【调用方式】
  PY="C:/Users/Administrator/.workbuddy/binaries/python/versions/3.13.12/python.exe"
  "$PY" "E:/Project/BttleFreamwork-v1/Assets/Tools/check_html_tags.py" 文档1.html 文档2.html

【期望输出】
  tags  : OK (no mismatch)
  EOF   : OK (stack empty)
"""

import sys
from html.parser import HTMLParser

VOID = {
    "br", "img", "hr", "input", "meta", "link", "area", "base",
    "col", "embed", "source", "track", "wbr",
}


class Balancer(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.stack = []      # [(tag, (line, col))]
        self.errors = []

    def handle_starttag(self, tag, attrs):
        if tag in VOID:
            return
        self.stack.append((tag, self.getpos()))

    def handle_startendtag(self, tag, attrs):
        pass  # 自闭合标签（含 SVG 的 <path/> 等）不参与配平

    def handle_endtag(self, tag):
        if tag in VOID:
            return
        if not self.stack:
            self.errors.append("extra </%s> at %s" % (tag, self.getpos()))
            return
        if self.stack[-1][0] == tag:
            self.stack.pop()
            return
        # 栈顶不是它 → 往回找同名标签，中间的都算「被跳过而未闭合」
        for i in range(len(self.stack) - 1, -1, -1):
            if self.stack[i][0] == tag:
                still_open = [t for t, _ in self.stack[i + 1:]]
                self.errors.append(
                    "</%s> at %s closes <%s> opened at %s; still-open inside: %s"
                    % (tag, self.getpos(), tag, self.stack[i][1], still_open)
                )
                del self.stack[i:]
                return
        self.errors.append("stray </%s> at %s" % (tag, self.getpos()))


def check(path):
    p = Balancer()
    with open(path, encoding="utf-8") as fh:
        p.feed(fh.read())

    ok = True
    print("===", path)
    if p.errors:
        ok = False
        for e in p.errors[:20]:
            print("   MISMATCH", e)
    else:
        print("   tags  : OK (no mismatch)")
    if p.stack:
        ok = False
        print("   EOF   : UNCLOSED ->", [(t, pos) for t, pos in p.stack][:20])
    else:
        print("   EOF   : OK (stack empty)")
    return ok


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    all_ok = all(check(f) for f in sys.argv[1:])
    print("\nRESULT =", "ALL OK" if all_ok else "HAS PROBLEM")
    sys.exit(0 if all_ok else 1)
