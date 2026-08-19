#!/usr/bin/env python3
"""Run one outbound cycle unattended, from cron or Task Scheduler.

A thin wrapper, deliberately. It decides whether today is a day worth waking Claude for,
and refuses to wake it for a day that would only end in a refusal:

    nothing due            -> exit 0, no Claude run, one line in the log
    preflight fails        -> exit 1, no Claude run, the reason in the log
    otherwise              -> hand the cycle to `claude -p` and let the skill work

The gate matters more than the convenience. `preflight` is what stands between an unattended
run and mail that carries no postal address, or a second batch on top of a spent daily cap.
Checking it here means a broken day costs a log line instead of a send.

Usage:
    python3 growth/scripts/run_outbound.py [--dry-run]

Environment:
    CLAUDE_BIN   path to the claude CLI (default: "claude")
    GROWTH_LOG   where to append run history (default: growth/outbound.log, gitignored)
"""

import argparse
import datetime as dt
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PIPELINE = ROOT / "growth" / "scripts" / "pipeline.py"
LEDGER = ROOT / "growth" / "pipeline.csv"
LOG = Path(os.environ.get("GROWTH_LOG") or ROOT / "growth" / "outbound.log")

# The cycle the skill runs. Named rather than described so the skill, not this file, stays
# the authority on what an outbound cycle actually does.
PROMPT = "Run the growth skill's outbound cycle for today."


def log(line):
    stamp = dt.datetime.now().strftime("%Y-%m-%d %H:%M")
    entry = f"{stamp}  {line}"
    print(entry)
    try:
        with LOG.open("a", encoding="utf-8") as f:
            f.write(entry + "\n")
    except OSError as e:
        print(f"(could not write {LOG}: {e})", file=sys.stderr)


def pipeline(*args):
    return subprocess.run([sys.executable, str(PIPELINE), *args],
                          capture_output=True, text=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dry-run", action="store_true",
                    help="run the checks and report, but do not invoke Claude")
    args = ap.parse_args()

    # The ledger is gitignored and lives only on Bruce's machine, so its absence is the
    # signal that this is a fresh clone (a cloud or CI session) rather than a broken install.
    # Sending is impossible there and reconstructing the ledger would be worse than not
    # running, so say which machine this needs and stop.
    if not LEDGER.exists():
        log(f"SKIP  no ledger at {LEDGER}. This cycle only runs where the ledger lives, "
            "which is Bruce's own machine, not a fresh clone.")
        return 1

    due = pipeline("due")
    if due.returncode != 0:
        log(f"FAIL  due: {due.stderr.strip() or due.stdout.strip()}")
        return 1
    if "Nothing due" in due.stdout:
        log("OK    nothing due today. No send, no Claude run.")
        return 0

    pre = pipeline("preflight")
    if pre.returncode != 0:
        detail = " | ".join(l.strip() for l in (pre.stdout + pre.stderr).splitlines()
                            if "FAIL" in l) or pre.stderr.strip()
        log(f"BLOCKED  preflight refused the batch: {detail}")
        return 1

    counts = " | ".join(l.strip() for l in pre.stdout.splitlines() if l.strip().startswith("ok"))
    if args.dry_run:
        log(f"DRY-RUN  would send. {counts}")
        return 0

    log(f"RUN   handing today's cycle to Claude. {counts}")
    claude = subprocess.run([os.environ.get("CLAUDE_BIN", "claude"), "-p", PROMPT],
                            cwd=ROOT, capture_output=True, text=True)
    if claude.returncode != 0:
        log(f"FAIL  claude exited {claude.returncode}: {claude.stderr.strip()[:400]}")
        return claude.returncode

    log("DONE  cycle finished. Ledger is the record of what actually sent.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
