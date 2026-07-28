#!/usr/bin/env python3
"""Generate docs/sitemap.xml from what is actually on disk.

Walking the filesystem rather than maintaining a list by hand: an earlier
hand-edited sitemap silently dropped /tools/ because of a substring bug, and
a page that is not in the sitemap may as well not exist.
"""

import os
import datetime

SITE = "https://recoverflow.org"
DOCS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docs")

# Longest matching prefix wins, so /blog/ can outrank / without ordering games.
TIERS = [
    ("", 1.0, "weekly"),
    ("pricing/", 0.9, "monthly"),
    ("recover-failed-stripe-payments/", 0.9, "monthly"),
    ("compare/", 0.8, "monthly"),
    ("tools/", 0.8, "monthly"),
    ("blog/", 0.7, "weekly"),
    ("about/", 0.6, "monthly"),
    ("contact/", 0.6, "monthly"),
    ("privacy/", 0.3, "yearly"),
    ("terms/", 0.3, "yearly"),
    ("security/", 0.5, "yearly"),
]


def rank(path):
    best = (0.5, "monthly")
    longest = -1
    for prefix, pri, freq in TIERS:
        if path.startswith(prefix) and len(prefix) > longest:
            longest, best = len(prefix), (pri, freq)
    return best


def discover():
    out = []
    for root, dirs, files in os.walk(DOCS):
        dirs[:] = [d for d in dirs if not d.startswith((".", "_"))]
        if "index.html" not in files:
            continue
        rel = os.path.relpath(root, DOCS).replace(os.sep, "/")
        path = "" if rel == "." else rel + "/"
        out.append(path)
    return sorted(out, key=lambda p: (-rank(p)[0], p))


if __name__ == "__main__":
    today = datetime.date.today().isoformat()
    lines = ['<?xml version="1.0" encoding="UTF-8"?>',
             '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">']
    paths = discover()
    for p in paths:
        pri, freq = rank(p)
        lines += ["  <url>",
                  f"    <loc>{SITE}/{p}</loc>",
                  f"    <lastmod>{today}</lastmod>",
                  f"    <changefreq>{freq}</changefreq>",
                  f"    <priority>{pri}</priority>",
                  "  </url>"]
    lines.append("</urlset>")

    with open(os.path.join(DOCS, "sitemap.xml"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"sitemap.xml: {len(paths)} URLs")
