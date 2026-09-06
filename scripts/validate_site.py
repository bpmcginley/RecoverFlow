#!/usr/bin/env python3
"""Pre-deploy checks on docs/. Exits non-zero if anything is wrong.

Catches the failures that have actually happened on this site: internal links
to pages that were never built, JSON-LD that stopped parsing after an edit,
sitemap drift, and em dashes (a house style rule, not a nit).

Two registers. A problem is something broken and exits non-zero. A warning is
something that costs clicks but ships fine, is printed with a count so it is
visible in CI and in Step 1 of the weekly routine, and does not fail the build.
Titles are the only warning today: 12 pages carry one too long to survive a
search result intact, and failing on all 12 would force twelve unreviewed prose
rewrites in one week, which is the opposite of what the routine asks for.
"""

import os
import re
import sys
import json

DOCS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docs")
SITE = "https://recoverflow.org"

from html import unescape as html_unescape

pages, problems, warnings = {}, [], []
# Two pages sharing a title or a description are competing for the same result,
# and neither wins. Nothing shares one today; this keeps it that way.
titles, descs = {}, {}


def note(path, msg):
    problems.append(f"{path}: {msg}")


def warn(path, msg):
    warnings.append(f"{path}: {msg}")


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
        # json and csv are here for the published datasets under /data/, which are
        # real files with no index.html and so are not "pages".
        if target not in pages and not re.search(r"\.(xml|txt|ico|png|svg|css|js|json|csv)$", href):
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

    # The title and the description are the two lines a searcher actually reads,
    # and only one of them was ever measured here. A missing title is broken. A
    # long one is not broken, it is just cut off, so it warns.
    title = re.search(r"<title>(.*?)</title>", html, re.S)
    if not title:
        note(path, "missing title")
    else:
        text = html_unescape(title.group(1)).strip()
        titles.setdefault(text, []).append(path)
        # Google drops or rewrites the brand suffix often enough that counting it
        # overstates the problem, so measure what is left of the title without it.
        core = text.rsplit("|", 1)[0].strip() if "|" in text else text
        if len(core) > 60:
            warn(path, f"title {len(core)} chars before the brand suffix, will truncate")

    for desc in re.findall(r'<meta name="description" content="([^"]*)"', html):
        descs.setdefault(html_unescape(desc).strip(), []).append(path)

    for tag in ("main", "article", "table", "details"):
        o, c = len(re.findall(rf"<{tag}[\s>]", html)), len(re.findall(rf"</{tag}>", html))
        if o != c:
            note(path, f"unbalanced <{tag}>: {o} open, {c} close")

for text, where in sorted(titles.items()):
    if len(where) > 1:
        note(where[0], f"shares its title with {', '.join(where[1:])}")
for text, where in sorted(descs.items()):
    if len(where) > 1:
        note(where[0], f"shares its meta description with {', '.join(where[1:])}")

# Sitemap must match the filesystem exactly, in both directions.
sm_path = os.path.join(DOCS, "sitemap.xml")
if os.path.exists(sm_path):
    with open(sm_path, encoding="utf-8") as f:
        listed = {u.replace(SITE, "") or "/" for u in re.findall(r"<loc>([^<]+)</loc>", f.read())}
    for missing in sorted(set(pages) - listed):
        note("sitemap.xml", f"page not listed: {missing}")
    for extra in sorted(listed - set(pages)):
        note("sitemap.xml", f"lists a page that does not exist: {extra}")

# llms.txt is hand-maintained and answer engines are pointed straight at it by
# robots.txt, so a page missing from it is invisible to them while ranking fine
# in search. It had drifted by two pages before anyone looked. Same two-way
# check as the sitemap, except it may also list the published /data/ files,
# which are real URLs with no index.html.
llms_path = os.path.join(DOCS, "llms.txt")
if os.path.exists(llms_path):
    with open(llms_path, encoding="utf-8") as f:
        raw = set(re.findall(rf"{re.escape(SITE)}(/[^\s)\]>,]*)", f.read()))
    files = {u for u in raw if re.search(r"\.(xml|txt|ico|png|svg|css|js|json|csv)$", u)}
    listed = {u if u.endswith("/") else u + "/" for u in raw - files}
    for missing in sorted(set(pages) - listed):
        note("llms.txt", f"page not listed: {missing}")
    for extra in sorted(listed - set(pages)):
        note("llms.txt", f"lists a page that does not exist: {extra}")

print(f"{len(pages)} pages checked")
if warnings:
    print(f"\n{len(warnings)} warning(s), not fatal:")
    for w in warnings:
        print("  " + w)
if problems:
    print(f"\n{len(problems)} problem(s):")
    for p in problems:
        print("  " + p)
    sys.exit(1)
print("all clean" if not warnings else "no problems")
