#!/usr/bin/env python3
"""RecoverFlow outbound pipeline.

The ledger at growth/pipeline.csv is the single source of truth for who has been
contacted and what happened. This script turns that ledger into a deterministic
daily queue, so continuing the pipeline never depends on anyone remembering
where it was left.

    python3 growth/scripts/pipeline.py due
    python3 growth/scripts/pipeline.py render --email chris@loops.so
    python3 growth/scripts/pipeline.py log --email chris@loops.so --event t2_sent
    python3 growth/scripts/pipeline.py add --email a@b.com --first-name Ann --company Acme
    python3 growth/scripts/pipeline.py stats
    python3 growth/scripts/pipeline.py validate

Stdlib only, no install step.
"""

import argparse
import csv
import datetime as dt
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
LEDGER = ROOT / "growth" / "pipeline.csv"
SEQUENCES = ROOT / "growth" / "sequences"

FIELDS = [
    "email", "first_name", "company", "domain", "track", "source", "status",
    "t1_date", "t2_date", "t3_date", "reply_date", "outcome", "thread_id", "notes",
]

# Business days to wait before the next touch. Short enough to stay in memory,
# long enough not to read as pestering.
CADENCE = {"t1_sent": ("t2", 3), "t2_sent": ("t3", 5), "t3_sent": ("close", 5)}

IN_FLOW = ["sourced", "t1_sent", "t2_sent", "t3_sent"]
TERMINAL = ["replied", "bounced", "disqualified", "closed", "connected", "customer"]
CONTACTED = ["t1_sent", "t2_sent", "t3_sent"] + TERMINAL

# One Gmail mailbox sending cold. Past roughly this many a day, deliverability
# and personalisation quality both fall off.
SEND_CAP_PER_DAY = 10


def today():
    return dt.date.today()


def parse_date(s):
    return dt.datetime.strptime(s.strip(), "%Y-%m-%d").date() if s and s.strip() else None


def add_business_days(start, n):
    d, added = start, 0
    while added < n:
        d += dt.timedelta(days=1)
        if d.weekday() < 5:
            added += 1
    return d


def next_send_day(d):
    """Never queue a send onto a weekend."""
    while d.weekday() >= 5:
        d += dt.timedelta(days=1)
    return d


def load():
    if not LEDGER.exists():
        sys.exit(f"No ledger at {LEDGER}")
    with LEDGER.open(newline="", encoding="utf-8") as f:
        return [dict(r) for r in csv.DictReader(f)]


def save(rows):
    with LEDGER.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS)
        w.writeheader()
        for r in rows:
            w.writerow({k: (r.get(k) or "") for k in FIELDS})


def find(rows, email):
    email = email.strip().lower()
    for r in rows:
        if r["email"].strip().lower() == email:
            return r
    return None


def due_items(rows, on):
    """Everything owed a touch on or before `on`, oldest debt first."""
    out = []
    for r in rows:
        status = r["status"].strip()
        if status not in IN_FLOW:
            continue
        if status == "sourced":
            out.append((on, "t1", r))
            continue
        touch, wait = CADENCE[status]
        sent = parse_date(r[f"t{status[1]}_date"])
        if not sent:
            continue
        when = next_send_day(add_business_days(sent, wait))
        if when <= on:
            out.append((when, touch, r))
    out.sort(key=lambda x: (x[0], x[2]["company"].lower()))
    return out


def signature():
    path = ROOT / "growth" / "sender.txt"
    return path.read_text(encoding="utf-8").rstrip() if path.exists() else ""


def compliance_problem():
    """Why the rendered footer is unfit to send, or None if it is fine.

    CAN-SPAM requires a valid physical postal address and a working opt-out on every
    commercial email, and both live in sender.txt. A missing file used to fail open:
    signature() returned "", render() substituted nothing, and the TODO check could not
    match an empty string, so the mail went out carrying neither and said nothing about it.
    That was survivable while a human read every draft before sending. It is not survivable
    now that the loop can send on its own, so the states are named and returned instead.
    """
    path = ROOT / "growth" / "sender.txt"
    if not path.exists():
        return (f"{path} does not exist, so every rendered email would carry no postal "
                "address and no opt-out at all.")
    sig = signature()
    if not sig.strip():
        return (f"{path} is empty, so every rendered email would carry no postal address "
                "and no opt-out at all.")
    if "TODO-POSTAL-ADDRESS" in sig:
        return (f"{path} still says TODO-POSTAL-ADDRESS. A PO box or registered-agent "
                "address satisfies the requirement; a home address is not required.")
    return None


