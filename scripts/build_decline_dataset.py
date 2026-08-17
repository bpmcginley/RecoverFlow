#!/usr/bin/env python3
"""Emit the machine-readable Stripe decline-code dataset published at /data/.

Set membership is never typed in here. It is read out of the two places that
already decide it at runtime:

  src/RecoverFlow.Domain/Services/DeclineCodeClassifier.cs  (the product)
  scripts/audit_calc.py                                     (the audit maths)

The two are cross-checked against each other before anything is written, so the
published file cannot quietly disagree with either. A unit test in
tests/RecoverFlow.Tests.Unit re-asserts the same equality from the C# side, so a
future edit to the classifier fails the build rather than shipping stale data.

Everything that is not set membership (what Stripe recommends you do, the test
card, the source URL) is a literal in METADATA below, each one traceable to
Stripe's own documentation or to a page in this repo.

    python scripts/build_decline_dataset.py

Writes docs/data/stripe-decline-codes.json and docs/data/stripe-decline-codes.csv.
"""

import csv
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLASSIFIER = os.path.join(
    ROOT, "src", "RecoverFlow.Domain", "Services", "DeclineCodeClassifier.cs")
OUT_DIR = os.path.join(ROOT, "docs", "data")
SITE = "https://recoverflow.org"

sys.path.insert(0, os.path.join(ROOT, "scripts"))
import audit_calc  # noqa: E402  the audit's own code sets, imported not copied

# Date the code list below was last read off Stripe's published documentation.
# Re-checked 16 August 2026 against https://docs.stripe.com/declines/codes and
# https://docs.stripe.com/billing/revenue-recovery/smart-retries. The nine
# non-retryable codes were unchanged from the 28 July 2026 check.
VERIFIED_ON = "2026-08-16"

SMART_RETRIES = ("https://docs.stripe.com/billing/revenue-recovery/"
                 "smart-retries#non-retryable-decline-codes")
DECLINE_CODES = "https://docs.stripe.com/declines/codes"

# recommended_action is Stripe's own "Next steps" for the code, in the wording
# already published on /tools/decline-code-lookup/. test_card_number is only
# filled in where Stripe publishes a card that produces exactly this code; the
# rest are null rather than guessed. guide_url points at our own guide where one
# exists, and is null otherwise rather than pointing somewhere approximate.
METADATA = {
    "authentication_required": dict(
        action="Trigger an authentication flow. The customer can pay once authenticated.",
        guide="/blog/stripe-authentication-required-recovery/"),
    "card_declined": dict(
        action="Read the decline_code for the reason. card_declined is the error code "
               "Stripe returns alongside a decline code, not a decline code itself.",
        card="4000000000000002", source=None, verified=None),
    "card_velocity_exceeded": dict(
        action="The customer should contact their issuer or use a different payment method.",
        guide="/blog/stripe-card-velocity-exceeded/"),
    "do_not_honor": dict(
        action="The customer needs to contact their card issuer.",
        guide="/blog/do-not-honor-stripe-decline/"),
    "expired_card": dict(
        action="The customer needs to use a different card.",
        card="4000000000000069", guide="/blog/expired-card-stripe-retries/"),
    "fraudulent": dict(
        action="Present as a generic decline. Do not disclose the reason.",
        guide="/blog/stripe-fraudulent-decline-code/"),
    "generic_decline": dict(
        action="The customer needs to contact their card issuer.",
        card="4000000000000002", guide="/blog/stripe-generic-decline-code/"),
    "highest_risk_level": dict(
        action="Present as a generic decline. Do not disclose the reason."),
    "incorrect_cvc": dict(
        action="The customer should retry with the correct CVC.",
        card="4000000000000127", guide="/blog/stripe-incorrect-cvc-decline/"),
    "incorrect_number": dict(
        action="The customer should retry with the correct card number.",
        card="4242424242424241", guide="/blog/stripe-incorrect-number-vs-invalid-number/"),
    "insufficient_funds": dict(
        action="The customer should use an alternative payment method.",
        card="4000000000009995", guide="/blog/is-insufficient-funds-a-hard-decline/"),
    "invalid_expiry_month": dict(
        action="The customer should retry with the correct expiry date."),
    "invalid_expiry_year": dict(
        action="The customer should retry with the correct expiry date."),
    "lost_card": dict(
        action="Present as a generic decline. Do not disclose the reason.",
        card="4000000000009987", guide="/blog/stripe-lost-card-stolen-card-declines/"),
    "merchant_blacklist": dict(
        action="Present as a generic decline. Do not disclose the reason."),
    "pickup_card": dict(
        action="Present as a generic decline. The customer should contact their issuer.",
        guide="/blog/stripe-pickup-card-vs-restricted-card/"),
    "processing_error": dict(
        action="Attempt the payment again. If it persists, retry later.",
        card="4000000000000119", guide="/blog/stripe-processing-error-decline/"),
    "restricted_card": dict(
        action="Present as a generic decline. The customer should contact their issuer.",
        guide="/blog/stripe-pickup-card-vs-restricted-card/"),
    "revocation_of_all_authorizations": dict(
        action="The customer needs to contact their card issuer."),
    "revocation_of_authorization": dict(
        action="The customer needs to contact their card issuer."),
    "security_violation": dict(
        action="The customer needs to contact their card issuer."),
    "stolen_card": dict(
        action="Present as a generic decline. Do not disclose the reason.",
        card="4000000000009979", guide="/blog/stripe-lost-card-stolen-card-declines/"),
    "transaction_not_allowed": dict(
        action="The customer needs to contact their card issuer.",
        guide="/blog/stripe-transaction-not-allowed-decline/"),
    "try_again_later": dict(
        action="Attempt the payment again. If it persists the customer should contact "
               "their issuer.",
        guide="/blog/stripe-try-again-later-decline/"),
}

