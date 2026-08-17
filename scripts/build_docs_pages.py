#!/usr/bin/env python3
"""Builds /docs/* and /changelog.

Everything on these pages is read out of the codebase or the git history. If
you change a default in Options.cs or the classifier, change it here too, or
the docs start lying. The numbers below are annotated with where they live.
"""

import os
import sys
import json

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_legal_pages import CSS, HEADER, FOOTER, TRACKING, DOCS, UPDATED  # noqa: E402
from build_content_pages import shell, plain, HARD_CODES  # noqa: E402

SITE = "https://recoverflow.org"


def doc_schema(slug, h1, desc, iso_modified="2026-07-28"):
    return {
        "@context": "https://schema.org",
        "@type": "TechArticle",
        "headline": plain(h1),
        "description": desc,
        "url": f"{SITE}/docs/{slug}/" if slug else f"{SITE}/docs/",
        "dateModified": iso_modified,
        "author": {"@type": "Person", "name": "Bruce McGinley"},
        "publisher": {"@type": "Organization", "name": "RecoverFlow", "url": SITE},
    }


def build_doc(slug, title, h1, desc, body_html, faqs=None, updated=None, iso_modified="2026-07-28"):
    # A page that describes a later change has to say so, otherwise the "as of"
    # line quietly contradicts the page under it.
    updated = updated or UPDATED
    faq_block = ""
    schema = [doc_schema(slug, h1, desc, iso_modified)]
    if faqs:
        faq_block = "\n".join(
            ['<h2 id="faq">Common questions</h2>', '<div class="faq">']
            + [f"  <details><summary>{q}</summary><p>{a}</p></details>" for q, a in faqs]
            + ["</div>"])
        schema.append({
            "@context": "https://schema.org",
            "@type": "FAQPage",
            "mainEntity": [
                {"@type": "Question", "name": q,
                 "acceptedAnswer": {"@type": "Answer", "text": a}}
                for q, a in faqs
            ],
        })
    body = f"""<main>
  <article class="wrap">
    <p class="eyebrow"><a href="/docs/">Docs</a></p>
    <h1>{h1}</h1>
    <p class="updated">Reflects the code as of {updated}. If the product changes and this page does not, that is a bug worth emailing about.</p>

{body_html}

    {faq_block}
  </article>
</main>"""
    url = f"{SITE}/docs/{slug}/" if slug else f"{SITE}/docs/"
    return shell(title, desc, url, body, schema)


# ---------------------------------------------------------------------------
# Fragments generated from real constants
# ---------------------------------------------------------------------------

def blocked_table():
    rows = "\n".join(
        f'      <tr><th scope="row"><code>{c}</code></th><td>{d}</td></tr>'
        for c, d in HARD_CODES)
    return f"""<div class="table-scroll">
  <table>
    <thead><tr><th scope="col">Decline code</th><th scope="col">Why we do not retry it</th></tr></thead>
    <tbody>
{rows}
    </tbody>
  </table>
</div>"""


PAGES = []

# --- hub -------------------------------------------------------------------
PAGES.append(dict(
    slug="", title="RecoverFlow docs | How the product actually behaves",
    h1="Documentation",
    desc="How RecoverFlow connects to Stripe, classifies declines, schedules retries, sends dunning emails, and decides what it is allowed to bill you for.",
    body="""    <p>These pages describe what the software does, not what it aspires to do. Where a behaviour is a default you can expect to change, it says so. Where something is missing, it says that too.</p>

    <div class="card-grid">
      <div class="card"><h3><a href="/docs/how-it-works/">How it works</a></h3><p>The whole path from a failed payment to a recovered one, in order.</p></div>
      <div class="card"><h3><a href="/docs/connecting-stripe/">Connecting Stripe</a></h3><p>What the OAuth connection grants, what it cannot do, and how to revoke it.</p></div>
      <div class="card"><h3><a href="/docs/decline-classification/">Decline classification</a></h3><p>Exactly which codes are retried, which are not, and why.</p></div>
      <div class="card"><h3><a href="/docs/retry-scheduling/">Retry scheduling</a></h3><p>The actual schedule, including the cases where it schedules nothing.</p></div>
      <div class="card"><h3><a href="/docs/dunning-emails/">Dunning emails</a></h3><p>The sequence, the timing, and what the emails currently do not do.</p></div>
      <div class="card"><h3><a href="/docs/attribution-and-billing/">Attribution and billing</a></h3><p>How a recovery is credited, and when you are not charged.</p></div>
      <div class="card"><h3><a href="/docs/backtest/">The 90-day backtest</a></h3><p>The scan that runs at connect time, and how to read its estimate.</p></div>
    </div>

    <h2>A note on these docs</h2>
    <p>RecoverFlow is early and run by one person. These pages are written from the source, and several of them end with a list of things the product does not do yet. That is deliberate. Finding out after you connect is worse for both of us.</p>
    <p>If you want the conceptual background rather than the implementation, the <a href="/blog/">guides</a> cover Stripe's own behaviour in more depth, with citations.</p>"""))

