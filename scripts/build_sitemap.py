#!/usr/bin/env python3
"""Generate docs/sitemap.xml from what is actually on disk.

Walking the filesystem rather than maintaining a list by hand: an earlier
hand-edited sitemap silently dropped /tools/ because of a substring bug, and
a page that is not in the sitemap may as well not exist.
"""

import os
import datetime
import subprocess

SITE = "https://recoverflow.org"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(ROOT, "docs")

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


def git(*args):
    """Run a git command in the repo, or return None if git cannot answer."""
    try:
        out = subprocess.run(("git", "-C", ROOT) + args, capture_output=True,
                             text=True, encoding="utf-8", check=True)
    except (OSError, subprocess.CalledProcessError):
        return None
    return out.stdout


def last_modified(today):
    """Map each page path to the date its index.html last actually changed.

    Stamping every URL with the build date told search engines all 61 pages
    changed every time anyone ran this script, which is the fastest way to get
    lastmod ignored entirely. The commit date of the file is the real answer.
    A page with uncommitted edits changed now, and anything git cannot account
    for falls back to today, which is the old behaviour for that page alone.
    """
    dates = {}
    log = git("log", "--format=%cs", "--name-only", "--no-renames", "--", "docs")
    if log is None:
        return dates
    date = None
    for line in log.splitlines():
        line = line.strip()
        if not line:
            continue
        if len(line) == 10 and line[4] == "-" and line[7] == "-":
            date = line
        elif line.endswith("/index.html"):
            # Log is newest first, so the first date a path appears under wins.
            dates.setdefault(line, date)

    # Staged, unstaged or untracked pages are changing right now.
    status = git("status", "--porcelain", "--", "docs") or ""
    for line in status.splitlines():
        path = line[3:].strip().strip('"')
        if path.endswith("/index.html"):
            dates[path] = today

    return dates


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
    dates = last_modified(today)
    for p in paths:
        pri, freq = rank(p)
        lastmod = dates.get(f"docs/{p}index.html", today)
        lines += ["  <url>",
                  f"    <loc>{SITE}/{p}</loc>",
                  f"    <lastmod>{lastmod}</lastmod>",
                  f"    <changefreq>{freq}</changefreq>",
                  f"    <priority>{pri}</priority>",
                  "  </url>"]
    lines.append("</urlset>")

    with open(os.path.join(DOCS, "sitemap.xml"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"sitemap.xml: {len(paths)} URLs")
