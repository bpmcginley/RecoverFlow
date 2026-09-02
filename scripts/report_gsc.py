#!/usr/bin/env python3
"""Turn the newest seo/gsc/*.json into the report Step 2 of the routine asks for.

Step 2 was done by hand every run until now, and that is why a real signal sat
unread in two consecutive files: on the exact decline code queries the site
returns two pages, the guide and the lookup tool, and neither one wins. Nobody
went looking for it because nobody was looking at anything except the striking
distance table.

Read only. This prints, it never writes, and it never fails a build. It answers
the three questions Step 2 asks, in the order Step 2 asks them, plus the split
above.

    python3 scripts/report_gsc.py            # newest file in seo/gsc/
    python3 scripts/report_gsc.py FILE.json  # a specific one

One thing it deliberately refuses to do: print week over week position deltas
when the two files it is comparing cover mostly the same days. The fetch pulls a
28 day window, so two runs a week apart overlap by 26 days and every delta is
noise dressed up as a number. It measures the overlap and says so instead.
"""

import os
import sys
import json
import glob
import datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GSC = os.path.join(ROOT, "seo", "gsc")
SITE = "https://recoverflow.org"

# Position 5 to 20 is where the routine says the return is: the page already
# ranks, so only the click is missing. Below 20 a metadata rewrite has no
# ranking to convert and is a change with a downside and no upside.
NEAR_TOP, NEAR_BOTTOM = 5.0, 20.0
# Fewer impressions than this and the ceiling is a rounding error either way.
FLOOR = 3


def path_of(url):
    return url.replace(SITE, "") or "/"


def load(name):
    with open(name, encoding="utf-8") as f:
        return json.load(f)


def days(rng):
    start = datetime.date.fromisoformat(rng["start"])
    end = datetime.date.fromisoformat(rng["end"])
    return {start + datetime.timedelta(n) for n in range((end - start).days + 1)}


def table(rows, headers):
    if not rows:
        return
    widths = [max(len(str(r[i])) for r in [headers] + rows) for i in range(len(headers))]
    print("  " + "  ".join(h.ljust(w) for h, w in zip(headers, widths)).rstrip())
    print("  " + "  ".join("-" * w for w in widths))
    for r in rows:
        print("  " + "  ".join(str(c).ljust(w) for c, w in zip(r, widths)).rstrip())


files = sorted(glob.glob(os.path.join(GSC, "*.json")))
if len(sys.argv) > 1:
    newest, previous = sys.argv[1], None
elif not files:
    print("seo/gsc/ holds no data. Steps 1, 4d and 5 need none of it; carry on.")
    sys.exit(0)
else:
    newest = files[-1]
    previous = files[-2] if len(files) > 1 else None

d = load(newest)
rng = d["range"]
window = days(rng)
impressions = sum(q["impressions"] for q in d["queries"])
clicks = sum(q["clicks"] for q in d["queries"])

print(f"{os.path.relpath(newest, ROOT)}  ({d['source']}, fetched {d['fetched']})")
print(f"{rng['start']} to {rng['end']}, {len(window)} days")
print(f"{impressions} impressions, {clicks} clicks, "
      f"{len(d['queries'])} queries, {len(d['pages'])} pages")

age = (datetime.date.today() - datetime.date.fromisoformat(d["fetched"])).days
if age > 30:
    print(f"\nSTALE: this file is {age} days old. Say so once in the report and continue.")

# 1. The cheapest possible win, and the reason Step 2 exists.
print(f"\n== Ranks {NEAR_TOP:.0f} to {NEAR_BOTTOM:.0f}, losing the click ==")
print("   The page ranks and the result is not being clicked. Usually the title")
print("   or the description answers a different question than the query asks.")
near = [r for r in d["query_pages"]
        if NEAR_TOP <= r["position"] <= NEAR_BOTTOM and r["impressions"] >= FLOOR]