# --- how it works ----------------------------------------------------------
PAGES.append(dict(
    slug="how-it-works",
    title="How RecoverFlow works, end to end | Docs",
    h1="How it works, end to end",
    desc="From invoice.payment_failed to a recovered payment: the webhook, the classification, the retry schedule, the email sequence, and the attribution.",
    body="""    <p>The whole system is one pipeline that starts at a Stripe webhook and ends either in a recovered payment or a case marked lost. Here it is in order.</p>

    <h2 id="1">1. Stripe tells us a payment failed</h2>
    <p>We listen for <code>invoice.payment_failed</code> on your connected account. The handler looks up the merchant by Stripe account ID, then either opens a new recovery case for that invoice or updates the existing one. One case per invoice, never two.</p>
    <p>Every webhook event is recorded before it is acted on, so a redelivery from Stripe does not open a second case or send a second email.</p>

    <h2 id="2">2. The decline is classified</h2>
    <p>The decline code is read and sorted into hard, soft or unknown. Hard means no retry will be scheduled. Unknown means a code we do not recognise, which is treated as recoverable so that a new Stripe code does not silently stop recovery. This is covered properly in <a href="/docs/decline-classification/">decline classification</a>.</p>

    <h2 id="3">3. A retry is scheduled, or deliberately not</h2>
    <p>For soft and unknown declines a retry is scheduled by decline reason. For hard declines nothing is scheduled, because the attempt would either be refused by the issuer or never put on the wire by Stripe at all. See <a href="/docs/retry-scheduling/">retry scheduling</a>.</p>
    <p>Two guards apply throughout. A second pending attempt is never stacked on a case that already has one, and no attempt is scheduled beyond the recovery window.</p>

    <h2 id="4">4. The first email goes out</h2>
    <p>When a case first opens, step 1 of the dunning sequence sends immediately. Later steps follow the retry schedule. See <a href="/docs/dunning-emails/">dunning emails</a>.</p>

    <h2 id="5">5. Retries execute in the background</h2>
    <p>Each scheduled attempt is a background job. When it fires, it asks Stripe to pay the invoice and records the outcome. If it declines again, the decline code from <em>that</em> attempt decides whether there is a next one, so a case that starts as <code>insufficient_funds</code> and becomes <code>lost_card</code> stops being retried at that point rather than running out the schedule.</p>
    <p>When retries are exhausted the case is marked lost, unless the decline is one that needs a new card, in which case the case stays open for the card-update flow.</p>

    <h2 id="6">6. Recovery is detected and attributed</h2>
    <p>We watch for the invoice being paid. When it is, the case closes as recovered and is attributed to whichever mechanism most plausibly caused it. If nothing we did can be credited, it is recorded as recovered without our involvement and you are not billed for it. See <a href="/docs/attribution-and-billing/">attribution and billing</a>.</p>

    <h2 id="stack">What it runs on</h2>
    <p>.NET 8 in a Clean Architecture layout, PostgreSQL via EF Core, Hangfire for scheduled jobs, SendGrid for email, hosted on Render. Merchant data is isolated at the database layer with EF Core global query filters, so a query that forgets to filter by tenant returns nothing rather than someone else's rows.</p>""",
    faqs=[
        ("How quickly does RecoverFlow react to a failed payment?",
         "The first dunning email is sent when the recovery case opens, which is when Stripe's invoice.payment_failed webhook arrives. Retries are scheduled from that point according to the decline reason."),
        ("Does RecoverFlow interfere with Stripe's own Smart Retries?",
         "They coexist. Stripe continues to run whatever retry settings you have configured on your own account. RecoverFlow schedules its own attempts and sends its own emails. If you would rather Stripe handled retries alone, its free settings are genuinely capable and there is a page on this site about when that is the right call."),
        ("What happens if Stripe sends the same webhook twice?",
         "Nothing extra. Processed events are recorded, so a redelivery does not open a second case, schedule a second retry, or send a duplicate email."),
    ]))

# --- connecting stripe -----------------------------------------------------
PAGES.append(dict(
    slug="connecting-stripe",
    title="Connecting your Stripe account | RecoverFlow docs",
    h1="Connecting Stripe",
    desc="RecoverFlow connects over Stripe Connect OAuth. What that grants, what it cannot reach, where the token is stored, and how to revoke it.",
    body="""    <p>RecoverFlow connects to your Stripe account using Stripe Connect OAuth. You approve the connection inside Stripe's own interface, not ours, and you can withdraw it the same way without asking us.</p>

    <h2 id="grants">What the connection allows</h2>
    <ul>
      <li>Reading subscriptions, invoices and payment intents, so we can see what failed and why.</li>
      <li>Attempting payment on a failed invoice, which is the retry.</li>
      <li>Reading customer email addresses, so dunning emails can be addressed to the right person.</li>
    </ul>

    <h2 id="cannot">What it does not allow</h2>
    <ul>
      <li><strong>We never see card numbers.</strong> Stripe holds those. RecoverFlow asks Stripe to charge a payment method by its ID; the card details never reach our servers. This is why the platform is PCI SAQ A.</li>
      <li>We do not create, change or cancel subscriptions.</li>
      <li>We do not issue refunds or move money out of your account.</li>
      <li>We do not change your Stripe retry settings, your dunning settings or your branding.</li>
    </ul>

    <h2 id="token">Where the access token lives</h2>
    <p>The OAuth token is encrypted at rest before it is written to the database, so a database dump on its own does not yield a usable Stripe credential. Merchant records are isolated by tenant at the query layer.</p>
    <p>More on the surrounding controls, including the certifications RecoverFlow does not hold, on the <a href="/security/">security page</a>.</p>

    <h2 id="revoke">Revoking access</h2>
    <p>Go to your Stripe Dashboard, find RecoverFlow under connected applications, and revoke it. It takes effect immediately. You do not need to email anyone, and nothing further is billed beyond recoveries already attributed before that point.</p>
    <div class="callout">
      <p><strong>This is the important bit:</strong> the off switch is on your side, in software you already trust, and it does not route through a retention conversation with us.</p>
    </div>""",
    faqs=[
        ("Does RecoverFlow ever see my customers' card numbers?",
         "No. Card data stays with Stripe. RecoverFlow references payment methods by ID and asks Stripe to attempt the charge, so card numbers never reach our servers. That is what keeps the platform in PCI SAQ A scope."),
        ("Can RecoverFlow cancel my customers' subscriptions?",
         "No. The connection does not include changing or cancelling subscriptions. Whether a subscription cancels after failed payments is decided by your own Stripe subscription settings."),
        ("How do I disconnect RecoverFlow?",
         "Revoke access from the connected applications section of your own Stripe Dashboard. It is immediate and does not require contacting us."),
        ("Is my Stripe token encrypted?",
         "Yes, it is encrypted before being stored, so a database dump alone does not produce a usable credential."),
    ]))

