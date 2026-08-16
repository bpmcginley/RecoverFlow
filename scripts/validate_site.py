#!/usr/bin/env python3
"""Pre-deploy checks on docs/. Exits non-zero if anything is wrong.

Catches the failures that have actually happened on this site: internal links
to pages that were never built, JSON-LD that stopped parsing after an edit,
sitemap drift, and em dashes (a house style rule, not a nit).
"""

import os
import re
import sys
import json

DOCS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docs")
SITE = "https://recoverflow.org"

pages, problems = {}, []


def note(path, msg):
    problems.append(f"{path}: {msg}")


for root, dirs, files in os.walk(DOCS):
    dirs[:] = [d for d in dirs if not d.startswith((".", "_"))]
    if "index.html" in files:
        rel = os.path.relpath(root, DOCS).replace(os.sep, "/")
        key = "/" if rel == "." else f"/{rel}/"
        with open(os.path.join(root, "index.html"), encoding="utf-8") as f:
            pages[key] = f.read()

for path, html in sorted(pages.items()):
    # Internal links must resolve to a page we actually built.
    for href in re.findall(r'href="(/[^"#?]*)"', html):
        target = href if href.endswith("/") else href + "/"
        if target not in pages and not re.search(r"\.(xml|txt|ico|png|svg|css|js)$", href):
            note(path, f"broken internal link -> {href}")

    for block in re.findall(r'<script type="application/ld\+json">(.*?)</script>', html, re.S):
        try:
            json.loads(block)
        except json.JSONDecodeError as e:
            note(path, f"JSON-LD does not parse: {e}")

    if "—" in html or "&mdash;" in html.split('class="sources"')[0]:
        note(path, "em dash in page body")

    # The builders emit each page's inline <style> and stop there, so a content
    # rebuild drops the v2 design system unless apply_design_system.py runs after
    # it. That reverted 34 of 57 pages once already, silently.
    if '<link rel="stylesheet" href="/assets/rf.css">' not in html.split("</head>")[0]:
        note(path, "missing the v2 design system, run scripts/apply_design_system.py")
    elif "Fraunces" not in html.split("</head>")[0]:
        note(path, "loads rf.css without its fonts, run scripts/apply_design_system.py")

    if re.search(r"fonts\.googleapis\.com/css2[^\"']*(?:Inter:|Space\+Grotesk)", html):
        note(path, "requests a font rf.css does not use")

    if not re.search(r'<link rel="canonical"', html):
        note(path, "missing canonical")
    if not re.search(r'<meta name="description"', html):
        note(path, "missing meta description")

    for desc in re.findall(r'<meta name="description" content="([^"]*)"', html):
        if len(desc) > 165:
            note(path, f"meta description {len(desc)} chars, will truncate in search results")

    for tag in ("main", "article", "table", "details"):
        o, c = len(re.findall(rf"<{tag}[\s>]", html)), len(re.findall(rf"</{tag}>", html))
        if o != c:
            note(path, f"unbalanced <{tag}>: {o} open, {c} close")

# Sitemap must match the filesystem exactly, in both directions.
sm_path = os.path.join(DOCS, "sitemap.xml")
if os.path.exists(sm_path):
    with open(sm_path, encoding="utf-8") as f:
        listed = {u.replace(SITE, "") or "/" for u in re.findall(r"<loc>([^<]+)</loc>", f.read())}
    for missing in sorted(set(pages) - listed):
        note("sitemap.xml", f"page not listed: {missing}")
    for extra in sorted(listed - set(pages)):
        note("sitemap.xml", f"lists a page that does not exist: {extra}")

print(f"{len(pages)} pages checked")
if problems:
    print(f"\n{len(problems)} problem(s):")
    for p in problems:
        print("  " + p)
    sys.exit(1)
print("all clean")