def sent_today(rows, on):
    """How many touches already went out on `on`.

    The cap is per day, not per run. Without this a second run on the same day starts its
    count again from zero and sends another full batch on top of the first.
    """
    return sum(1 for r in rows for n in (1, 2, 3) if parse_date(r[f"t{n}_date"]) == on)


def render(row, touch):
    track = (row.get("track") or "prospect").strip()
    candidates = [SEQUENCES / f"{touch}-{track}.txt", SEQUENCES / f"{touch}.txt"]
    for path in candidates:
        if path.exists():
            body = path.read_text(encoding="utf-8")
            break
    else:
        sys.exit(f"No template for touch={touch} track={track} in {SEQUENCES}")
    body = body.replace("{signature}", signature())
    for key in ("first_name", "company", "domain"):
        body = body.replace("{" + key + "}", row.get(key, "") or "")
    return body


def scheduled_dates(rows):
    """When each in-flight prospect next comes due."""
    out = []
    for r in rows:
        status = r["status"].strip()
        if status not in CADENCE:
            continue
        sent = parse_date(r[f"t{status[1]}_date"])
        if sent:
            out.append(next_send_day(add_business_days(sent, CADENCE[status][1])))
    return sorted(out)


def cmd_due(args):
    on = parse_date(args.date) if args.date else today()
    rows = load()
    items = due_items(rows, on)
    if not items:
        print(f"Nothing due on {on} ({on.strftime('%A')}).")
        upcoming = [d for d in scheduled_dates(rows) if d > on]
        if upcoming:
            when = upcoming[0]
            n = sum(1 for d in upcoming if d == when)
            print(f"Next: {n} prospect(s) on {when} ({when.strftime('%A')}).")
        return

    print(f"Due on {on} ({on.strftime('%A')}): {len(items)}\n")
    for when, touch, r in items:
        overdue = (on - when).days
        flag = f"  [{overdue}d overdue]" if overdue > 0 else ""
        print(f"  {touch.upper():5} {r['first_name']:10} {r['company']:18} {r['email']}{flag}")
    if len(items) > SEND_CAP_PER_DAY:
        print(f"\n  ! {len(items)} exceeds the {SEND_CAP_PER_DAY}/day cap. Send the top "
              f"{SEND_CAP_PER_DAY} today, the rest tomorrow.")
    if on.weekday() == 4:
        print("\n  Note: Friday. Replies to cold email drop off over a weekend; "
              "Monday-Thursday lands better if it can wait.")
    print("\n  Copy:  python3 growth/scripts/pipeline.py render --email <email>")


def cmd_render(args):
    rows = load()
    row = find(rows, args.email)
    if not row:
        sys.exit(f"{args.email} is not in the ledger.")
    touch = args.touch
    if not touch:
        status = row["status"].strip()
        if status == "sourced":
            touch = "t1"
        elif status in CADENCE:
            touch = CADENCE[status][0]
        else:
            sys.exit(f"{args.email} is {status}; no touch owed.")
    if touch == "close":
        sys.exit(f"{args.email} is owed a close, not a send. Use: log --event closed")
    print(render(row, touch))
    problem = compliance_problem()
    if problem:
        print(f"\n  ! Not fit to send. {problem}\n"
              "    CAN-SPAM requires a valid physical postal address and a working opt-out "
              "on commercial email.", file=sys.stderr)