# --- decline classification ------------------------------------------------
PAGES.append(dict(
    slug="decline-classification",
    title="How RecoverFlow classifies declines | Docs",
    h1="Decline classification",
    desc="The exact codes RecoverFlow retries and refuses to retry, including the nine Stripe will not execute without a new card.",
    body="""    <p>Every failed payment is sorted into one of three buckets, and that decision drives everything downstream: whether a retry is scheduled, which email goes out, and what the backtest estimates you could recover.</p>

    <h2 id="blocked">Never retried: the nine Stripe will not execute</h2>
    <p>For these, Stripe keeps the retry schedule running and keeps incrementing <code>attempt_count</code>, but never sends the charge to the card network until a new payment method is attached. Scheduling our own retry against them would add delay and nothing else.</p>
""" + blocked_table() + """
    <p>This list is held in one place in the source and is checked against <a href="https://docs.stripe.com/billing/revenue-recovery/smart-retries" rel="nofollow noopener" target="_blank">Stripe's Smart Retries documentation</a>. It was last verified on 28 July 2026.</p>

    <h2 id="choice">Not retried by choice</h2>
    <p>Four more codes are retryable as far as Stripe is concerned, but we do not retry them: <code>fraudulent</code>, <code>merchant_blacklist</code>, <code>restricted_card</code> and <code>security_violation</code>. The answer is settled, and repeatedly re-asking looks like card testing.</p>

    <h2 id="soft">Retried</h2>
    <p>Everything temporary: <code>insufficient_funds</code>, <code>processing_error</code>, <code>generic_decline</code>, <code>expired_card</code>, <code>incorrect_cvc</code>, <code>card_declined</code>, <code>try_again_later</code>, <code>do_not_honor</code>, <code>card_velocity_exceeded</code>, and the invalid expiry codes.</p>
    <p>Two of those need a caveat. <code>expired_card</code> and <code>incorrect_cvc</code> are classified as soft, but the scheduler returns no retry date for them, because the fix is new card details rather than another attempt. They go straight to the card-update path.</p>

    <h2 id="unknown">Unrecognised codes</h2>
    <p>A code we do not know is treated as recoverable rather than ignored. Stripe adds codes, and the failure mode of guessing wrong in that direction is a wasted retry. The failure mode of guessing wrong in the other direction is silently abandoning revenue.</p>

    <h2 id="card-update">Which declines trigger a card update</h2>
    <p>Bad card data (<code>expired_card</code>, <code>incorrect_cvc</code>, <code>incorrect_number</code>, the invalid expiry codes) and reissues (<code>lost_card</code>, <code>stolen_card</code>, <code>pickup_card</code>). Reissues matter more than they look: the customer usually already has the replacement card, so the fix costs them under a minute.</p>
    <div class="callout">
      <p><strong>What we never put in an email:</strong> for lost, stolen and blocked cards, the reason is not named. Stripe's guidance is that a precise message is useful to whoever is holding a card they should not have. The email says the payment did not go through and asks for updated details.</p>
    </div>""",
    faqs=[
        ("Which decline codes does RecoverFlow refuse to retry?",
         "The nine Stripe will not execute without a new payment method (incorrect_number, lost_card, pickup_card, stolen_card, revocation_of_authorization, revocation_of_all_authorizations, authentication_required, highest_risk_level, transaction_not_allowed), plus fraudulent, merchant_blacklist, restricted_card and security_violation, which we choose not to retry."),
        ("Does RecoverFlow retry insufficient_funds?",
         "Yes. It is a temporary condition and one of the better codes to retry, so it gets its own schedule aimed at the 1st and 15th of the month."),
        ("What happens with a decline code RecoverFlow does not recognise?",
         "It is treated as recoverable and retried on the default schedule. Abandoning revenue because Stripe added a code we had not seen would be the worse mistake."),
        ("Is expired_card retried?",
         "No retry is scheduled for it, even though it is classified as a soft decline. An expired card needs new details, not another attempt, so it goes to the card-update path instead."),
    ]))

# --- retry scheduling ------------------------------------------------------
PAGES.append(dict(
    slug="retry-scheduling",
    title="How RecoverFlow schedules retries | Docs",
    h1="Retry scheduling",
    desc="The actual retry schedule by decline reason, the caps that bound it, and an honest note on how these intervals were chosen.",
    body="""    <p>The schedule depends on the decline reason. There is no single interval that suits a customer who is short until payday and a customer whose card processor glitched.</p>

    <h2 id="schedule">The schedule</h2>
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Decline reason</th><th scope="col">When we try again</th></tr></thead>
        <tbody>
          <tr><th scope="row"><code>insufficient_funds</code></th><td>The next payday, meaning the 1st or 15th, at 10:00. Then the day after that payday, then the following payday.</td></tr>
          <tr><th scope="row"><code>processing_error</code></th><td>Two hours later. This is a transient technical failure, not a decision about the customer.</td></tr>
          <tr><th scope="row"><code>expired_card</code>, <code>incorrect_cvc</code></th><td>Never. These go to the card-update path instead.</td></tr>
          <tr><th scope="row">Everything else retryable</th><td>Six hours, then two days, then five days.</td></tr>
        </tbody>
      </table>
    </div>
    <p>The six hour first step is deliberately not a round number of days: it lands the retry at a different time of day from the original failure, which is free to do and occasionally matters.</p>

    <h2 id="caps">The caps</h2>
    <ul>
      <li><strong>Four attempts maximum</strong> per invoice.</li>
      <li><strong>Fourteen days maximum</strong> from the first failure. An attempt that would land outside the window is not scheduled at all.</li>
      <li><strong>One pending attempt at a time.</strong> A retry that fails re-triggers <code>invoice.payment_failed</code>, so without this guard a case could stack duplicate attempts against itself.</li>
    </ul>
    <p>The decline code from the most recent attempt decides whether there is a next one. A case that starts recoverable and becomes a hard decline stops there rather than running out its remaining attempts.</p>

    <h2 id="honest">How these intervals were chosen</h2>
    <p>Reasoning, not measurement. Aiming <code>insufficient_funds</code> at the 1st and 15th is a bet that balances are most likely to have been topped up around then. Spreading attempts across different times of day is a bet that a bank declining at one hour may approve at another.</p>
    <p>We have no recovery data of our own yet, and the published "best time to retry" figures are vendor sourced and not something we would print. So these are starting points that will be revisited when there is enough real outcome data to test them, and this page will say so when that happens.</p>
    <div class="callout">
      <p><strong>Worth knowing:</strong> Stripe's own Smart Retries picks attempt timing with a model trained across the whole Stripe network, which is more data than RecoverFlow will have for a long time. If retry timing is the only thing you care about, Stripe's free version is a reasonable choice and the <a href="/compare/stripe-native/">comparison page</a> says so.</p>
    </div>""",
    faqs=[
        ("How many times will RecoverFlow retry a failed payment?",
         "Up to four attempts per invoice, and never more than fourteen days after the first failure. Attempts that would fall outside that window are not scheduled."),
        ("When does RecoverFlow retry an insufficient_funds decline?",
         "On the next payday, meaning the 1st or 15th of the month, at 10:00, then the day after, then the following payday. The reasoning is that balances are most likely to have been topped up around those dates."),
        ("Are these retry timings based on measured results?",
         "No, and we would rather say so. They are reasoned starting points. RecoverFlow has no recovery data of its own yet, and the published industry figures on retry timing are vendor sourced, so nothing here is tuned against a benchmark we would be willing to publish."),
        ("What if the decline reason changes between attempts?",
         "The most recent decline code decides what happens next. A case that starts as insufficient_funds and later returns lost_card stops being retried at that point."),
    ]))

