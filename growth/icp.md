# Who to write to, and why that range

Targeting here is derived from the pricing, not from a persona workshop. RecoverFlow bills
25% of attributed recovery, with a $29 monthly floor after the first 30 days and a $299
monthly cap. Those two numbers set both ends of the range.

## The lower bound: about $10k MRR

For a customer to clear the $29 floor rather than pay it, RecoverFlow has to attributably
recover at least $116 a month, because 25% of $116 is $29.

Working backwards, conservatively:

- Roughly 6% of subscription charges fail in a given month. Call failed volume `0.06 × MRR`.
- Of that, the share RecoverFlow can attribute to its own action, over and above what
  Stripe's free Smart Retries would have recovered anyway, is the honest number and it is
  not large. Assume 20%.
- So attributable recovery ≈ `0.20 × 0.06 × MRR` = `0.012 × MRR`.
- Setting that at or above $116: `MRR ≥ $9,700`.

**Below roughly $10k MRR the product costs more than it returns.** That is not a targeting
preference, it is arithmetic, and the audit is supposed to say so out loud when it comes up.
Writing to those companies wastes the send and produces the exact conversation the free
audit exists to have honestly.

## The upper bound: about $150k MRR

The $299 cap means a company at $500k MRR pays exactly what a company at $150k MRR pays.
There is no revenue reason to chase upmarket. There are three reasons not to:

- Above roughly $150k MRR there is usually someone who owns billing, so it stops being a
  founder decision and starts being a vendor review.
- That review compares against Churnkey, Baremetrics Recover, Paddle Retain and Stunning,
  with procurement and a security questionnaire attached.
- The revenue at the end of it is capped at the same $299.

Not disqualifying, but it converts slower for identical money. Sort it below the core range.

**Core range: $10k to $150k MRR.** Usually 2 to 50 people.

## The three tracks

| Track | Who | Opening | Template |
|---|---|---|---|
| `prospect` | Runs subscriptions on Stripe, handling failures with Stripe defaults or nothing | The product, plus one true sentence about them | `t1-prospect.txt` |
| `competitor` | Already pays for a recovery tool | Name their tool and where it is weak; point at `/compare` | `t1-competitor.txt` |
| `peer` | Builds on Stripe billing but isn't a buyer: billing infra, monetization layers, email tooling | Compare notes, no pitch. Plays for integrations and word of mouth | `t1-peer.txt` |

`competitor` is the highest-intent track. They have already agreed the category is worth
money, so the only question left is which tool, and there are five side-by-side pages under
`/compare` built for exactly that conversation. It is also the track where a generic email
is most obviously generic, so it has the strictest personalisation rule.

## Disqualify outright

- **Merchant-of-record billing** (Paddle, Lemon Squeezy, Gumroad). They have no Stripe
  account to connect and the MoR handles dunning. Nothing to sell, ever.
- **One-time payments or marketplace-only Stripe usage.** No subscriptions, no involuntary
  churn.
- **Enterprise billing stacks** (Zuora, Recurly, Chargebee). Dunning is already in the
  contract and the switching cost dwarfs $299 a month.
- **Pre-revenue.** No failed payments to recover.
- **Agencies and consultancies.** Invoice-based, not card-on-file recurring.
- **Anyone who has asked not to be contacted.** Log as `disqualified` immediately; the
  ledger is what makes that stick across sessions.

## Sourcing

**Apollo does not work for this ICP on the current plan. Tested 14 August 2026, do not repeat
it without checking the plan first.** What happens, in order:

1. `apollo_mixed_people_api_search` returns `API_INACCESSIBLE`. The search API is excluded
   from the free plan entirely.
2. Routing through `apollo_agent_find_prospects` gets past that, but the
   `currently_using_any_of_technology_uids: ['stripe']` filter is itself paid-only. That is
   the one filter that matters most here, because "uses Stripe" is most of the qualification.
3. Without it the pool is 1.6M B2B SaaS companies of poor fit. Actual previews returned NEOM,
   Il Sole 24 ORE and ConstructionPlacements: a Saudi megaproject, an Italian newspaper and a
   jobs board.
4. The agent falls back to AI research to qualify on Stripe. There are 25 free runs. **All 25
   came back unqualified.** Going further means buying credits to research 300 companies on
   the evidence of a 0/25 trial.
5. `export_credit` is 0 regardless, so nothing can be pulled out in bulk even if it qualified.

So the credit balance was never the binding constraint. The plan is. Apollo Basic unlocks the
technology filter and the search API; until someone decides to pay for that, Apollo is not the
sourcing channel and this section is a record of why rather than an instruction.

If the plan is upgraded, ask the agent for: titles founder / co-founder / ceo / cto; headcount
2 to 50; keywords saas, subscription, b2b software; technology stripe; email status verified.

## Sourcing by hand, which is what actually fits

Worth being honest that this is not much of a downgrade. The binding constraint on this
pipeline is not list size, it is the one true sentence per prospect at 10 sends a day. That is
40 to 50 a week, and a list of 40 hand-picked companies where Stripe billing is visible on the
pricing page beats 400 unqualified rows that still need the same sentence written.

Places where Stripe-billed subscription SaaS in the $10k to $150k range actually congregate:

- Indie Hackers products with revenue disclosed. MRR is stated, which resolves the whole
  lower-bound question in one glance.
- Product Hunt, filtered to subscription B2B tools rather than one-time or free.
- MicroConf and similar bootstrapper communities, where the size range is the norm.
- Competitor comparison traffic. Anyone publicly discussing Churnkey, Baremetrics Recover,
  Paddle Retain or Stunning is a `competitor` track prospect who has already agreed the
  category is worth money.

Check Stripe by opening their checkout. It is visible from outside, and it is the same check
the Apollo filter would have done, minus the plan gate.

Then, before anything is added to the ledger, check by hand:

1. Do they actually sell a **subscription**, not one-time or usage-invoiced? Pricing page.
2. Is Stripe the **billing** system, not just a checkout button?
3. Can you write **one true sentence** about them? If not, drop them. That sentence is the
   entire difference between this and spam, and there is no version of the framework that
   manufactures it for you.

Roughly half of any Apollo pull dies at those three checks. That is the filter working.

## Channel note

Average revenue per customer sits between $29 and $299 a month, so lifetime value is low
hundreds to low thousands. Paid acquisition does not clear that bar at this ACV, so it is
off the table. That leaves three channels whose economics work: cold email, SEO, and the
Stripe App Marketplace.

The Marketplace was written off once and is back. The original blocker was that Stripe will
not grant public distribution to a Connect platform account, which is what RecoverFlow's main
product has to be to read merchant data — so `stripe-app/BLOCKED.md` concluded there were only
two channels. That was superseded on 4 August 2026. The listing now goes through a separate
standalone Stripe App, `com.recoverflow.dashboard` in `stripe-dashboard-app/`, which reaches a
merchant through the Stripe App *install grant* instead of Connect OAuth and so is not bound by
the platform-account restriction. Read `stripe-dashboard-app/`, not `stripe-app/BLOCKED.md`,
for the live picture.

That app is in review, not yet published: v0.0.3 was rejected on the OAuth callback, v0.0.4
shipped the fix and is awaiting Stripe's verdict. Until it is approved and publicly listed,
cold email and SEO are still the only channels actually turning, so keep working them. But the
Marketplace is a live, in-flight third channel now — the highest-leverage of the three once it
lands, because it borrows Stripe's directory and trust — not a closed door. Plan for three.