def cmd_preflight(args):
    """Everything that has to be true before an unattended run may send anything.

    Each check here used to be a warning that a person was trusted to read on their way to
    the send button. Unattended, a warning nobody reads is the same as no check at all, so
    these exit non-zero and the caller sends nothing.
    """
    on = parse_date(args.date) if args.date else today()
    rows = load()
    failures, notes = [], []

    problem = compliance_problem()
    if problem:
        failures.append(f"CAN-SPAM: {problem}")
    else:
        notes.append("Footer carries a postal address and an opt-out.")

    already = sent_today(rows, on)
    budget = SEND_CAP_PER_DAY - already
    if already:
        notes.append(f"{already} already sent today; {max(budget, 0)} left of the "
                     f"{SEND_CAP_PER_DAY}/day cap.")
    if budget <= 0:
        failures.append(f"The {SEND_CAP_PER_DAY}/day cap is already spent ({already} sent "
                        f"on {on}). Nothing more may go out today.")

    items = due_items(rows, on)[:max(budget, 0)]
    for _, touch, r in items:
        track = (r.get("track") or "prospect").strip()
        if not any((SEQUENCES / n).exists()
                   for n in (f"{touch}-{track}.txt", f"{touch}.txt")):
            failures.append(f"No {touch} template for track={track} ({r['email']}) "
                            f"in {SEQUENCES}.")
        if touch != "t1" and not (r.get("thread_id") or "").strip():
            failures.append(f"{r['email']} is owed {touch} but has no thread_id, so the "
                            "follow-up would start a new thread instead of replying.")

    if on.weekday() >= 5:
        failures.append(f"{on} is a {on.strftime('%A')}. Cold sends go Monday to Thursday.")

    for n in notes:
        print(f"  ok    {n}")
    print(f"  ok    {len(items)} to send on {on}." if items and not failures
          else f"  --    {len(items)} due on {on}.")
    for f in failures:
        print(f"  FAIL  {f}", file=sys.stderr)
    if failures:
        sys.exit(f"\nPreflight failed on {len(failures)} check(s). Send nothing.")
    print("\nPreflight passed. Send one at a time, logging each before the next.")


def cmd_log(args):
    rows = load()
    row = find(rows, args.email)
    if not row:
        sys.exit(f"{args.email} is not in the ledger.")
    when = (parse_date(args.date) if args.date else today()).isoformat()
    event = args.event

    if event in ("t1_sent", "t2_sent", "t3_sent"):
        row[f"t{event[1]}_date"] = when
        row["status"] = event
        # T1 opens the thread every later touch has to reply on. Nothing could write this
        # before, so an automatically sent T1 would strand its own follow-ups: preflight
        # would then refuse the T2 for want of a thread_id and the sequence would stall.
        if args.thread_id:
            row["thread_id"] = args.thread_id.strip()
    elif event == "replied":
        row["reply_date"] = when
        row["status"] = "replied"
        row["outcome"] = args.outcome or "replied"
    else:
        row["status"] = event
        row["outcome"] = args.outcome or event
        if event in ("connected", "customer", "bounced"):
            row["reply_date"] = row["reply_date"] or when
    if args.note:
        row["notes"] = (row["notes"] + "; " if row["notes"] else "") + args.note
    save(rows)
    print(f"{args.email} -> {row['status']}" + (f" ({row['outcome']})" if row["outcome"] else ""))


def cmd_add(args):
    rows = load()
    if find(rows, args.email):
        sys.exit(f"{args.email} is already in the ledger. Refusing to duplicate.")
    rows.append({
        "email": args.email.strip().lower(), "first_name": args.first_name,
        "company": args.company, "domain": args.domain or args.email.split("@")[-1],
        "track": args.track, "source": args.source, "status": "sourced",
        "t1_date": "", "t2_date": "", "t3_date": "", "reply_date": "",
        "outcome": "", "thread_id": "", "notes": args.note or "",
    })
    save(rows)
    print(f"Added {args.email} ({args.company}) as sourced.")


def cmd_stats(args):
    rows = load()
    by_status = {}
    for r in rows:
        by_status[r["status"]] = by_status.get(r["status"], 0) + 1

    contacted = [r for r in rows if r["status"] in CONTACTED]
    replied = [r for r in rows if r["reply_date"].strip()]
    connected = [r for r in rows if r["status"] in ("connected", "customer")]

    print(f"Ledger: {len(rows)} prospects\n")
    for s in IN_FLOW + TERMINAL:
        if by_status.get(s):
            print(f"  {s:14} {by_status[s]}")

    print(f"\n  contacted      {len(contacted)}")
    if contacted:
        print(f"  replied        {len(replied)}  ({len(replied) / len(contacted):.0%})")
        print(f"  connected      {len(connected)}  ({len(connected) / len(contacted):.0%})")

    stalled = [r for r in rows if r["status"] == "t1_sent" and not r["t2_date"].strip()]
    if stalled:
        print(f"\n  {len(stalled)} sat at one touch with no follow-up. Reply rates roughly "
              f"double across a full sequence; single-touch is the most common way to "
              f"read a list as dead when it is not.")

    by_source = {}
    for r in contacted:
        k = r["source"] or "unknown"
        agg = by_source.setdefault(k, [0, 0])
        agg[0] += 1
        if r["reply_date"].strip():
            agg[1] += 1
    if len(by_source) > 1:
        print("\n  by source:")
        for k, (n, rep) in sorted(by_source.items()):
            print(f"    {k:12} {n:3} contacted, {rep} replied")


