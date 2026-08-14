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

## Sourcing recipe (Apollo)

Budget is the binding constraint: **199 lead credits left in the cycle ending 10 September,
and zero export credits.** No CSV dumps; pull contacts and add them to the ledger directly.
That caps sourcing at roughly 200 a month, which happens to match what one person can send
with real personalisation at 10 a day. Do not try to beat that number, it is not the
bottleneck worth attacking.

`apollo_mixed_people_api_search` with:

- `person_titles`: founder, co-founder, ceo, cto, head of growth, head of revenue
- `organization_num_employees_ranges`: `2,50`
- `q_organization_keyword_tags`: saas, subscription, b2b software
- `technologies`: stripe
- `contact_email_status`: verified

Then, before anything is added to the ledger, check by hand:

1. Do they actually sell a **subscription**, not one-time or usage-invoiced? Pricing page.
2. Is Stripe the **billing** system, not just a checkout button?
3. Can you write **one true sentence** about them? If not, drop them. That sentence is the
   entire difference between this and spam, and there is no version of the framework that
   manufactures it for you.

Roughly half of any Apollo pull dies at those three checks. That is the filter working.

## Channel note

Average revenue per customer sits between $29 and $299 a month, so lifetime value is low
hundreds to low thousands. Paid acquisition does not clear that bar at this ACV. Cold email
and SEO are the only two channels whose economics work, and the Stripe App Marketplace,
which would have been the third, is closed off: Stripe will not grant public distribution to
a Connect platform account, which is what RecoverFlow has to be to read merchant data. See
`stripe-app/BLOCKED.md`. Plan around two channels, not three.
