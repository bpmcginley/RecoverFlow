#!/usr/bin/env python3
"""Retry waste audit: how many retry attempts went to cards that could never succeed.

This is the new positioning made concrete. Every competitor sells MORE retries.
This counts the ones that were never going to work, and the Visa reattempt budget
they burned doing it.

Run it against a Stripe charges CSV export (Payments > export, include the decline
code and card columns). Column names vary between exports, so the loader accepts
the common aliases and tells you what it could not find.

  python retry_waste_audit.py charges.csv
  python retry_waste_audit.py charges.csv --company "Acme"

Everything printed is arithmetic on their data. No recovery-rate assumptions appear
anywhere in this script, deliberately: the whole point of the pitch is that the
number is theirs, not a benchmark we invented.
"""

import argparse
import csv
import io
import sys
from collections import defaultdict, Counter
from datetime import datetime, timedelta

# Verified 28 July 2026 against docs.stripe.com/billing/revenue-recovery/smart-retries
# Stripe keeps the retry schedule running on these and keeps incrementing attempt_count,
# but never sends the charge to the network until a new payment method is attached.
BLOCKED = {
    "incorrect_number", "lost_card", "pickup_card", "stolen_card",
    "revocation_of_authorization", "revocation_of_all_authorizations",
    "authentication_required", "highest_risk_level", "transaction_not_allowed",
}
# Retried by Stripe, but the card itself is unusable until the customer acts.
NEEDS_NEW_CARD = {"expired_card", "incorrect_cvc", "invalid_expiry_month", "invalid_expiry_year"}

# Visa Excessive Reattempts Rule, enforced since April 2022: no more than 15
# reattempts per card per 30 days. Stripe blocks further attempts past this.
# https://support.stripe.com/questions/payment-blocked-due-to-excessive-retries
VISA_CAP = 15
VISA_WINDOW_DAYS = 30

ALIASES = {
    "decline": ["decline_code", "Decline Code", "failure_code", "Failure Code",
                "decline_reason", "Decline Reason", "outcome_reason"],
    "status": ["status", "Status"],
    "created": ["created", "Created", "Created (UTC)", "created_at", "Date"],
    "amount": ["amount", "Amount", "Amount Captured", "amount_captured"],
    "customer": ["customer", "Customer", "customer_id", "Customer ID", "CustomerID"],
    "card": ["card_id", "Card ID", "payment_method", "PaymentMethod ID", "source_id",
             "card_fingerprint", "Card Fingerprint"],
    "brand": ["card_brand", "Card Brand", "brand", "Card Type"],
    "currency": ["currency", "Currency"],
}


def pick(fieldnames, key):
    for a in ALIASES[key]:
        if a in fieldnames:
            return a
    return None


def parse_date(v):
    for fmt in ("%Y-%m-%d %H:%M:%S", "%Y-%m-%d %H:%M", "%Y-%m-%d",
                "%Y-%m-%dT%H:%M:%SZ", "%m/%d/%Y %H:%M", "%m/%d/%Y"):
        try:
            return datetime.strptime(v.strip()[:len(fmt) + 4], fmt)
        except (ValueError, AttributeError):
            continue
    return None


def money(cents, cur="USD"):
    return f"${cents/100:,.0f}" if cur.upper() == "USD" else f"{cents/100:,.0f} {cur.upper()}"


def load(path):
    raw = io.open(path, encoding="utf-8-sig", newline="")
    rdr = csv.DictReader(raw)
    cols = {k: pick(rdr.fieldnames or [], k) for k in ALIASES}
    if not cols["decline"]:
        sys.exit("Could not find a decline-code column. Looked for: "
                 + ", ".join(ALIASES["decline"])
                 + f"\nColumns present: {', '.join(rdr.fieldnames or [])}")
    rows = []
    for r in rdr:
        status = (r.get(cols["status"]) or "").strip().lower()
        if status and status in ("succeeded", "paid", "available"):
            continue  # only failures matter here
        code = (r.get(cols["decline"]) or "").strip().lower()
        if not code:
            continue
        try:
            amt = int(float(r.get(cols["amount"]) or 0))
        except ValueError:
            amt = 0
        # Stripe CSV exports amounts in major units; API uses cents. Normalise up
        # if the column looks like dollars (has a decimal point in the raw text).
        rawamt = (r.get(cols["amount"]) or "")
        if "." in rawamt:
            try:
                amt = int(round(float(rawamt) * 100))
            except ValueError:
                pass
        rows.append({
            "code": code,
            "amount": amt,
            "when": parse_date(r.get(cols["created"]) or "") if cols["created"] else None,
            "customer": (r.get(cols["customer"]) or "").strip() if cols["customer"] else "",
            "card": (r.get(cols["card"]) or "").strip() if cols["card"] else "",
            "brand": (r.get(cols["brand"]) or "").strip().lower() if cols["brand"] else "",
            "currency": (r.get(cols["currency"]) or "usd").strip() if cols["currency"] else "usd",
        })
    return rows, cols