# --- dunning emails --------------------------------------------------------
PAGES.append(dict(
    slug="dunning-emails",
    title="How RecoverFlow's dunning emails work | Docs",
    h1="Dunning emails",
    desc="The three-step sequence, when each step sends, what the emails deliberately never say, and what they do not do yet.",
    body="""    <p>Step 1 sends when the recovery case opens. Later steps follow the retry schedule, so the email cadence tracks the retry cadence rather than running on a separate clock.</p>

    <h2 id="sequence">The sequence</h2>
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Step</th><th scope="col">Tone</th><th scope="col">What it says</th></tr></thead>
        <tbody>
          <tr><th scope="row">1</th><td>Friendly reminder</td><td>The payment did not go through, this usually means a card needs updating, and no action is needed if they have already fixed it.</td></tr>
          <tr><th scope="row">2</th><td>Follow-up</td><td>Still outstanding, with the amount, asking them to update the card so the subscription is not interrupted.</td></tr>
          <tr><th scope="row">3</th><td>Final notice</td><td>Several attempts have failed, and this is the last reminder we will send.</td></tr>
        </tbody>
      </table>
    </div>
    <p>Every email ships an HTML part and a plain-text alternative. Mail without a text part is a spam-filter signal, and dunning email that lands in spam is worth nothing.</p>

    <h2 id="never">What the emails never say</h2>
    <p>They never name the decline reason for a lost, stolen or blocked card. Stripe's guidance is that a precise message is useful to whoever is holding a card they should not have, and separately, nobody wants to read the word "stolen" in an email from a company they pay.</p>
    <p>Step 3 says it is the last reminder we will send. It does not tell the customer their subscription will be cancelled, because that depends on your own Stripe end-of-retry setting, which we neither see nor control. Telling a customer their subscription is about to be cancelled when it may not be would be a false statement made on your behalf.</p>

    <h2 id="tracking">Open and click tracking</h2>
    <p>Recovery emails record opens and clicks. This is disclosed in the <a href="/privacy/">privacy policy</a> rather than buried. We publish no benchmark open or click rates anywhere on this site, because we have none worth standing behind.</p>

    <h2 id="gaps">What these emails do not do yet</h2>
    <p>This is the least finished part of the product and it would be silly to pretend otherwise.</p>
    <ul>
      <li><strong>Copy does not vary by decline reason.</strong> The three steps are generic. The free <a href="/tools/dunning-email-generator/">dunning email generator</a> on this site actually produces better reason-aware copy than the product currently sends, which is an odd thing to admit and a fair reason to hold off if reason-aware copy is what you came for.</li>
      <li><strong>The sequence is not editable in the dashboard.</strong> Three steps, fixed wording.</li>
      <li><strong>No custom sending domain yet.</strong></li>
    </ul>""",
    faqs=[
        ("How many dunning emails does RecoverFlow send?",
         "Three steps. The first sends when the recovery case opens and the later ones follow the retry schedule, so the emails and the retries stay in step rather than running on separate clocks."),
        ("Can I edit the dunning email copy?",
         "Not yet. The three steps are currently fixed wording and do not vary by decline reason. This is the least finished part of the product."),
        ("Do the emails tell customers why their card was declined?",
         "Not for lost, stolen or blocked cards. Stripe's guidance is that naming the reason gives useful information to whoever holds a card they should not have, so those emails ask for updated details without stating the cause."),
        ("Will the email tell my customer their subscription is being cancelled?",
         "No. Whether a subscription cancels after failed payments is your own Stripe setting, which RecoverFlow does not see or control, so the final email says only that it is the last reminder we will send."),
    ]))

# --- attribution and billing -----------------------------------------------
PAGES.append(dict(
    slug="attribution-and-billing",
    title="How RecoverFlow attributes recoveries and bills | Docs",
    h1="Attribution and billing",
    desc="How a recovered payment is credited, the cases where you are not billed at all, and the exact numbers behind the 25% fee.",
    # The ceiling and the reversal handling shipped on 12 August, later than the
    # rest of the docs, so this page carries its own date.
    updated="12 August 2026",
    iso_modified="2026-08-12",
    body="""    <p>RecoverFlow charges 25% of what it recovers, with a $29 monthly floor after the first 30 days and a $299 monthly ceiling. That only works if "what it recovered" is decided honestly, so this page describes the rule precisely enough to argue with.</p>

    <h2 id="order">How a recovery is attributed</h2>
    <p>When an invoice we were working on gets paid, the cause is assigned in this order:</p>
    <ol>
      <li><strong>Retry.</strong> One of our attempts ran against the invoice. Credited to the retry.</li>
      <li><strong>Card update.</strong> No attempt ran, but the customer completed a card update through our flow.</li>
      <li><strong>Email.</strong> Neither of the above, but the customer had received at least one email from the sequence.</li>
      <li><strong>Nothing we did.</strong> None of the above applies. Recorded as recovered without our involvement, and <strong>not billable</strong>.</li>
    </ol>
    <div class="callout">
      <p><strong>The case that matters:</strong> an invoice we never touched that Stripe's own retries recovered is not something you pay us for. If it were, the pricing model would just be a tax on your existing Stripe setup.</p>
    </div>
    <p>The honest limitation: attribution to the email step is the weakest link in that chain. A customer who receives our email and then updates their card directly in your own billing portal, without touching our flow, is credited to the email. That is a judgement call, and it is the one we would expect a careful customer to push back on. If you think a specific recovery was miscredited, say which invoice and it gets looked at.</p>

    <h2 id="numbers">The numbers</h2>
    <div class="table-scroll">
      <table>
        <tbody>
          <tr><th scope="row">Performance fee</th><td>25% of attributably-recovered revenue</td></tr>
          <tr><th scope="row">Monthly minimum</th><td>$29, waived for the first 30 days after signup</td></tr>
          <tr><th scope="row">Monthly maximum</th><td>$299, however much is recovered. It applies from day one, including during the first 30 days.</td></tr>
          <tr><th scope="row">During those 30 days</th><td>The percentage still applies. The minimum does not.</td></tr>
          <tr><th scope="row">Invoice terms</th><td>Billed monthly through Stripe, 7 days to pay</td></tr>
          <tr><th scope="row">Very small totals</th><td>Stripe will not collect under $0.50, so amounts below that roll into the next month rather than being written off or rounded up</td></tr>
          <tr><th scope="row">Contract</th><td>None. No minimum term, no exit fee.</td></tr>
        </tbody>
      </table>
    </div>

    <h2 id="when-not">When 25% is the wrong deal</h2>
    <p>At high recovery volumes a percentage costs more than a flat monthly fee, and at that point you should be paying someone else or nobody. The ceiling limits how far that goes: past about $1,196 of attributed recovery in a month the fee stops rising at $299, so the most we can cost you is $299 and only a flat fee below that beats us. The <a href="/pricing/">pricing page</a> works through where that crossover sits, and the <a href="/tools/recovery-estimator/">recovery estimator</a> will tell you, using your own numbers, when a competitor is cheaper.</p>""",
    faqs=[
        ("Does RecoverFlow charge for payments that would have recovered anyway?",
         "Not when we can tell. If no retry ran, no card update completed through our flow, and no email had been sent, the recovery is recorded as happening without our involvement and is not billable."),
        ("How does RecoverFlow decide a recovery was caused by its email?",
         "Only when no retry attempt ran and no card update completed through our flow, but at least one sequence email had been sent. This is the weakest link in the attribution chain and the one most worth disputing if it looks wrong to you."),
        ("What does RecoverFlow cost?",
         "25% of attributably-recovered revenue, with a $29 monthly minimum that is waived for the first 30 days and a $299 monthly maximum that always applies. The percentage applies during those 30 days; only the minimum is waived. Invoices are issued monthly through Stripe with 7 day terms."),
        ("Is there a contract?",
         "No minimum term and no exit fee. You revoke access from your own Stripe dashboard and nothing further is billed beyond recoveries already attributed."),
    ]))