def cmd_validate(args):
    rows, problems, warnings, seen = load(), [], [], set()
    for i, r in enumerate(rows, start=2):
        who = r.get("email", f"row {i}")
        if not r.get("email", "").strip() or "@" not in r.get("email", ""):
            problems.append(f"row {i}: missing or malformed email")
            continue
        key = r["email"].strip().lower()
        if key in seen:
            problems.append(f"{who}: duplicate")
        seen.add(key)
        if r["status"] not in IN_FLOW + TERMINAL:
            problems.append(f"{who}: unknown status '{r['status']}'")
        for col in ("t1_date", "t2_date", "t3_date", "reply_date"):
            if r[col].strip():
                try:
                    parse_date(r[col])
                except ValueError:
                    problems.append(f"{who}: {col} is not YYYY-MM-DD")
        if r["status"] in ("t2_sent", "t3_sent") and not r["t1_date"].strip():
            problems.append(f"{who}: {r['status']} with no t1_date")
        if not r.get("first_name", "").strip():
            problems.append(f"{who}: no first_name, templates will render blank")
        if r["status"] in ("t1_sent", "t2_sent", "t3_sent") and not r.get("thread_id", "").strip():
            warnings.append(f"{who}: contacted but no thread_id, follow-ups cannot reply in thread")

    if "TODO-POSTAL-ADDRESS" in signature():
        warnings.append("sender.txt has no postal address; CAN-SPAM requires one on every send")

    if problems:
        print(f"{len(problems)} problem(s):")
        for p in problems:
            print(f"  - {p}")
        sys.exit(1)
    print(f"Ledger OK: {len(rows)} rows, no duplicates, states and dates consistent.")
    for w in warnings:
        print(f"  warning: {w}")


def main():
    p = argparse.ArgumentParser(description="RecoverFlow outbound pipeline")
    sub = p.add_subparsers(dest="cmd", required=True)

    d = sub.add_parser("due", help="who is owed a touch")
    d.add_argument("--date", help="YYYY-MM-DD, defaults to today")
    d.set_defaults(func=cmd_due)

    pf = sub.add_parser("preflight", help="gate an unattended run before it sends")
    pf.add_argument("--date", help="YYYY-MM-DD, defaults to today")
    pf.set_defaults(func=cmd_preflight)

    r = sub.add_parser("render", help="print the copy for a prospect's next touch")
    r.add_argument("--email", required=True)
    r.add_argument("--touch", choices=["t1", "t2", "t3"])
    r.set_defaults(func=cmd_render)

    l = sub.add_parser("log", help="record what happened")
    l.add_argument("--email", required=True)
    l.add_argument("--event", required=True,
                   choices=["t1_sent", "t2_sent", "t3_sent", "replied", "bounced",
                            "disqualified", "closed", "connected", "customer"])
    l.add_argument("--date", help="YYYY-MM-DD, defaults to today")
    l.add_argument("--thread-id", dest="thread_id",
                   help="thread the send opened; record it on t1_sent so follow-ups reply "
                        "on the same thread")
    l.add_argument("--outcome", help="free text, e.g. positive / not-now / no")
    l.add_argument("--note")
    l.set_defaults(func=cmd_log)

    a = sub.add_parser("add", help="add a sourced prospect")
    a.add_argument("--email", required=True)
    a.add_argument("--first-name", required=True, dest="first_name")
    a.add_argument("--company", required=True)
    a.add_argument("--domain")
    a.add_argument("--track", default="prospect",
                   choices=["prospect", "peer", "competitor"])
    a.add_argument("--source", default="apollo")
    a.add_argument("--note")
    a.set_defaults(func=cmd_add)

    s = sub.add_parser("stats", help="funnel numbers")
    s.set_defaults(func=cmd_stats)

    v = sub.add_parser("validate", help="check ledger integrity")
    v.set_defaults(func=cmd_validate)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
