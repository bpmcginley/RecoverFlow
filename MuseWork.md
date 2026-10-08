# MuseWork.md

Work log for Muse (Bruce's AI agent). Read this when working in this repo.

## What Muse did (2026-10-08)

- Created **draft PR #9** "Add /compare/churnbuster/ and /compare/reecova/ pages" (branch `content/compare-churnbuster-reecova`, commit `6ab832fa`, **not merged** — awaiting Bruce's review).
  - Added two new competitor comparison pages, `/compare/churnbuster/` and `/compare/reecova/`, generated via `scripts/build_compare_pages.py` following the repo's builder conventions.
  - Re-verified competitor pricing from vendor sites on Oct 8, 2026: ChurnBuster $269/mo (Dunning or Cancel Flows alone) / $430/mo (both) — this moved up from the $149/mo in the July index, so old index copy is stale. Reecova $49.99/mo + 10% of recovered revenue.
  - Builder gained backwards-compatible `verified_on` / `updated` / `date_published` fields plus `hybrid` pricing support, so Reecova's page shows the honest two-crossover math (RecoverFlow cheaper below ~$333/mo and above ~$2,490/mo; Reecova cheaper between).
  - Regenerated the compare index (7 cards) and hand-updated `docs/sitemap.xml` with the 2 new URLs.
  - Verified existing pages regenerate byte-identically, so the source audit stays green.
- Also prepared the same day (kept off-repo, not committed): Show HN and Indie Hackers launch-post drafts plus a 10-prospect outreach backlog — they live in `~/workspace/goals/recoverflow-outreach/hidden_files/` on Bruce's machine.

## Current state / thoughts

- `main` had no commits today; its latest is Sep 27, 2026 ("Add per-account monthly minimum grace period"). All Oct 8 work sits on the PR #9 branch.
- Product: Stripe failed-payment recovery for indie SaaS (recoverflow.org). Pricing: 30 days free, then 25% of attributed recoveries, $29/mo floor, $299/mo cap, free 90-day backtest.
- Watch item: the `.org` domain's trust perception came up in Show HN feedback.

## Suggestions for the Claude agent

- **Re-verify competitor pricing periodically** (quarterly at least). ChurnBuster's entry price moved $149 → $269/mo between July and Oct 2026 — stale pricing on a comparison page kills credibility. Use the builder's `verified_on` field.
- After any page change, run `validate_site.py` and `audit_page_sources.py` — the compare pages are generated, and the audit expects generated pages to be byte-identical to builder output.
- New compare pages must go through `scripts/build_compare_pages.py`; don't hand-write pages the builder owns.
- There is an autonomous outreach cron (Mon/Fri mornings) sending from admin@recoverflow.org with a do-not-contact list — don't duplicate it or mail its prospects separately.
- Launch-post drafts already exist locally; don't re-draft them, and ask Bruce before posting anything.
- Never merge PRs without Bruce's explicit go-ahead.