# --- backtest --------------------------------------------------------------
PAGES.append(dict(
    slug="backtest",
    title="The 90-day backtest | RecoverFlow docs",
    h1="The 90-day backtest",
    desc="What the connect-time scan reads, how the estimate is built from decline classification, and why it is a range rather than a number.",
    body="""    <p>The moment you connect Stripe, a one-off scan reads your recent history and shows you what already failed and what a recovery process might have won back. It is the fastest honest answer to "is this worth anything to me".</p>

    <h2 id="scope">What it reads</h2>
    <ul>
      <li><strong>90 days</strong> of history.</li>
      <li>Up to <strong>200 invoices</strong>. The cap exists so a large account cannot stall the job or exhaust Stripe rate limits. If your account is bigger than that, the scan is a sample and not a complete audit, and the result should be read that way.</li>
      <li>Failed invoices only, with their decline codes.</li>
    </ul>
    <p>It is read-only. The backtest never retries anything, never emails anyone, and never changes a subscription. It is looking at the past, not acting on it.</p>

    <h2 id="estimate">How the estimate is built</h2>
    <p>Each failed invoice is classified by decline code, and each classification carries a recovery range rather than a single figure:</p>
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Classification</th><th scope="col">Assumed recovery range</th></tr></thead>
        <tbody>
          <tr><th scope="row">Soft decline</th><td>30% to 50%</td></tr>
          <tr><th scope="row">Unrecognised code</th><td>15% to 30%</td></tr>
          <tr><th scope="row">Hard decline</th><td>0% to 5%</td></tr>
        </tbody>
      </table>
    </div>

    <h2 id="honest">What those percentages are, and are not</h2>
    <p>They are assumptions. They are not measured results from RecoverFlow customers, because there is not yet a body of customer results to measure. They are set deliberately low so that a real account is more likely to beat its backtest than miss it.</p>
    <p>You should treat the output as an order-of-magnitude answer to "is there enough failed revenue here to be worth anyone's time", and not as a forecast. Anyone showing you a single confident recovery percentage for your account before running your account is guessing too, with more confidence and less disclosure.</p>
    <div class="callout">
      <p><strong>Fixed on 28 July 2026:</strong> six decline codes were previously misclassified as retryable, which inflated the estimate for accounts holding those failures. They are now classified correctly. If you connected before that date, your backtest range was optimistic for those invoices, and re-running the scan will give you a lower and more accurate figure.</p>
    </div>""",
    faqs=[
        ("What does the RecoverFlow backtest actually do?",
         "It reads up to 200 failed invoices from the last 90 days of your Stripe history, classifies each by decline code, and applies a recovery range per classification to estimate what a recovery process might have won back. It is read-only and never retries or emails anyone."),
        ("Are the backtest recovery percentages based on real results?",
         "No. They are deliberately conservative assumptions, set low so a real account is more likely to beat its backtest than miss it. RecoverFlow does not yet have a body of customer results to measure against, and says so rather than publishing a number it cannot support."),
        ("Why is the backtest limited to 200 invoices?",
         "So a large account cannot stall the scan or exhaust Stripe API rate limits. If you have more failed invoices than that in 90 days, the result is a sample rather than a complete audit."),
        ("Does connecting Stripe for a backtest commit me to anything?",
         "No. The scan is read-only, there is no card required to start, and you revoke access from your own Stripe dashboard whenever you like."),
    ]))


# ---------------------------------------------------------------------------
# /changelog, from real git history
# ---------------------------------------------------------------------------

RELEASES = [
    ("2026-08-12", "A ceiling on the fee, and no billing for money handed back", [
        "The performance fee is now capped at $299 a month. Past roughly $1,196 of attributed recovery in a month the bill stops rising, however much more is recovered. The cap applies from the first day of the account, not only after the free 30 days.",
        "Recoveries that are later refunded or charged back are no longer billed. A fully reversed case drops out of billing entirely, a partly reversed one bills only on what the merchant kept, and a reversal found after the invoice went out comes off the next one as a credit.",
    ]),
    ("2026-07-28", "Correctness pass on decline handling", [
        "Fixed six Stripe decline codes being treated as retryable when Stripe will not execute a retry for them without a new payment method: incorrect_number, transaction_not_allowed, revocation_of_authorization, revocation_of_all_authorizations, authentication_required and highest_risk_level. This also inflated the connect-time backtest estimate for accounts holding those failures.",
        "Lost, stolen and retained cards now route to the card-update flow instead of being written off. A reissue is the most recoverable failure there is, because the customer usually already has the new card.",
        "card_velocity_exceeded is now retried. It is a limit that resets, and Stripe retries it.",
        "Dunning step 3 no longer tells customers their subscription will be cancelled. Whether that happens is the merchant's own Stripe setting, which RecoverFlow does not see or control.",
        "Published twelve guides at /blog, plus /about, /contact and this documentation.",
    ]),
    ("2026-07-27", "Comparison and tooling", [
        "Four competitor comparison pages with pricing read from each vendor's own page and dated on-page, plus a comparisons hub.",
        "Free recovery estimator and a searchable reference for all 48 Stripe decline codes.",
        "Retry schedule builder and dunning email generator, completing the free tool set.",
        "Pricing page, including a section on the volume at which 25% becomes the wrong deal for the customer.",
        "Privacy policy, terms of service and security pages, with governing law in Massachusetts.",
    ]),
    ("2026-07-13", "Backtest and analytics", [
        "Instant 90-day backtest scan on Stripe Connect, so a new merchant sees what already failed before committing to anything.",
        "DataProtection keys persisted across redeploys, so sessions survive a deployment.",
        "Analytics on the marketing site.",
    ]),
    ("2026-07-12", "Billing, auth and deliverability", [
        "Usage-based billing: 25% of recovered revenue with a $29 monthly floor.",
        "Magic-link authentication and the customer dashboard.",
        "Self-serve signup funnel.",
        "Dunning email deliverability fixes, including rejecting a placeholder SendGrid key at startup rather than silently sending nothing.",
        "Open and click tracking disabled on dunning emails at the SendGrid level.",
    ]),
    ("2026-07-10", "First working system", [
        "Clean Architecture scaffold on .NET 8, with PostgreSQL and EF Core.",
        "Stripe Connect OAuth, with the access token encrypted at rest.",
        "Multi-tenant isolation via EF Core global query filters, so a query that forgets its tenant filter returns nothing rather than another merchant's rows.",
        "Decline classification, retry scheduling and the retry execution worker.",
        "Dunning email sending through SendGrid.",
        "Read-only merchant dashboard API.",
        "Deployment to Render.",
    ]),
]


