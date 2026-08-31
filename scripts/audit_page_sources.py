#!/usr/bin/env python3
"""Work out which pages under docs/ a builder emits, and which are hand-written.

`seo/ROUTINE.md` used to open by saying every file in docs/ is generated. That
was never true. 24 of the 61 pages, including the whole of /tools/ and fourteen
blog posts, have no builder at all, and a weekly run that believed the docstring
either edited a script that emits nothing or refused to touch a page that was
perfectly safe to edit.

Nothing here is typed in twice. Membership is measured: snapshot the modified
times, run every page builder, and see which files were rewritten. HAND_WRITTEN
below is only an expectation, and the script fails if reality has moved away
from it, so a page that gains or loses a builder has to be acknowledged rather
than discovered a month later.

    python scripts/audit_page_sources.py

Exits non-zero if the split has drifted, or if rebuilding changed a file that
was committed, which means docs/ and the builders had fallen out of step.
"""

import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(ROOT, "docs")

# Order matters. apply_design_system.py injects rf.css and the fonts that the
# content builders do not emit, so it runs last or every page it touched loses
# the design system. build_sitemap.py is deliberately absent: it writes no page.
BUILDERS = [
    "build_content_pages.py",
    "build_compare_pages.py",
    "build_docs_pages.py",
    "build_legal_pages.py",
    "apply_design_system.py",
]

# Pages no builder emits, so the only way to change one is to edit it directly.
# Sorted, and checked against what the builders actually wrote.
HAND_WRITTEN = {
    "/",
    "/pricing/",
    "/recover-failed-stripe-payments/",
    "/compare/stripe-native/",
    "/tools/",
    "/tools/decline-code-lookup/",
    "/tools/dunning-email-generator/",
    "/tools/recovery-estimator/",
    "/tools/retry-schedule-builder/",
    "/tools/visa-retry-budget/",
    "/blog/invoice-payment-failed-vs-payment-intent-payment-failed/",
    "/blog/stripe-call-issuer-decline/",
    "/blog/stripe-card-velocity-exceeded/",
    "/blog/stripe-currency-not-supported-decline/",
    "/blog/stripe-duplicate-transaction-decline/",
    "/blog/stripe-fraudulent-decline-code/",
    "/blog/stripe-generic-decline-code/",
    "/blog/stripe-incorrect-cvc-decline/",
    "/blog/stripe-incorrect-number-vs-invalid-number/",
    "/blog/stripe-lost-card-stolen-card-declines/",
    "/blog/stripe-pickup-card-vs-restricted-card/",
    "/blog/stripe-processing-error-decline/",
    "/blog/stripe-transaction-not-allowed-decline/",
    "/blog/stripe-try-again-later-decline/",
}


def pages():
    """Every built page, as the URL path it is served at."""
    out = {}
    for root, dirs, files in os.walk(DOCS):
        dirs[:] = [d for d in dirs if not d.startswith((".", "_"))]
        if "index.html" not in files:
            continue
        rel = os.path.relpath(root, DOCS).replace(os.sep, "/")
        out["/" if rel == "." else f"/{rel}/"] = os.path.join(root, "index.html")
    return out


def dirty():
    """Pages already differing from HEAD, so the rebuild does not get blamed."""
    try:
        out = subprocess.run(("git", "-C", ROOT, "diff", "--name-only", "HEAD",
                              "--", "docs"), capture_output=True, text=True,
                             encoding="utf-8", check=True)
    except (OSError, subprocess.CalledProcessError):
        return None
    return {line.strip() for line in out.stdout.splitlines() if line.strip()}


def main():
    before = dirty()
    found = pages()
    stamps = {path: os.path.getmtime(f) for path, f in found.items()}

    for builder in BUILDERS:
        script = os.path.join(ROOT, "scripts", builder)
        run = subprocess.run((sys.executable, script), capture_output=True,
                             text=True, encoding="utf-8")
        if run.returncode != 0:
            print(f"{builder} failed:\n{run.stdout}{run.stderr}")
            return 1

    written = {p for p, was in stamps.items()
               if os.path.getmtime(found[p]) > was + 0.001}
    hand = set(found) - written

    print(f"{len(found)} pages: {len(written)} generated, {len(hand)} hand-written")

    problems = []
    for path in sorted(hand - HAND_WRITTEN):
        problems.append(f"{path} has no builder but is not listed in HAND_WRITTEN. "
                        "Add it there, or point a builder at it.")
    for path in sorted(HAND_WRITTEN - hand):
        if path in found:
            problems.append(f"{path} is listed in HAND_WRITTEN but a builder now "
                            "emits it. Drop it from the list; editing it directly "
                            "will be overwritten from now on.")
        else:
            problems.append(f"{path} is listed in HAND_WRITTEN but no longer exists.")

    after = dirty()
    if before is not None and after is not None:
        for path in sorted(after - before):
            problems.append(f"{path} changed when the builders ran, so docs/ was "
                            "out of step with the script that emits it. Commit the "
                            "rebuild, or find the hand edit that caused it.")

    if problems:
        print(f"\n{len(problems)} problem(s):")
        for p in problems:
            print("  " + p)
        return 1

    print("the split matches HAND_WRITTEN, and no page moved when the builders ran")
    return 0


if __name__ == "__main__":
    sys.exit(main())
