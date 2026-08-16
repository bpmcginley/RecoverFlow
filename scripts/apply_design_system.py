#!/usr/bin/env python3
"""Re-apply the v2 design system to every built page.

The four page builders emit a page's own inline <style> and nothing else. The
design system was applied once, by hand, in "Unify site design system across all
43 pages" -- which means every later content rebuild silently reverted whatever
it touched back to the pre-v2 look. That is how 34 of 57 pages ended up on the
old Space Grotesk/Inter theme while 23 kept Fraunces.

So the design system is a post-build pass, not a template edit: it runs after any
builder, over every page, and is idempotent. `validate_site.py` fails the build if
a page is missing it, so the revert cannot happen quietly again.

    python3 scripts/apply_design_system.py [--check]
"""

import os
import re
import sys

DOCS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docs")

FONTS = (
    '<link href="https://fonts.googleapis.com/css2?'
    "family=Fraunces:opsz,wght@9..144,600..800&"
    "family=IBM+Plex+Mono:wght@400;500;600&"
    'family=IBM+Plex+Sans:wght@400;500;600;700&display=swap" rel="stylesheet">'
)
SHEET = '<link rel="stylesheet" href="/assets/rf.css">'
TAIL = f"  {FONTS}\n  {SHEET}\n</head>"

# Fonts rf.css explicitly does not use. Builders still request them, so they cost
# a render-blocking round trip for glyphs no rule ever selects.
DEAD_FONTS = re.compile(
    r'[ \t]*<link[^>]*fonts\.googleapis\.com/css2[^>]*(?:Inter:|Space\+Grotesk)[^>]*>\n?'
)
EXISTING_TAIL = re.compile(
    r'[ \t]*<link[^>]*fonts\.googleapis\.com/css2[^>]*Fraunces[^>]*>\n?'
    r'|[ \t]*<link[^>]*href="/assets/rf\.css"[^>]*>\n?'
)


def pages():
    for root, dirs, files in os.walk(DOCS):
        dirs[:] = [d for d in dirs if not d.startswith((".", "_"))]
        if "index.html" in files:
            yield os.path.join(root, "index.html")


def applied(html):
    """True when both tags are present and sit last in the head, so rf.css wins."""
    head = html.split("</head>", 1)[0]
    return FONTS in head and SHEET in head and head.rindex(SHEET) > head.rindex("<style>")


def fix(html):
    html = DEAD_FONTS.sub("", html)
    head, sep, rest = html.partition("</head>")
    if not sep:
        return None
    return EXISTING_TAIL.sub("", head).rstrip("\n") + "\n" + TAIL + rest


def main():
    check = "--check" in sys.argv
    changed, broken = [], []

    for path in sorted(pages()):
        rel = os.path.relpath(path, DOCS).replace(os.sep, "/")
        with open(path, encoding="utf-8") as f:
            html = f.read()

        if applied(html) and not DEAD_FONTS.search(html):
            continue

        if check:
            changed.append(rel)
            continue

        fixed = fix(html)
        if fixed is None:
            broken.append(rel)
            continue
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(fixed)
        changed.append(rel)

    for rel in broken:
        print(f"  no </head>, skipped: {rel}")

    if check:
        if changed:
            print(f"{len(changed)} page(s) missing the design system:")
            for rel in changed:
                print("  " + rel)
            return 1
        print("design system applied on every page")
        return 0

    print(f"{len(changed)} page(s) updated")
    for rel in changed:
        print("  " + rel)
    return 1 if broken else 0


if __name__ == "__main__":
    sys.exit(main())