def build_changelog():
    blocks = []
    for date, title, items in RELEASES:
        lis = "\n".join(f"        <li>{i}</li>" for i in items)
        blocks.append(f"""    <div class="release">
      <p class="updated" style="margin-bottom:4px;">{date}</p>
      <h2 style="margin-top:0;">{title}</h2>
      <ul>
{lis}
      </ul>
    </div>""")
    body = f"""<main>
  <div class="wrap">
    <p class="eyebrow">Changelog</p>
    <h1>What has changed</h1>
    <p>Grouped by the day the work actually landed. Bug fixes that affected numbers shown to customers are listed as prominently as features, because those are the ones you would want to know about.</p>

{"".join(blocks)}

    <p style="margin-top:40px;color:var(--text-dim);">RecoverFlow started on 10 July 2026. Anything older than that does not exist.</p>
  </div>
</main>"""
    schema = [{
        "@context": "https://schema.org",
        "@type": "WebPage",
        "name": "RecoverFlow changelog",
        "url": f"{SITE}/changelog/",
        "description": "What changed in RecoverFlow and when, including fixes that affected numbers shown to customers.",
    }]
    return shell("Changelog | RecoverFlow",
                 "What changed in RecoverFlow and when. Bug fixes that affected numbers shown to customers are listed as prominently as features.",
                 f"{SITE}/changelog/", body, schema)


AUDIT_BODY = """<main>
  <div class="wrap">
    <p class="eyebrow">Free</p>
    <h1>I will tell you what failed payments are costing you</h1>

    <div class="answer">
      <span class="label">The offer</span>
      <p>Answer three questions in a reply. I send back a written breakdown of what you are probably losing to failed subscription payments, how much of it is genuinely recoverable, and whether Stripe's own free features already handle it.</p>
      <p>No Stripe connection. No account. No card. No call unless you want one.</p>
    </div>

    <h2 id="questions">The three questions</h2>
    <ol>
      <li><strong>Roughly what is your monthly recurring revenue?</strong> A range is fine. "Somewhere around $40k" is enough.</li>
      <li><strong>Roughly how many subscription payments fail in a month?</strong> If you do not know, say so. That is a useful answer too, and it is more common than you would think.</li>
      <li><strong>What happens today when one fails?</strong> Stripe's default retries, a tool you already pay for, something you built, or nothing in particular.</li>
    </ol>

    <h2 id="back">What you get back</h2>
    <ul>
      <li>What that failure volume is costing you a month and a year, with the arithmetic written out so you can check every step.</li>
      <li>What Stripe already does for you for free, which is more than most people realise.</li>
      <li>A second question that turns the estimate into something precise. More on that below.</li>
    </ul>

    <h2 id="two-step">Why it takes two replies, not one</h2>
    <p>Three numbers tell me your <em>volume</em>. They cannot tell me your <em>decline mix</em>, and the mix is what actually matters, because it decides whether you have a retry problem or an email problem. Those need completely different fixes and one of them is free.</p>
    <p>I could put an industry-average percentage in the first reply. I am not going to, because I would be making it up, and a number I cannot source is worth less than no number at all.</p>
    <p>So the first reply gives you the cost, and asks you to do one five-minute thing: open Stripe, filter invoices to unpaid, look at the last twenty or so, and tell me which decline codes come up. The <a href="/tools/decline-code-lookup/">free lookup on this site</a> explains what each one means if any are unfamiliar.</p>
    <p>With that, the second reply tells you how much of your failed revenue is reachable at all, how much needs a new card rather than another attempt, and whether Stripe's free retries already cover the reachable part. Every figure traces back to your data rather than to a benchmark somebody invented.</p>

    <h2 id="faster">If you would rather not wait for me</h2>
    <p>The single most useful number here is what share of your failures are on cards that are <em>gone</em> rather than cards that are <em>short</em>. Stripe will not send nine decline codes to the bank at all, so retries against them never actually happen, whatever your settings say. Nearly every tool in this category answers that with more retries.</p>
    <p>The <a href="/tools/retry-waste-calculator/">retry waste calculator</a> works that out from your decline counts in about two minutes, in your browser, with nothing uploaded. If it tells you most of your failures are the ordinary retryable kind, Stripe's own free settings already cover you and you can stop reading.</p>

    <h2 id="catch">The catch, stated plainly</h2>
    <p>I built RecoverFlow, so I am obviously not a neutral party. Two things make this worth your fifteen minutes anyway.</p>
    <p>The first is that the numbers are yours and the arithmetic is shown, so you can check every step and ignore my conclusion if you disagree with it.</p>
    <p>The second is that a real fraction of these end with me saying you do not need this. Stripe's built-in Smart Retries are genuinely capable, they cost nothing, and if your failures are mostly the kind Stripe already recovers then buying anything is a waste of your money. There is <a href="/compare/stripe-native/">a whole page here</a> about when that is the right call, written before I had any customers to lose by saying so.</p>

    <h2 id="who">Who is on the other end</h2>
    <p>Me. Bruce McGinley, one person in Massachusetts. Not a sales team, not a sequence, not a chatbot. If you reply, I read it and write back. More on the <a href="/about/">about page</a>, including what this product does not have yet.</p>

    <div class="cta-band">
      <h2>Send me the three answers</h2>
      <p>Email <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a> with your MRR, roughly how many payments fail a month, and what you currently do about it. I will reply with the breakdown.</p>
      <p style="color:var(--text-dim);">If you would rather work it out yourself, the <a href="/tools/recovery-estimator/">recovery estimator</a> does the same arithmetic in your browser and stores nothing.</p>
      <a class="btn" href="mailto:admin@recoverflow.org?subject=Failed%20payment%20audit">Email me the three answers</a>
    </div>
  </div>
</main>"""

AUDIT_FAQS = [
    ("Does the free audit require connecting my Stripe account?",
     "No. It runs entirely off three numbers you send in a reply: your rough MRR, roughly how many subscription payments fail per month, and what you currently do when one fails. No Stripe access, no account and no card."),
    ("What does the audit actually tell me?",
     "What your failure volume is likely costing per year with the arithmetic shown, roughly how much of it sits on decline codes no retry can fix, what Stripe already does for free, and whether any paid tool is worth it for you."),
    ("What if I do not know how many payments fail?",
     "Say so. It is a common answer and still useful, because not knowing is itself the finding. Stripe's dashboard can show you, and the audit explains where to look."),
    ("Is this just a sales pitch?",
     "Partly, obviously, since I built the product. But the numbers are yours and the arithmetic is shown so you can check it, and a real share of these end with me saying Stripe's free features are enough and you should not buy anything."),
]


