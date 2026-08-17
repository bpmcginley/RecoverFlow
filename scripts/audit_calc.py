#!/usr/bin/env python3
"""Work out the numbers for a free /audit reply.

Step 1 uses only what the prospect sent: volume and cost. No recovery-rate
assumptions, because their decline mix is unknown at that point and inventing
a benchmark is the one thing this whole site refuses to do.

Step 2 runs once they send their decline codes back, and applies the same
assumed recovery ranges already published on /docs/backtest, labelled as
assumptions rather than results.

  python audit_calc.py --mrr 40000 --failures 22 --avg-price 79
  python audit_calc.py --mrr 40000 --failures 22 --avg-price 79 \
      --codes insufficient_funds=8,expired_card=6,do_not_honor=4,lost_card=2
"""

import argparse
import sys

# Verified 28 July 2026 against Stripe's Smart Retries documentation, re-checked
# 16 August 2026 and unchanged. build_decline_dataset.py imports these two sets and
# fails if they stop agreeing with DeclineCodeClassifier.cs.
BLOCKED = {
    "incorrect_number", "lost_card", "pickup_card", "stolen_card",
    "revocation_of_authorization", "revocation_of_all_authorizations",
    "authentication_required", "highest_risk_level", "transaction_not_allowed",
}
NEEDS_NEW_CARD = BLOCKED | {"expired_card", "incorrect_cvc",
                            "invalid_expiry_month", "invalid_expiry_year"}
# Same ranges as BacktestRecoveryModel. Assumptions, deliberately conservative.
SOFT_RANGE, BLOCKED_RANGE = (0.30, 0.50), (0.00, 0.05)


def money(x):
    return f"${x:,.0f}"


def step1(mrr, failures, avg_price):
    monthly = failures * avg_price
    print("STEP 1: what the volume costs\n")
    print(f"  {failures} failed payments a month x {money(avg_price)} average = "
          f"{money(monthly)} a month at risk")
    print(f"  {money(monthly)} x 12 = {money(monthly * 12)} a year")
    if mrr:
        subs = mrr / avg_price
        print(f"\n  Your MRR of {money(mrr)} at {money(avg_price)} implies about "
              f"{subs:,.0f} active subscriptions,")
        print(f"  so roughly {failures / subs * 100:.1f}% of them fail in a given month.")
        print(f"  That is {monthly / mrr * 100:.1f}% of MRR at risk each month.")
    print("\n  Every figure above is arithmetic on their own numbers. Nothing assumed.")
    return monthly


def step2(counts, avg_price):
    total = sum(counts.values())
    if not total:
        return
    blocked = sum(n for c, n in counts.items() if c in BLOCKED)
    newcard = sum(n for c, n in counts.items() if c in NEEDS_NEW_CARD)
    soft = total - blocked
    print("\n\nSTEP 2: what is actually reachable\n")
    print(f"  {total} invoices sampled")
    print(f"  {blocked} ({blocked/total*100:.0f}%) on codes Stripe will not retry at all "
          f"without a new card")
    print(f"  {newcard} ({newcard/total*100:.0f}%) need new card details, retry or not")
    print(f"  {soft} ({soft/total*100:.0f}%) are temporary and worth retrying")

    lo = soft * SOFT_RANGE[0] + blocked * BLOCKED_RANGE[0]
    hi = soft * SOFT_RANGE[1] + blocked * BLOCKED_RANGE[1]
    print(f"\n  Applying the assumed ranges ({int(SOFT_RANGE[0]*100)}-{int(SOFT_RANGE[1]*100)}% "
          f"on temporary, {int(BLOCKED_RANGE[0]*100)}-{int(BLOCKED_RANGE[1]*100)}% on blocked):")
    print(f"  {lo:.0f} to {hi:.0f} of {total} recoverable, "
          f"{money(lo/total*100*avg_price)} to {money(hi/total*100*avg_price)} per 100 failures")
    print("\n  SAY OUT LOUD IN THE REPLY: those percentages are assumptions from "
          "/docs/backtest,\n  not measured results. RecoverFlow has no customer data to quote yet.")

    if blocked / total > 0.4:
        print("\n  READ: most of their loss cannot be retried. This is an email problem.")
        print("  Stripe's free retries will not help much. Say so plainly.")
    else:
        print("\n  READ: most of their loss is retryable, which is exactly what Stripe's free")
        print("  Smart Retries already do. Tell them to check their retry settings FIRST.")
        print("  If they are not already on 8 attempts over 2 weeks, that is a free fix and")
        print("  they should do that before paying anyone, including us.")


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--mrr", type=float, default=0)
    p.add_argument("--failures", type=float, required=True)
    p.add_argument("--avg-price", type=float, required=True)
    p.add_argument("--codes", default="", help="code=count,code=count from their Stripe dashboard")
    a = p.parse_args()

    if a.avg_price <= 0 or a.failures < 0:
        sys.exit("avg-price must be positive and failures cannot be negative")

    step1(a.mrr, a.failures, a.avg_price)
    if a.codes:
        counts = {}
        for part in a.codes.split(","):
            k, _, v = part.partition("=")
            counts[k.strip()] = int(v or 0)
        unknown = [c for c in counts if c not in NEEDS_NEW_CARD and c not in BLOCKED]
        step2(counts, a.avg_price)
        if unknown:
            print(f"\n  Treated as temporary/retryable: {', '.join(unknown)}")