FIELDS = ["code", "blocked_by_stripe", "classification", "requires_card_update",
          "recommended_action", "test_card_number", "guide_url", "source_url",
          "verified_on"]


def hashset(source, name):
    """Read one `HashSet<string> NAME = [ ... ];` out of the classifier."""
    m = re.search(rf"HashSet<string>\s+{name}\s*=\s*\[(.*?)\];", source, re.S)
    if not m:
        sys.exit(f"{name} not found in DeclineCodeClassifier.cs")
    return set(re.findall(r'"([a-z_]+)"', m.group(1)))


def requires_card_update(source):
    """Read the code list out of the RequiresCardUpdate pattern match."""
    m = re.search(r"RequiresCardUpdate\(string\? declineCode\)\s*=>(.*?);", source, re.S)
    if not m:
        sys.exit("RequiresCardUpdate not found in DeclineCodeClassifier.cs")
    return set(re.findall(r'"([a-z_]+)"', m.group(1)))


def build():
    with open(CLASSIFIER, encoding="utf-8") as f:
        cs = f.read()

    blocked = hashset(cs, "RetryBlockedByStripe")
    not_worth = hashset(cs, "NotWorthRetrying")
    soft = hashset(cs, "SoftDeclineCodes")
    card_update = requires_card_update(cs)

    # The audit maths and the classifier have to describe the same world. These
    # are the two relationships that hold today; if either breaks, one of the two
    # files moved and a human needs to decide which one is right.
    if blocked != audit_calc.BLOCKED:
        sys.exit("RetryBlockedByStripe and audit_calc.BLOCKED disagree: "
                 f"{blocked ^ audit_calc.BLOCKED}")
    if blocked | card_update != audit_calc.NEEDS_NEW_CARD:
        sys.exit("audit_calc.NEEDS_NEW_CARD is not RetryBlockedByStripe plus "
                 f"RequiresCardUpdate: {(blocked | card_update) ^ audit_calc.NEEDS_NEW_CARD}")

    overlap = (blocked & not_worth) | (blocked & soft) | (not_worth & soft)
    if overlap:
        sys.exit("a code is in more than one classification set, so its "
                 f"classification depends on match order: {sorted(overlap)}")

    codes = sorted(blocked | not_worth | soft)
    undocumented = [c for c in codes if c not in METADATA]
    if undocumented:
        sys.exit("no METADATA entry for: " + ", ".join(undocumented) +
                 "\nAdd one sourced from Stripe's docs before republishing.")

    rows = []
    for code in codes:
        meta = METADATA[code]
        is_blocked = code in blocked
        rows.append({
            "code": code,
            "blocked_by_stripe": is_blocked,
            "classification": "soft_decline" if code in soft and not is_blocked
                              and code not in not_worth else "hard_decline",
            "requires_card_update": code in card_update,
            "recommended_action": meta["action"],
            "test_card_number": meta.get("card"),
            "guide_url": SITE + meta["guide"] if meta.get("guide") else None,
            "source_url": meta.get("source", SMART_RETRIES if is_blocked else DECLINE_CODES),
            "verified_on": meta.get("verified", VERIFIED_ON),
        })
    return rows


def cell(value):
    if value is None:
        return ""
    if isinstance(value, bool):
        return "true" if value else "false"
    return value


def main():
    rows = build()
    os.makedirs(OUT_DIR, exist_ok=True)

    doc = {
        "name": "Stripe decline codes, classified for subscription retry",
        "description": "One record per decline code RecoverFlow classifies. "
                       "blocked_by_stripe marks the codes Stripe will not execute a "
                       "retry for until a new payment method is attached. Generated "
                       "from the same code that runs in the product.",
        "license": "CC0-1.0",
        "generated_by": "scripts/build_decline_dataset.py",
        "source_of_truth": "src/RecoverFlow.Domain/Services/DeclineCodeClassifier.cs",
        "documentation_checked_on": VERIFIED_ON,
        "record_count": len(rows),
        "codes": rows,
    }

    json_path = os.path.join(OUT_DIR, "stripe-decline-codes.json")
    with open(json_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")

    csv_path = os.path.join(OUT_DIR, "stripe-decline-codes.csv")
    with open(csv_path, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS, lineterminator="\n")
        w.writeheader()
        for row in rows:
            # Booleans go out as JSON spelling, not Python's, so the two files
            # read the same in a spreadsheet and in a parser.
            w.writerow({k: cell(row[k]) for k in FIELDS})

    blocked = sum(1 for r in rows if r["blocked_by_stripe"])
    print(f"{len(rows)} codes written ({blocked} blocked by Stripe)")
    print("  " + os.path.relpath(json_path, ROOT).replace(os.sep, "/"))
    print("  " + os.path.relpath(csv_path, ROOT).replace(os.sep, "/"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