# ---------------------------------------------------------------------------
# /tools/retry-waste-calculator
# ---------------------------------------------------------------------------

WASTE_FAQS = [
    ("What counts as a wasted retry?",
     "An attempt against a decline code that cannot succeed no matter how many times you try. Nine codes block execution at Stripe entirely until a new card is attached, and four more (expired card, incorrect CVC, invalid expiry) do get retried but cannot succeed until the customer replaces the card. Attempts against those are spent, not invested."),
    ("Where do I find my decline codes?",
     "In the Stripe Dashboard, filter Payments to failed, or filter Invoices to unpaid and open the latest payment attempt on each. The decline code sits on the failed PaymentIntent as last_payment_error.decline_code. Twenty invoices is enough for a useful picture."),
    ("Is there really a cap on retries?",
     "Yes. Visa's Excessive Reattempts Rule limits reattempts to 15 per card per 30 days, effective 17 April 2021 per Visa's own bulletin. Stripe blocks further attempts once you pass it. Mastercard runs a separate excessive-authorisation fee with a different threshold and a different window, repriced periodically, so a Visa-shaped budget does not describe it. Most recovery tools do not show you where you stand against either."),
    ("Does this send my data anywhere?",
     "No. The calculation runs in your browser. Nothing is uploaded, stored or logged, and there is no form to submit."),
]

WASTE_BODY = """<main>
  <div class="wrap">
    <p class="eyebrow">Free tool</p>
    <h1>How many of your Stripe retries were never going to work?</h1>

    <div class="answer">
      <span class="label">The short version</span>
      <p>Every failed payment tool on the market sells you more retries. Some of your declines cannot succeed on any attempt, ever, because the card is gone rather than temporarily short.</p>
      <p>Put in the counts from your last twenty or so failed invoices and this tells you how much of your failed revenue is in that group. Nothing is uploaded.</p>
    </div>

    <h2 id="calc">Enter your decline codes</h2>
    <p>Counts from your Stripe Dashboard. Leave anything at zero if you did not see it.</p>

    <div class="calc" id="calc">
      <div class="grid" id="codeGrid"></div>
      <label for="avg">Average failed invoice amount</label>
      <input id="avg" type="number" min="0" step="1" value="79" inputmode="decimal">
      <button id="run" class="btn" type="button">Work it out</button>
    </div>

    <div id="out" hidden></div>

    <h2 id="why">Why this is the number that matters</h2>
    <p>Failed payments split into two groups that need opposite fixes, and almost every tool in this category treats them the same.</p>
    <p>One group is temporary. The bank balance was short, the processor glitched, the issuer was cautious. Waiting and trying again genuinely works, and Stripe's own free Smart Retries already do this well.</p>
    <p>The other group is a card that is gone. Reported lost or stolen, reissued, expired, revoked, or blocked for this merchant. <strong>No retry schedule can collect from a card that no longer exists.</strong> Stripe will not even send nine of those codes to the network. It keeps incrementing the attempt counter while nothing is attempted.</p>
    <p>If most of your failed revenue is in the second group, buying a tool that retries harder is buying a solution to the wrong half of your problem. That is worth knowing before you spend anything, including with us.</p>

    <h2 id="cap">There is also a ceiling you may be hitting</h2>
    <p>Visa's Excessive Reattempts Rule caps reattempts at 15 per card per 30 days. It took effect on 17 April 2021, a date worth stating precisely because April 2022 is repeated everywhere and belongs to a later fee phase rather than to the rule. Stripe blocks further attempts once you cross it. The <a href="/blog/visa-excessive-reattempts-rule/">full rule is its own page</a>, and you can <a href="/tools/visa-retry-budget/">check your own peak against the cap</a>.</p>
    <p>Mastercard operates its own excessive-authorisation fee, and it is not the same shape: a different attempt threshold, a shorter window, and a schedule the network reprices periodically. <strong>This calculator does not model it.</strong> Published third-party figures for the Mastercard threshold and the per-attempt amount disagree with each other, and this site does not print a number it cannot put a primary source behind, so treat the Visa budget as a floor on your exposure rather than the whole of it.</p>
    <p>A third-party recovery tool retrying on top of Stripe's own retries draws down the same budget, and neither system can see the other's count. More attempts is not a free action.</p>

    <div class="cta-band">
      <h2>Want this run against your real data?</h2>
      <p>Send your rough MRR, roughly how many payments fail a month, and what happens today when one does. I will send back a written breakdown with the arithmetic shown. No Stripe access, no signup, no call.</p>
      <p style="color:var(--text-dim);">Sometimes the answer is that Stripe's free settings already cover you, and I will say so.</p>
      <a class="btn" href="/audit/">See what the free audit covers</a>
    </div>
  </div>
</main>"""