def visa_exposure(rows):
    """Cards at or near Visa's 15-reattempt-per-30-days ceiling."""
    per_card = defaultdict(list)
    for r in rows:
        key = r["card"] or r["customer"]
        if key and r["when"]:
            per_card[key].append(r["when"])
    worst = []
    for key, dates in per_card.items():
        dates.sort()
        peak = 0
        for i, d in enumerate(dates):
            window = [x for x in dates[i:] if x - d <= timedelta(days=VISA_WINDOW_DAYS)]
            peak = max(peak, len(window))
        if peak >= 10:
            worst.append((key, peak))
    return sorted(worst, key=lambda x: -x[1])


def main():
    p = argparse.ArgumentParser()
    p.add_argument("csv")
    p.add_argument("--company", default="this account")
    a = p.parse_args()

    rows, cols = load(a.csv)
    if not rows:
        sys.exit("No failed charges with decline codes found in that file.")

    cur = Counter(r["currency"] for r in rows).most_common(1)[0][0]
    total = len(rows)
    total_amt = sum(r["amount"] for r in rows)

    blocked = [r for r in rows if r["code"] in BLOCKED]
    newcard = [r for r in rows if r["code"] in NEEDS_NEW_CARD]
    retryable = [r for r in rows if r["code"] not in BLOCKED]

    print(f"\nRETRY WASTE AUDIT: {a.company}")
    print("=" * 62)
    print(f"\n{total:,} failed charges, {money(total_amt, cur)} at risk\n")

    print("WHAT COULD NEVER HAVE WORKED")
    print("-" * 62)
    pct = blocked and len(blocked) / total * 100 or 0
    bamt = sum(r["amount"] for r in blocked)
    print(f"  {len(blocked):,} charges ({pct:.0f}%) are on the nine codes Stripe will not")
    print(f"  retry at all without a new card. That is {money(bamt, cur)} that no retry")
    print(f"  schedule, however clever, was ever going to collect.\n")
    if blocked:
        for code, n in Counter(r["code"] for r in blocked).most_common():
            amt = sum(r["amount"] for r in blocked if r["code"] == code)
            print(f"     {code:<34} {n:>5}   {money(amt, cur)}")
    print()

    print("WHAT NEEDED A NEW CARD, NOT ANOTHER ATTEMPT")
    print("-" * 62)
    namt = sum(r["amount"] for r in newcard)
    print(f"  {len(newcard):,} charges ({len(newcard)/total*100:.0f}%), {money(namt, cur)}.")
    print("  Stripe DOES retry these, so they burn attempts, but the card cannot")
    print("  succeed until the customer replaces it.\n")

    reach = len(retryable) - len(newcard)
    print("WHAT WAS ACTUALLY REACHABLE BY RETRYING")
    print("-" * 62)
    print(f"  {reach:,} charges ({reach/total*100:.0f}%). Everything else needed a")
    print("  conversation, not a schedule.\n")

    exposure = visa_exposure(rows)
    print(f"VISA REATTEMPT BUDGET (cap is {VISA_CAP} per card per {VISA_WINDOW_DAYS} days)")
    print("-" * 62)
    if not cols["card"] and not cols["customer"]:
        print("  Skipped: the export had no card or customer column.\n")
    elif not exposure:
        print("  No card reached 10 failed attempts in a 30 day window. Healthy.\n")
    else:
        over = [x for x in exposure if x[1] >= VISA_CAP]
        print(f"  {len(exposure)} card(s) reached 10 or more attempts in 30 days.")
        if over:
            print(f"  {len(over)} of them hit {VISA_CAP} or more, where Stripe starts blocking")
            print("  further attempts outright.")
        for key, n in exposure[:8]:
            flag = "  <-- at or over the cap" if n >= VISA_CAP else ""
            print(f"     {key[:38]:<38} {n:>3} attempts{flag}")
        print()

    print("THE ONE SENTENCE VERSION")
    print("-" * 62)
    waste_pct = (len(blocked) + len(newcard)) / total * 100
    waste_amt = bamt + namt
    print(f"  {waste_pct:.0f}% of {a.company}'s failed revenue ({money(waste_amt, cur)}) sits on")
    print("  cards that needed replacing, not retrying. Any tool that answers this")
    print("  with more retry attempts is solving the wrong half of the problem.\n")


if __name__ == "__main__":
    main()