near.sort(key=lambda r: -r["impressions"])
table([[r["impressions"], r["clicks"], f"{r['position']:.1f}", r["query"], path_of(r["page"])]
       for r in near], ["impr", "clk", "pos", "query", "page"])
if not near:
    print("  nothing above the floor")

# 2. One query, several of our pages. Google is unsure which one answers it, so
#    it splits the impressions and both land lower than either would alone.
print("\n== One query, more than one of our pages ==")
print("   A split is not automatically a problem. It is worth reading when the")
print("   weaker page takes impressions off a page that ranks materially better.")
split = {}
for r in d["query_pages"]:
    split.setdefault(r["query"], []).append(r)
split = {q: rs for q, rs in split.items()
         if len(rs) > 1 and sum(r["impressions"] for r in rs) >= FLOOR}
if not split:
    print("  none")
for q, rs in sorted(split.items(), key=lambda kv: -sum(r["impressions"] for r in kv[1])):
    rs.sort(key=lambda r: r["position"])
    total = sum(r["impressions"] for r in rs)
    best = rs[0]
    busiest = max(rs, key=lambda r: r["impressions"])
    # The case that costs something: the page Google shows most often for this
    # query is not the page that ranks best for it, and the two are well apart.
    # Comparing against the worst ranked page instead would miss the split on
    # transaction_not_allowed, where the page doing the damage is in the middle.
    flag = ""
    if busiest is not best and busiest["position"] - best["position"] >= 10:
        flag = "   <- the weaker page is taking the impressions"
    print(f"\n  {q!r}  {total} impressions{flag}")
    table([[r["impressions"], r["clicks"], f"{r['position']:.1f}", path_of(r["page"])]
           for r in rs], ["impr", "clk", "pos", "page"])

# 3. Impressions arriving on nothing that ranks. Article candidates live here,
#    but so do head terms we have no business chasing, so this needs judgement.
print("\n== Impressions, but nothing of ours ranks ==")
print("   Article candidates hide here, and so do commercial head terms where a")
print("   new page changes nothing. Read the query before believing the number.")
best = {}
for r in d["query_pages"]:
    if r["query"] not in best or r["position"] < best[r["query"]]["position"]:
        best[r["query"]] = r
deep = [r for r in best.values() if r["position"] > NEAR_BOTTOM and r["impressions"] >= FLOOR]
deep.sort(key=lambda r: -r["impressions"])
table([[r["impressions"], f"{r['position']:.1f}", r["query"], path_of(r["page"])]
       for r in deep[:20]], ["impr", "best pos", "query", "closest page"])
if not deep:
    print("  none")

# 4. The comparison, if the two files can honestly support one.
print("\n== Against the previous file ==")
if not previous:
    print("  nothing to compare against")
else:
    p = load(previous)
    shared = window & days(p["range"])
    overlap = len(shared) / len(window)
    print(f"  {os.path.relpath(previous, ROOT)}: {p['range']['start']} to {p['range']['end']}")
    print(f"  {len(shared)} of {len(window)} days are shared, {overlap:.0%} overlap")
    if overlap > 0.5:
        print("\n  Too much overlap to compare. Most of the movement a delta would")
        print("  show here is the same days counted twice, not a change in rank.")
        print("  Compare files four or more weeks apart, or shorten the fetch")
        print("  window so consecutive files do not overlap.")
    else:
        was = {(r["query"], r["page"]): r for r in p["query_pages"]}
        moved = []
        for r in d["query_pages"]:
            old = was.get((r["query"], r["page"]))
            if old and r["impressions"] >= FLOOR and abs(r["position"] - old["position"]) >= 1.0:
                moved.append((r["position"] - old["position"], old, r))
        moved.sort(key=lambda m: -m[0])
        table([[f"{delta:+.1f}", f"{old['position']:.1f}", f"{new['position']:.1f}",
                new["impressions"], new["query"], path_of(new["page"])]
               for delta, old, new in moved],
              ["moved", "was", "now", "impr", "query", "page"])
        if not moved:
            print("  nothing moved by a full position")