WASTE_JS = """
<script>
(function () {
  var BLOCKED = [
    ['lost_card','Lost card'],['stolen_card','Stolen card'],['pickup_card','Pickup card'],
    ['incorrect_number','Incorrect number'],['transaction_not_allowed','Transaction not allowed'],
    ['authentication_required','Authentication required'],['highest_risk_level','Highest risk level'],
    ['revocation_of_authorization','Revocation of authorization'],
    ['revocation_of_all_authorizations','Revocation of all authorizations']
  ];
  var NEWCARD = [
    ['expired_card','Expired card'],['incorrect_cvc','Incorrect CVC'],
    ['invalid_expiry_month','Invalid expiry month'],['invalid_expiry_year','Invalid expiry year']
  ];
  var SOFT = [
    ['insufficient_funds','Insufficient funds'],['do_not_honor','Do not honor'],
    ['generic_decline','Generic decline'],['processing_error','Processing error'],
    ['try_again_later','Try again later'],['card_velocity_exceeded','Card velocity exceeded'],
    ['card_declined','Card declined']
  ];
  var ALL = BLOCKED.concat(NEWCARD, SOFT);
  var grid = document.getElementById('codeGrid');
  ALL.forEach(function (c) {
    var w = document.createElement('div');
    w.className = 'field';
    w.innerHTML = '<label for="c_' + c[0] + '">' + c[1] + '</label>' +
      '<input id="c_' + c[0] + '" type="number" min="0" step="1" value="0" inputmode="numeric">';
    grid.appendChild(w);
  });

  function n(id) { var v = parseInt((document.getElementById(id) || {}).value, 10); return isNaN(v) || v < 0 ? 0 : v; }
  function money(x) { return '$' + Math.round(x).toLocaleString(); }
  function sum(list) { var t = 0; list.forEach(function (c) { t += n('c_' + c[0]); }); return t; }

  document.getElementById('run').addEventListener('click', function () {
    var b = sum(BLOCKED), nc = sum(NEWCARD), s = sum(SOFT), total = b + nc + s;
    var avg = parseFloat(document.getElementById('avg').value) || 0;
    var out = document.getElementById('out');
    out.hidden = false;
    if (!total) {
      out.innerHTML = '<div class="callout"><p>Enter at least one decline count above.</p></div>';
      return;
    }
    var wasted = b + nc, pct = wasted / total * 100;
    var verdict;
    if (pct >= 50) {
      verdict = '<p><strong>More than half of your failed revenue cannot be retried into existence.</strong> ' +
        'This is an email problem wearing a retry problem\\'s clothes. A tool that retries harder will not move this number. ' +
        'What moves it is what you say to these customers, and when.</p>';
    } else if (pct >= 25) {
      verdict = '<p><strong>A meaningful minority is unreachable by retrying.</strong> Stripe\\'s free Smart Retries should ' +
        'handle most of the rest, so check your retry window is on the recommended 8 attempts over 2 weeks before ' +
        'paying anyone for retry logic. The ' + Math.round(pct) + '% above is where a tool could actually help.</p>';
    } else {
      verdict = '<p><strong>Most of your failures are genuinely retryable</strong>, which is exactly what Stripe\\'s free ' +
        'Smart Retries are built for. Check your settings are on 8 attempts over 2 weeks. If they are, you may not ' +
        'need to buy anything at all, and we would rather tell you that than sell you something.</p>';
    }
    out.innerHTML =
      '<div class="result">' +
      '<h2>Your ' + total + ' failed payments</h2>' +
      '<div class="rows">' +
      '<div class="row"><span>Could never succeed on any retry</span><strong>' + b + ' (' + Math.round(b / total * 100) + '%)</strong></div>' +
      '<div class="row"><span>Retried by Stripe, but need a new card first</span><strong>' + nc + ' (' + Math.round(nc / total * 100) + '%)</strong></div>' +
      '<div class="row"><span>Genuinely reachable by retrying</span><strong>' + s + ' (' + Math.round(s / total * 100) + '%)</strong></div>' +
      '</div>' +
      (avg ? '<p class="big">' + money(wasted * avg) + ' of every ' + money(total * avg) +
        ' needed a new card, not another attempt.</p>' : '') +
      verdict +
      '<p style="color:var(--text-dim);font-size:.9rem;">Arithmetic on the numbers you entered. No assumed recovery rates ' +
      'are used anywhere on this page, because we have none we could substantiate.</p>' +
      '</div>';
  });
})();
</script>
<style>
  .calc { background: var(--card-bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 20px; margin: 18px 0; }
  .calc .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: 12px; margin-bottom: 16px; }
  .calc label { display: block; font-size: .82rem; color: var(--text-dim); margin-bottom: 4px; }
  .calc input { width: 100%; padding: 8px 10px; border-radius: 8px; border: 1px solid var(--border-strong); background: var(--bg); color: var(--text); font: inherit; }
  .result { background: var(--card-bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 20px; margin: 18px 0; }
  .result .rows { margin: 12px 0; }
  .result .row { display: flex; justify-content: space-between; gap: 12px; padding: 8px 0; border-bottom: 1px solid var(--border); }
  .result .row:last-child { border-bottom: 0; }
  .result .big { font-size: 1.25rem; font-weight: 700; letter-spacing: -.02em; margin: 14px 0; }
</style>"""


def write(path_slug, html):
    out = os.path.join(DOCS, *path_slug.split("/")) if path_slug else DOCS
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    print("built", f"{SITE}/{path_slug}/")


if __name__ == "__main__":
    for p in PAGES:
        slug = f"docs/{p['slug']}" if p["slug"] else "docs"
        write(slug, build_doc(p["slug"], p["title"], p["h1"], p["desc"], p["body"], p.get("faqs"),
                              p.get("updated"), p.get("iso_modified", "2026-07-28")))
    write("changelog", build_changelog())

    audit_schema = [{
        "@context": "https://schema.org",
        "@type": "Service",
        "name": "Free failed payment audit",
        "provider": {"@type": "Person", "name": "Bruce McGinley"},
        "description": "A written analysis of what failed Stripe subscription payments are costing you, from three numbers you send by email. No Stripe connection required.",
        "url": f"{SITE}/audit/",
        "areaServed": "US",
    }, {
        "@context": "https://schema.org",
        "@type": "FAQPage",
        "mainEntity": [{"@type": "Question", "name": q,
                        "acceptedAnswer": {"@type": "Answer", "text": a}} for q, a in AUDIT_FAQS],
    }]
    faq = "\n".join(['<h2 id="faq">Common questions</h2>', '<div class="faq">']
                    + [f"  <details><summary>{q}</summary><p>{a}</p></details>" for q, a in AUDIT_FAQS]
                    + ["</div>"])
    write("audit", shell(
        "Free failed payment audit | RecoverFlow",
        "Send three numbers, get a written breakdown of what failed Stripe payments cost you and whether Stripe's free features already cover it. No connection needed.",
        f"{SITE}/audit/", AUDIT_BODY.replace("</div>\n</main>", f"{faq}\n  </div>\n</main>"), audit_schema))

    waste_schema = [{
        "@context": "https://schema.org",
        "@type": "WebApplication",
        "name": "Stripe retry waste calculator",
        "applicationCategory": "BusinessApplication",
        "operatingSystem": "Any",
        "url": f"{SITE}/tools/retry-waste-calculator/",
        "description": "Work out how much of your failed Stripe revenue sits on decline codes that no retry can ever collect. Runs in your browser, nothing uploaded.",
        "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"},
    }, {
        "@context": "https://schema.org",
        "@type": "FAQPage",
        "mainEntity": [{"@type": "Question", "name": q,
                        "acceptedAnswer": {"@type": "Answer", "text": a}} for q, a in WASTE_FAQS],
    }]
    wfaq = "\n".join(['<h2 id="faq">Common questions</h2>', '<div class="faq">']
                     + [f"  <details><summary>{q}</summary><p>{a}</p></details>" for q, a in WASTE_FAQS]
                     + ["</div>"])
    write("tools/retry-waste-calculator", shell(
        "How many of your Stripe retries are wasted? | Free calculator",
        "Free browser calculator. Enter your decline codes and see how much failed revenue sits on cards no retry can ever collect. Nothing uploaded.",
        f"{SITE}/tools/retry-waste-calculator/",
        WASTE_BODY.replace("</div>\n</main>", f"{wfaq}\n  </div>\n</main>") + WASTE_JS,
        waste_schema))

    print(f"\n{len(PAGES)} doc pages + changelog + audit + retry waste calculator")
