#!/usr/bin/env python3
"""
Generates the informational content surface for recoverflow.org:

  /about/     /contact/     /blog/     and the articles under /blog/<slug>/

Every factual claim about Stripe's behaviour in these articles was re-checked
against Stripe's own documentation on 2026-07-28. The load bearing ones are:

  * Smart Retries default is 8 tries within 2 weeks, window configurable to
    1 week, 2 weeks, 3 weeks, 1 month or 2 months.
  * Exactly nine decline codes stop Stripe executing a retry until a new
    payment method exists. That list is in HARD_CODES below and nowhere else,
    so it cannot drift between articles.

There are no invented customers, no invented recovery rates and no "industry
average" percentages. Where a number would be useful and we do not have a
trustworthy one, the article says so and gives the reader the arithmetic to
work it out from their own Stripe data instead.

Run from the repo root:  python scripts/build_content_pages.py
"""

import os
import sys
import glob
import json
import re

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from build_legal_pages import TRACKING, CSS, HEADER, FOOTER, DOCS, UPDATED  # noqa: E402

SITE = "https://recoverflow.org"

# The single source of truth for the hard decline list. Verified 28 July 2026
# against https://docs.stripe.com/billing/revenue-recovery/smart-retries
HARD_CODES = [
    ("incorrect_number", "The card number itself is wrong. No amount of waiting fixes a typo."),
    ("lost_card", "The cardholder reported the card lost. The issuer will keep saying no."),
    ("pickup_card", "The issuer wants the physical card retained. Treat it as permanently dead."),
    ("stolen_card", "Reported stolen. Never tell the customer this is the reason."),
    ("revocation_of_authorization", "The customer told their bank to stop this specific merchant."),
    ("revocation_of_all_authorizations", "The customer told their bank to stop all recurring charges."),
    ("authentication_required", "The charge needs SCA. Only the cardholder can complete it."),
    ("highest_risk_level", "Stripe Radar blocked it. Retrying does not lower the risk score."),
    ("transaction_not_allowed", "The issuer does not permit this type of transaction on this card."),
]

EXTRA_CSS = """
  .answer { border-left: 4px solid var(--accent); background: var(--bg-alt); padding: 20px 24px; border-radius: 0 var(--radius) var(--radius) 0; margin: 28px 0 34px; }
  .answer .label { font-family: var(--font-display); font-size: .72rem; letter-spacing: .09em; text-transform: uppercase; color: var(--accent); font-weight: 700; display: block; margin-bottom: 8px; }
  .answer p { margin: 0 0 10px; font-size: 1.06rem; }
  .answer p:last-child { margin-bottom: 0; }
  .faq { margin-top: 12px; }
  .faq details { border: 1px solid var(--border); border-radius: var(--radius); padding: 14px 18px; margin-bottom: 10px; background: var(--card-bg); }
  .faq summary { font-family: var(--font-display); font-weight: 600; cursor: pointer; }
  .faq details p { margin: 12px 0 2px; color: var(--text-dim); }
  .sources { border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 22px; margin-top: 40px; background: var(--bg-alt); }
  .sources h2 { margin-top: 0; font-size: 1.05rem; }
  .sources ul { margin: 0; padding-left: 20px; }
  .sources li { margin-bottom: 7px; color: var(--text-dim); font-size: .93rem; }
  .related { margin-top: 40px; }
  .card-grid { display: grid; gap: 16px; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); margin-top: 18px; }
  .card { border: 1px solid var(--border); border-radius: var(--radius); padding: 20px; background: var(--card-bg); }
  .card h3 { margin: 0 0 8px; font-size: 1.02rem; }
  .card h3 a { text-decoration: none; }
  .card p { margin: 0; color: var(--text-dim); font-size: .93rem; }
  .meta { color: var(--text-mute); font-size: .88rem; margin-bottom: 4px; }
  .cta-band { border: 1px solid var(--border-strong); border-radius: var(--radius); padding: 24px; margin-top: 44px; background: var(--bg-alt); }
  .cta-band h2 { margin-top: 0; }
  .cta-band .btn { display: inline-block; background: var(--accent-grad); color: var(--accent-ink); padding: 11px 22px; border-radius: 10px; text-decoration: none; font-weight: 600; font-family: var(--font-display); margin-top: 6px; }
  code { font-family: var(--font-mono); font-size: .92em; background: var(--bg-alt); padding: 2px 6px; border-radius: 5px; }
"""


def plain(s):
    """Strip inline tags so a heading can be reused inside JSON-LD."""
    return re.sub(r"<[^>]+>", "", s)


def shell(title, desc, canonical, body, schema=None):
    ld = ""
    if schema:
        for s in schema:
            ld += '\n<script type="application/ld+json">%s</script>' % json.dumps(s, indent=2)
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
{TRACKING}
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{title}</title>
<meta name="description" content="{desc}">
<link rel="canonical" href="{canonical}">
<link rel="icon" type="image/png" href="/assets/logo-mark.png">
<meta property="og:type" content="article">
<meta property="og:site_name" content="RecoverFlow">
<meta property="og:title" content="{title}">
<meta property="og:description" content="{desc}">
<meta property="og:url" content="{canonical}">
<meta property="og:image" content="{SITE}/assets/og-image.png">
<meta name="twitter:card" content="summary_large_image">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&family=Space+Grotesk:wght@500;600;700&display=swap" rel="stylesheet">
<style>{CSS}{EXTRA_CSS}</style>{ld}
</head>
<body>

{HEADER}

{body}

{FOOTER}

</body>
</html>
"""


def article_schema(slug, h1, desc, published="2026-07-28", modified="2026-07-28"):
    return {
        "@context": "https://schema.org",
        "@type": "Article",
        "headline": plain(h1),
        "description": desc,
        "url": f"{SITE}/blog/{slug}/",
        "datePublished": published,
        "dateModified": modified,
        "author": {"@type": "Person", "name": "Bruce McGinley"},
        "publisher": {"@type": "Organization", "name": "RecoverFlow", "url": SITE},
        "mainEntityOfPage": {"@type": "WebPage", "@id": f"{SITE}/blog/{slug}/"},
    }


def faq_schema(faqs):
    return {
        "@context": "https://schema.org",
        "@type": "FAQPage",
        "mainEntity": [
            {"@type": "Question", "name": q,
             "acceptedAnswer": {"@type": "Answer", "text": a}}
            for q, a in faqs
        ],
    }


def faq_html(faqs):
    if not faqs:
        return ""
    out = ['<h2 id="faq">Questions people actually ask</h2>', '<div class="faq">']
    for q, a in faqs:
        out.append(f"  <details><summary>{q}</summary><p>{a}</p></details>")
    out.append("</div>")
    return "\n".join(out)


def sources_html(sources):
    if not sources:
        return ""
    items = "\n".join(
        f'      <li><a href="{u}" rel="nofollow noopener" target="_blank">{t}</a>{note}</li>'
        for t, u, note in sources
    )
    return f"""<div class="sources">
  <h2>Where this came from</h2>
  <p style="margin-top:0;color:var(--text-dim);font-size:.93rem;">Checked against primary sources on {UPDATED}. If Stripe changes something and this page has not caught up, tell us and it gets fixed.</p>
  <ul>
{items}
  </ul>
</div>"""


def related_html(items):
    cards = "\n".join(
        f'    <div class="card"><h3><a href="{u}">{t}</a></h3><p>{d}</p></div>'
        for t, u, d in items
    )
    return f"""<div class="related">
  <h2>Related</h2>
  <div class="card-grid">
{cards}
  </div>
</div>"""


# The button here points at the free audit rather than at pricing, and that is deliberate.
# Every one of these 26 guides is written for someone still working out whether they have a
# failed-payment problem at all, which is a question the audit answers for nothing and the
# pricing page cannot answer at any price. Sending that reader to a page that asks for money
# skips the step they are actually on. Pricing stays linked in the prose below, and from the
# nav and the related-links block, so nothing is lost by demoting it out of the button.
#
# The pricing sentence is worded to match /pricing/ word for word, and is the same sentence the 14
# hand-written decline-code posts carry. It used to claim the product costs nothing in a month it
# recovers nothing, which is false once the first 30 days are up: /pricing/ says a quiet month
# costs $29. Do not reword it here without changing /pricing/ in the same commit.
CTA = """<div class="cta-band">
  <h2>If you would rather not build this yourself</h2>
  <p>RecoverFlow watches your Stripe account for failed subscription payments, stops retrying the ones that cannot succeed, and emails the customers whose card simply needs replacing. It charges 25% of what it can attribute to a specific action it took, with a $29 monthly floor and a $299 monthly ceiling, and the floor is waived for the first 30 days.</p>
  <p>Before any of that, you can find out whether it is worth doing at all. Answer three questions in a reply and I will send back what your failed payments are probably costing you, how much of it is genuinely recoverable, and whether Stripe's own free features already handle it. No Stripe connection, no account, no card.</p>
  <p style="color:var(--text-dim);">It is early. It is run by one person. If Stripe's own free retry settings are enough for you, use those instead, and <a href="/pricing/">the pricing page</a> says exactly when that is the right call.</p>
  <a class="btn" href="/audit/">Get the free audit</a>
</div>"""


def build_article(slug, title, h1, desc, answer, sections, faqs, sources, related,
                  updated=None, published="2026-07-28", modified="2026-07-28"):
    # updated/published/modified are optional so a guide written later than the
    # original batch can carry its own date instead of inheriting 28 July 2026.
    # A page that says "last updated 4 August" above content written on the 16th
    # is a small lie that a careful reader will find, and this site cannot afford
    # to be caught in one.
    updated = updated or UPDATED
    toc = "\n".join(f'        <li><a href="#{sid}">{head}</a></li>' for sid, head, _ in sections)
    body_sections = "\n\n".join(
        f'    <h2 id="{sid}">{head}</h2>\n{html}' for sid, head, html in sections
    )
    body = f"""<main>
  <article class="wrap">
    <p class="eyebrow">Guide</p>
    <h1>{h1}</h1>
    <p class="updated">Last updated {updated} &middot; written by Bruce McGinley, who builds RecoverFlow</p>

    <div class="answer">
      <span class="label">Short answer</span>
{answer}
    </div>

    <div class="toc">
      <p>On this page</p>
      <ol>
{toc}
        <li><a href="#faq">Questions people actually ask</a></li>
      </ol>
    </div>

{body_sections}

    {faq_html(faqs)}

    {sources_html(sources)}

    {CTA}

    {related_html(related)}
  </article>
</main>"""
    schema = [article_schema(slug, h1, desc, published, modified)]
    if faqs:
        schema.append(faq_schema(faqs))
    return shell(title, desc, f"{SITE}/blog/{slug}/", body, schema)


# ---------------------------------------------------------------------------
# Reusable fragments
# ---------------------------------------------------------------------------

def hard_code_table():
    rows = "\n".join(
        f"      <tr><th scope=\"row\"><code>{c}</code></th><td>{d}</td></tr>"
        for c, d in HARD_CODES
    )
    return f"""<div class="table-scroll">
  <table>
    <thead><tr><th scope="col">Decline code</th><th scope="col">What it means and why waiting will not help</th></tr></thead>
    <tbody>
{rows}
    </tbody>
  </table>
</div>"""


SRC_SMART = ("Stripe: Smart Retries", "https://docs.stripe.com/billing/revenue-recovery/smart-retries",
             " &mdash; the source for the 8 tries in 2 weeks default, the configurable window, and the nine code list.")
SRC_DECLINES = ("Stripe: Declines", "https://docs.stripe.com/declines", "")
SRC_CODES = ("Stripe: Decline codes reference", "https://docs.stripe.com/declines/codes", "")
SRC_EMAILS = ("Stripe: Customer emails", "https://docs.stripe.com/billing/revenue-recovery/customer-emails", "")
SRC_SUBS = ("Stripe: Subscription lifecycle and statuses", "https://docs.stripe.com/billing/subscriptions/overview",
            " &mdash; the source for every status definition quoted on this page.")
SRC_WEBHOOKS = ("Stripe: Subscription webhooks", "https://docs.stripe.com/billing/subscriptions/webhooks", "")
SRC_INVOICE = ("Stripe: Invoice object, API reference", "https://docs.stripe.com/api/invoices/object",
               " &mdash; the field definitions for attempt_count, next_payment_attempt, attempted and billing_reason.")
SRC_TESTING = ("Stripe: Testing", "https://docs.stripe.com/testing", " &mdash; where every test card number here comes from.")
SRC_CARDUPDATE = ("Stripe: Card payments overview", "https://docs.stripe.com/payments/cards/overview",
                  " &mdash; covers automatic card updates and network participation.")
SRC_EXCESSIVE = ("Stripe Support: Payment blocked due to excessive retries",
                 "https://support.stripe.com/questions/payment-blocked-due-to-excessive-retries",
                 " &mdash; the source for the 15 in 30 calendar days figure, for what Stripe does after the 15th attempt, and for the blocked outcome fields.")
SRC_VISA_RESUB = ("Visa: Updates to rules for declined transaction resubmission and use of authorization response codes",
                  "https://usa.visa.com/dam/VCOM/global/support-legal/documents/updates-to-rules-for-declined-transaction-resubmission-and-use-of-authorization-response-codes.pdf",
                  " &mdash; Visa's own document behind the reattempt rule and the response code categories.")
SRC_VISA_CATS = ("CardPointe: Visa decline rules and responses",
                 "https://support.cardpointe.com/compliance/visa-decline-rules-and-responses/",
                 " &mdash; a processor's published summary of Visa's four response code categories, used here for the Category 1 definition and nothing else.")
SRC_CONNECT_OAUTH = ("Stripe: Using OAuth with Standard accounts",
                     "https://docs.stripe.com/connect/oauth-standard-accounts",
                     " &mdash; the source for the account.application.deauthorized event.")
SRC_INDIA = ("Stripe: India recurring payments", "https://docs.stripe.com/india-recurring-payments",
             " &mdash; the RBI e-mandate rules behind Stripe not retrying India-issued cards.")


# ---------------------------------------------------------------------------
# Articles
# ---------------------------------------------------------------------------

ARTICLES = []

# 1 ------------------------------------------------------------------------
ARTICLES.append(dict(
    slug="stripe-decline-codes-that-stop-retries",
    title="The 9 Stripe decline codes that stop retries | RecoverFlow",
    h1="The nine Stripe decline codes that stop retries dead",
    desc="Stripe will not retry nine specific decline codes until a new card is added. The exact list, what each one means, and what to do instead of retrying.",
    answer="""      <p>There are exactly nine decline codes where Stripe will keep a subscription's retry schedule running but will not actually attempt the charge until a new payment method exists on the customer.</p>
      <p>They are <code>incorrect_number</code>, <code>lost_card</code>, <code>pickup_card</code>, <code>stolen_card</code>, <code>revocation_of_authorization</code>, <code>revocation_of_all_authorizations</code>, <code>authentication_required</code>, <code>highest_risk_level</code> and <code>transaction_not_allowed</code>.</p>
      <p>If you see one of these, the retry is not the lever. Getting a new card on file is the only thing that changes the outcome.</p>""",
    sections=[
        ("list", "The list", hard_code_table() + """
    <p>That is the complete list as documented by Stripe. Every other decline code is one Stripe will keep attempting on schedule.</p>"""),
        ("mechanics", "What actually happens when one of these fires", """
    <p>This is the part that confuses people looking at their own data. Stripe does not cancel the retry schedule when it sees one of these codes. The invoice stays open, the attempt counter keeps climbing, and the schedule keeps ticking. What changes is that Stripe stops sending the charge to the network, because it already knows the answer.</p>
    <p>So an invoice can show several attempts against a stolen card without a single one of them ever having reached the issuer. If you are counting attempts as a measure of effort, you will overcount. If you are counting them as a measure of hope, you will overcount badly.</p>
    <div class="callout">
      <p><strong>The practical consequence:</strong> your retry configuration has no effect on these invoices. Widening the retry window from two weeks to two months does not give them more chances. It gives them a longer wait before Stripe gives up.</p>
    </div>
    <p>Worth separating from a limit it is often confused with. This block is Stripe deciding not to send the charge. Visa separately caps how many attempts one payment may have at all, and that budget is drawn down by every attempt that does go out, from Stripe and from anything else retrying alongside it. <a href="/blog/visa-excessive-reattempts-rule/">The reattempt rule is its own page</a>, and if no retry is being scheduled in the first place, the <a href="/blog/stripe-not-retrying-failed-invoice/">nine reasons Stripe stops retrying</a> is the faster place to start.</p>"""),
        ("do-instead", "What to do instead", """
    <p>All nine resolve the same way: a new payment method. What differs is how you should ask for it.</p>
    <ul>
      <li><strong><code>incorrect_number</code></strong> is usually a typo during signup. Ask plainly, mention the card ending, and make the update link one click. These convert well because nothing is actually wrong with the customer's relationship to you.</li>
      <li><strong><code>lost_card</code>, <code>pickup_card</code> and <code>stolen_card</code></strong> mean the customer almost certainly already has a replacement card in their wallet. They just have not told you. This is the highest value group to email, and the easiest to recover, because the fix costs them thirty seconds.</li>
      <li><strong><code>revocation_of_authorization</code> and <code>revocation_of_all_authorizations</code></strong> are different in kind. The customer went to their bank and told it to block you, or to block everything recurring. Treat the first as a churn signal and the second as a life event. Emailing is still fine. Emailing five times is not.</li>
      <li><strong><code>authentication_required</code></strong> means the transaction needs Strong Customer Authentication and only the cardholder can complete it. Stripe can send a link that lets them authenticate. That link, not another retry, is the whole job.</li>
      <li><strong><code>highest_risk_level</code></strong> is Radar declining the charge, not the bank. Retrying does not lower a risk score. If you believe it is a false positive, the fix is in your Radar rules, not in your dunning.</li>
      <li><strong><code>transaction_not_allowed</code></strong> usually means the card cannot do recurring, cross border, or this merchant category. A different card fixes it. The same card never will.</li>
    </ul>"""),
        ("never-say", "One thing never to put in the email", """
    <p>For <code>lost_card</code>, <code>pickup_card</code>, <code>stolen_card</code> and <code>highest_risk_level</code>, do not tell the customer the reason. Stripe's own guidance is that revealing these gives useful feedback to whoever is holding a card they should not have.</p>
    <p>Write the generic version instead. Something like "we were not able to complete the payment on your card ending 4242" is honest, useful, and gives a card thief nothing. It is also, awkwardly, better copy: nobody wants to read the word "stolen" in an email from a company they pay.</p>"""),
        ("counting", "How to find yours", """
    <p>You do not need any tooling to check this. In the Stripe Dashboard, filter invoices to unpaid, open a few, and look at the last payment attempt's decline code. Or pull it from the API: on a failed <code>PaymentIntent</code>, it is <code>last_payment_error.decline_code</code>.</p>
    <p>Count what fraction of your unpaid invoices sit on these nine codes. That fraction is the share of your failed payments where retry configuration is irrelevant and only an email can help. In most subscription businesses it is a meaningful minority, but the only number that matters is yours, and we are not going to quote you an industry average we cannot stand behind.</p>
    <p>There is a <a href="/tools/decline-code-lookup/">free decline code lookup</a> on this site that covers 48 codes and marks these nine, if you would rather search than remember.</p>"""),
    ],
    faqs=[
        ("Is insufficient_funds a hard decline?",
         "No. insufficient_funds is not on Stripe's list of codes that block retry execution, and it is one of the better codes to retry, because the underlying condition genuinely changes when the customer gets paid. A lot of articles list it as a hard decline. They are wrong."),
        ("Does Stripe stop the retry schedule on these codes?",
         "No. Stripe keeps the schedule and keeps incrementing the attempt count. It just does not send the charge to the card network until a new payment method is attached. The invoice looks like it is being retried when in practice nothing is being attempted."),
        ("Is expired_card on this list?",
         "No, and that surprises people. expired_card is not one of the nine, so Stripe will genuinely retry it, even though an expired card does not become unexpired. Unless the issuer has pushed an updated card through the card account updater, those retries do nothing. It is an email problem wearing a retry problem's clothes."),
        ("Can I make Stripe retry these anyway?",
         "You can turn Smart Retries off and write your own retry logic, but you cannot make the issuer approve a stolen card. The block exists because the answer is already known. Spending attempts on it only delays the point at which you ask the customer for a new card."),
        ("How many decline codes does Stripe have in total?",
         "Stripe documents around 48 decline codes. Nine of them block retry execution. The rest are retryable to varying degrees of usefulness."),
    ],
    sources=[SRC_SMART, SRC_CODES, SRC_DECLINES],
    related=[
        ("Stripe is not retrying your failed invoice", "/blog/stripe-not-retrying-failed-invoice/", "The nine reasons why, and the API field that proves each one."),
        ("The Visa excessive reattempts rule", "/blog/visa-excessive-reattempts-rule/", "The other limit on retries, the one the card network imposes rather than Stripe."),
        ("Free decline code lookup", "/tools/decline-code-lookup/", "All 48 codes, searchable, with the nine flagged and guidance on what to say."),
        ("How Stripe Smart Retries actually work", "/blog/how-stripe-smart-retries-work/", "The default schedule, what you can configure, and what the ML is doing."),
        ("Why retrying an expired card rarely works", "/blog/expired-card-stripe-retries/", "The code Stripe will retry that it probably should not."),
    ],
))

# 2 ------------------------------------------------------------------------
ARTICLES.append(dict(
    slug="how-stripe-smart-retries-work",
    title="How Stripe Smart Retries actually work | RecoverFlow",
    h1="How Stripe Smart Retries actually work",
    desc="Stripe Smart Retries defaults to 8 attempts over 2 weeks, timed by a model. What you can configure, what you cannot, and when to turn it off.",
    answer="""      <p>Smart Retries is Stripe's built in retry engine for failed subscription invoices. Stripe's recommended default is <strong>8 tries within 2 weeks</strong>, and it picks the timing of those tries with a model trained across the Stripe network rather than using a fixed schedule.</p>
      <p>You can change the window to 1 week, 2 weeks, 3 weeks, 1 month or 2 months. You cannot pick the individual attempt times while it is on. You can turn it off entirely and define your own retry rules instead.</p>
      <p>It is free, it is already in your account, and for most people it is the right starting point.</p>""",
    sections=[
        ("what", "What it is", """
    <p>When a subscription invoice fails, Stripe does not give up on the first try. It schedules further attempts. Smart Retries is the feature that decides <em>when</em> those attempts happen.</p>
    <p>The ordinary way to build this is a fixed ladder: retry after one day, then three, then seven. Smart Retries instead uses a model trained on payment outcomes across Stripe's network to pick moments that are more likely to succeed for that particular card and issuer. The intuition is simple enough. A card that failed for insufficient funds on the 28th has a much better chance on the 1st than on the 29th, and the network has seen enough of those to know it.</p>
    <p>It is on by default for Stripe Billing subscriptions, and you configure it under the automatic collection settings in the Dashboard.</p>"""),
        ("defaults", "The defaults, precisely", """
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Setting</th><th scope="col">Value</th></tr></thead>
        <tbody>
          <tr><th scope="row">Recommended default</th><td>8 tries within 2 weeks</td></tr>
          <tr><th scope="row">Configurable windows</th><td>1 week, 2 weeks, 3 weeks, 1 month, 2 months</td></tr>
          <tr><th scope="row">Attempt timing</th><td>Chosen by Stripe's model, not by you</td></tr>
          <tr><th scope="row">Cost</th><td>Free, included in Stripe Billing</td></tr>
          <tr><th scope="row">Can be disabled</th><td>Yes, in favour of your own retry rules</td></tr>
        </tbody>
      </table>
    </div>
    <p>The trade you are making by leaving it on is control for accuracy. You give up the ability to say "retry at 9am on day three" and in exchange you get timing informed by far more data than you will ever have about your own customers' cards.</p>"""),
        ("cannot", "What it cannot do", """
    <p>Three limits are worth knowing before you decide it is enough.</p>
    <p><strong>It cannot beat a hard decline.</strong> For nine specific decline codes, Stripe will not execute the retry at all until a new payment method is added. The schedule still runs and the attempt counter still climbs, but nothing reaches the issuer. No retry configuration changes that. There is <a href="/blog/stripe-decline-codes-that-stop-retries/">a full article on those nine codes</a>.</p>
    <p><strong>It does not chase the customer.</strong> Retrying is a machine talking to a bank. A meaningful share of failures need a human to do something: replace a card, authenticate a payment, move money into the account. Stripe does send its own emails for this, but what those emails say and when they send is largely out of your hands, which is <a href="/blog/stripe-dunning-emails-what-you-control/">its own article</a>.</p>
    <p><strong>It does not look backwards.</strong> Turning it on today does nothing for the invoices that already failed and closed. Whatever you lost last quarter stays lost.</p>"""),
        ("window", "How to choose the window", """
    <p>The window is the only real decision, and the honest answer is that it depends on why your payments fail.</p>
    <ul>
      <li>If your failures skew toward <code>insufficient_funds</code>, a longer window helps, because the thing you are waiting for is payday. Two weeks catches one pay cycle. A month catches two.</li>
      <li>If your failures skew toward cards that need replacing, a longer window mostly delays the moment you find out. The attempts are not doing the work; an email is.</li>
      <li>If you sell to consumers, the longer windows are usually worth it. If you sell to businesses on corporate cards, the failure profile is different and stretching to two months mainly stretches your reporting lag.</li>
    </ul>
    <p>A practical starting point: leave it at the default 2 weeks, look at your decline code mix after a month, and lengthen only if <code>insufficient_funds</code> is your biggest bucket.</p>"""),
        ("off", "When to turn it off", """
    <p>Turning Smart Retries off is a real option and Stripe supports it. You would do it if you have a genuine reason to control exact attempt times: a business where you know payday, a market where issuer behaviour is unusual, or an existing retry system you trust and want to keep.</p>
    <p>What you should not do is turn it off because you read that a fixed ladder is best practice. You will be replacing a model trained on Stripe's whole network with a guess. If your own data does not tell you the guess is better, it probably is not.</p>
    <div class="callout">
      <p><strong>Worth saying plainly:</strong> if you have not yet switched Smart Retries on and configured the window deliberately, do that before you buy anything from anyone, including us. It is free and it is the largest single improvement available to most Stripe accounts. The rest of the free settings, with the exact Dashboard path for each, are in <a href="/blog/stripe-free-dunning-settings-checklist/">the settings checklist</a>.</p>
    </div>"""),
    ],
    faqs=[
        ("How many times does Stripe retry a failed payment?",
         "Stripe's recommended default for Smart Retries is 8 attempts within 2 weeks. The number is tied to the window you choose, and the individual attempt times are decided by Stripe's model rather than a fixed schedule."),
        ("Can I choose the exact days Stripe retries?",
         "Not while Smart Retries is on. You choose the overall window and Stripe chooses the timing inside it. If you need exact control you have to disable Smart Retries and define your own retry rules."),
        ("Is Smart Retries free?",
         "Yes. It is part of Stripe Billing at no extra charge. Anyone selling you payment recovery should be adding something on top of it, not selling you the thing you already have."),
        ("What is the longest retry window Stripe allows?",
         "Two months. The full set of options is 1 week, 2 weeks, 3 weeks, 1 month and 2 months."),
        ("Does Smart Retries work on one off invoices?",
         "Smart Retries is a Stripe Billing feature for invoices with automatic collection, which in practice means subscriptions and recurring invoices rather than one off payment intents."),
    ],
    sources=[SRC_SMART, SRC_DECLINES],
    related=[
        ("Stripe is not retrying your failed invoice", "/blog/stripe-not-retrying-failed-invoice/", "The nine documented reasons no retry is scheduled, with the field to check for each."),
        ("The free Stripe settings checklist", "/blog/stripe-free-dunning-settings-checklist/", "Every free retry and email setting, with the exact Dashboard path."),
        ("The nine codes that stop retries dead", "/blog/stripe-decline-codes-that-stop-retries/", "Where retry configuration stops mattering entirely."),
        ("Retry schedule builder", "/tools/retry-schedule-builder/", "Free tool. Tells you when a retry schedule is the wrong answer."),
        ("RecoverFlow vs Stripe's own features", "/compare/stripe-native/", "An honest account of what Stripe already does for free."),
    ],
))

# 3 ------------------------------------------------------------------------
ARTICLES.append(dict(
    slug="expired-card-stripe-retries",
    title="Why retrying an expired card rarely works | RecoverFlow",
    h1="Why retrying an expired card rarely works",
    desc="expired_card is not on Stripe's hard decline list, so Stripe retries it. An expired card does not become unexpired. Here is what actually recovers these payments.",
    answer="""      <p><code>expired_card</code> is <strong>not</strong> one of the nine decline codes that stop Stripe executing a retry. So Stripe will genuinely attempt the charge again, on schedule, against a card that has expired.</p>
      <p>Cards do not un-expire. Unless the issuer pushes a replacement through the card account updater and Stripe picks it up, every one of those attempts fails the same way.</p>
      <p>This is an email problem wearing a retry problem's clothes. The recovery lever is asking the customer for a new expiry date, and doing it before the card dies rather than after.</p>""",
    sections=[
        ("why", "Why Stripe retries it anyway", """
    <p>Stripe's block list exists for codes where the answer is already known and permanent: a stolen card, a revoked authorization, a Radar block. <code>expired_card</code> did not make that list, and there is a defensible reason. Card networks run an updater service. When an issuer reissues a card, the new number and expiry can be pushed to merchants automatically, and Stripe supports this. If that update lands between one attempt and the next, a retry genuinely succeeds.</p>
    <p>So the retry is not irrational. It is a bet that the updater will do its job during the retry window. Sometimes it does. Often it does not, because the customer's issuer does not participate, or the card is a type the updater does not cover, or the customer has simply not activated the replacement sitting in a drawer.</p>
    <p>What you get in the meantime is a subscription that looks like it is being actively worked on when nothing is changing.</p>"""),
        ("before", "The version of this that actually works is preventative", """
    <p>An expired card is the only common failure you can see coming. You know the expiry date. You have known it since the day they signed up.</p>
    <p>Stripe will send its own card expiry email roughly a month before the card dies, using your branding, if you have that email enabled in your Billing settings. Turn it on. It costs nothing and it is the single highest leverage thing in this article.</p>
    <p>If you want to go further, you can query it yourself. Every <code>PaymentMethod</code> carries <code>card.exp_month</code> and <code>card.exp_year</code>. Listing active subscriptions whose default payment method expires in the next 60 days is a short script and it gives you a list of customers you can contact before anything breaks, on your own timing, in your own voice.</p>
    <div class="callout">
      <p><strong>The asymmetry is large.</strong> Asking someone to update a card that is <em>about to</em> expire is a helpful reminder. Asking after it has expired is a failed payment notice, and it arrives alongside the small anxiety of "did my service just stop". The same request converts differently depending on which side of the expiry date you send it.</p>
    </div>"""),
        ("after", "If it has already failed", """
    <p>Once you are on the wrong side of it, the job is a good email, sent quickly, with as little friction as possible between reading it and having a working card on file.</p>
    <ul>
      <li><strong>Say which card.</strong> The last four digits and the brand. People hold several cards and cannot be expected to guess which one you have.</li>
      <li><strong>Say what happens and when.</strong> If access continues for now, say so. If it stops on a date, give the date. Vagueness here reads as a threat, which is the fastest way to turn a payment problem into a cancellation.</li>
      <li><strong>One link, no login.</strong> Stripe's hosted invoice page and the customer portal both let someone update a card without remembering a password. Use them.</li>
      <li><strong>Do not send five.</strong> An expired card is not ignored out of reluctance to pay. It is ignored because updating a card is a small chore. Two or three well spaced reminders is the whole budget.</li>
    </ul>
    <p>There is a <a href="/tools/dunning-email-generator/">free dunning email generator</a> on this site that writes this for you, by decline reason, and it will tell you when it thinks an email is the wrong move.</p>"""),
        ("measure", "How to tell if this matters for you", """
    <p>Pull your unpaid invoices and group the last attempt's decline code. If <code>expired_card</code> is near the top, you have a preventative problem and the fix is upstream of any retry setting.</p>
    <p>We are deliberately not telling you what share of failures are expired cards on average. Published figures vary wildly depending on who is selling what, and your mix depends on your customers' countries, card types and how long they have been subscribed. The number on your own dashboard is the only one worth acting on.</p>"""),
    ],
    faqs=[
        ("Is expired_card a hard decline in Stripe?",
         "Not in the sense that matters. It is not one of the nine codes where Stripe stops executing retries, so Stripe will keep attempting the charge. In practical terms it behaves like a hard decline anyway, because an expired card cannot start working unless the issuer pushes an updated card through the network updater."),
        ("Will Stripe automatically update an expired card?",
         "Sometimes. Stripe supports the card networks' automatic account updater, so a reissued card can be updated without the customer doing anything. It depends on the issuer and card type participating, so it is a helpful backstop rather than something to rely on."),
        ("How far in advance does Stripe email about expiring cards?",
         "Stripe's card expiry email goes out roughly a month before the card expires, if you have that email enabled in your Billing settings. It uses your branding."),
        ("Should I retry an expired card at all?",
         "Letting Stripe's schedule run costs you nothing and occasionally catches an updater refresh. Just do not treat those attempts as your recovery plan. Send the email."),
        ("Can I see which subscriptions have cards expiring soon?",
         "Yes. Every card PaymentMethod exposes exp_month and exp_year through the Stripe API, so you can list active subscriptions whose default payment method expires in the next month or two and contact those customers first."),
    ],
    sources=[SRC_SMART, SRC_CODES, SRC_EMAILS],
    related=[
        ("The nine codes that stop retries dead", "/blog/stripe-decline-codes-that-stop-retries/", "expired_card is not one of them, and that is the point."),
        ("Free dunning email generator", "/tools/dunning-email-generator/", "Writes the email by decline reason, and says when not to send one."),
        ("What Stripe's own dunning emails control", "/blog/stripe-dunning-emails-what-you-control/", "Including the expiry email you should switch on today."),
    ],
))

# 4 ------------------------------------------------------------------------
ARTICLES.append(dict(
    slug="stripe-dunning-emails-what-you-control",
    title="Stripe dunning emails: what you control | RecoverFlow",
    h1="Stripe's dunning emails: what you control and what you do not",
    desc="Stripe emails your customers about failed payments for free. You control branding and whether they send, but not the copy, the schedule or the sender.",
    answer="""      <p>Stripe will email your customers about failed payments for free. You control your branding, and you control which of the emails are switched on.</p>
      <p>You do not meaningfully control the wording, the sending schedule, or the fact that they come from Stripe's infrastructure rather than from you. Stripe also keeps its email logs for 60 days, which sets a floor on how far back you can audit what your customers were told.</p>
      <p>For a lot of businesses that is a perfectly acceptable trade. Switch them on before you evaluate anything paid.</p>""",
    sections=[
        ("what-sends", "What Stripe will send", """
    <p>Three things matter here, all configurable in your Billing settings.</p>
    <ul>
      <li><strong>Failed payment notifications.</strong> Sent when a subscription payment fails, telling the customer and pointing them at a way to fix it.</li>
      <li><strong>Unpaid invoice reminders.</strong> Follow ups on invoices that remain open.</li>
      <li><strong>Card expiry notices.</strong> Sent roughly a month before a saved card expires. This one is preventative and is the most underused of the three.</li>
    </ul>
    <p>All of them carry your branding: your logo, your colours, your business name. To the customer they read as coming from you, which is the correct design.</p>"""),
        ("control", "What you actually control", """
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Thing</th><th scope="col">Can you control it?</th></tr></thead>
        <tbody>
          <tr><th scope="row">Whether each email sends</th><td>Yes, per email type</td></tr>
          <tr><th scope="row">Logo, colours, business name</th><td>Yes, through Branding settings</td></tr>
          <tr><th scope="row">The wording of the email</th><td>No, beyond branding and locale</td></tr>
          <tr><th scope="row">How many go out and when</th><td>Not independently of Stripe's schedule</td></tr>
          <tr><th scope="row">The sending domain and reply address</th><td>Not in the way a marketing tool lets you</td></tr>
          <tr><th scope="row">Different copy per decline reason</th><td>No</td></tr>
          <tr><th scope="row">How long the send log is kept</th><td>No. Stripe retains email logs for 60 days</td></tr>
        </tbody>
      </table>
    </div>
    <p>The row that catches people out is the last one. If someone asks in October what a customer was told in July, and you were relying entirely on Stripe's logs, you cannot answer. That matters more for disputes and compliance questions than for revenue, but it matters.</p>"""),
        ("when-enough", "When this is enough", """
    <p>Genuinely, often. If your failed payments are mostly insufficient funds and expired cards, if your customers are consumers who recognise your brand, and if you are not trying to run different messaging for different failure reasons, Stripe's emails plus Smart Retries will do most of the available work for zero extra cost and zero extra vendor.</p>
    <p>The bar for adding anything on top should be that you can name the specific thing you want that Stripe does not do. If you cannot name it, you do not need it yet.</p>"""),
        ("when-not", "When it stops being enough", """
    <p>Four situations where people outgrow it, in rough order of how often they come up.</p>
    <p><strong>You want the message to match the reason.</strong> A customer whose card was reported lost needs a different email from one whose payment needs SCA authentication, which needs a different email again from someone who is simply short of money this week. One generic template addresses all three adequately and none of them well.</p>
    <p><strong>You want it to come from a person.</strong> Especially in B2B, an email from a named human at your company gets replies that a system notification does not. Stripe's emails are branded but they are not from anyone.</p>
    <p><strong>You want to know what worked.</strong> Attribution here is genuinely hard. A payment succeeds and something caused it, but distinguishing "our email prompted a card update" from "Stripe's retry happened to land after payday" needs the sequence of events, not just the outcome. Stripe reports the outcome.</p>
    <p><strong>You want to look back further than 60 days.</strong> Self explanatory, and only a problem once someone asks.</p>"""),
        ("if-building", "If you are building your own", """
    <p>Two things are easy to get wrong and worth stating.</p>
    <p>First, do not tell customers the decline reason for <code>lost_card</code>, <code>pickup_card</code>, <code>stolen_card</code> or <code>highest_risk_level</code>. Stripe's guidance is explicit that revealing these gives useful information to someone using a card they should not have. Write the generic version.</p>
    <p>Second, if you send your own emails you become responsible for their deliverability, their unsubscribe behaviour and their data handling. Stripe was absorbing all three for you. Transactional billing email is a legitimate category, but sending it badly from a domain you also use for marketing is how you end up in spam folders for both.</p>"""),
    ],
    faqs=[
        ("Does Stripe send dunning emails automatically?",
         "Yes, if you enable them. Stripe can send failed payment notifications, unpaid invoice reminders and card expiry notices, all carrying your branding, at no additional cost."),
        ("Can I customise the text of Stripe's dunning emails?",
         "Not beyond branding and language. You control the logo, colours and business name, and whether each email type sends at all. The copy itself is Stripe's."),
        ("How long does Stripe keep email logs?",
         "60 days. If you need a record of what a customer was told further back than that, you need to keep it somewhere yourself."),
        ("Do Stripe's emails come from my domain?",
         "They carry your branding and read as coming from your business, but they are sent by Stripe's infrastructure. You do not get the sender control you would have with your own email system."),
        ("Should I turn Stripe's emails off if I use a third party tool?",
         "Usually yes, or you will send two sets of emails about the same failed payment, which is worse than either alone. Decide which system owns the conversation and switch the other one off."),
    ],
    sources=[SRC_EMAILS, SRC_SMART, SRC_CODES],
    related=[
        ("The free Stripe settings checklist", "/blog/stripe-free-dunning-settings-checklist/", "Every email switch named exactly as Stripe labels it, and where to find it."),
        ("RecoverFlow vs Stripe's own features", "/compare/stripe-native/", "The honest version, including when to just use Stripe."),
        ("Free dunning email generator", "/tools/dunning-email-generator/", "Reason aware copy you can paste anywhere."),
        ("How Smart Retries work", "/blog/how-stripe-smart-retries-work/", "The other half of Stripe's free recovery stack."),
    ],
))

# 5 ------------------------------------------------------------------------
ARTICLES.append(dict(
    slug="how-to-measure-involuntary-churn",
    title="How to measure involuntary churn without guessing | RecoverFlow",
    h1="How to measure involuntary churn without guessing",
    desc="Involuntary churn is revenue lost to a failed payment, not a decision. How to calculate it from your own Stripe data, and why benchmarks are not worth much.",
    answer="""      <p>Involuntary churn is the customers you lost because a payment failed, not because they decided to leave. Voluntary churn is a decision. Involuntary churn is an accident, usually a card one.</p>
      <p>Measure it as: subscriptions that ended in a period where the final state was an unpaid invoice, divided by all subscriptions active at the start of that period. Do it in dollars as well as in counts, because the two numbers are rarely the same shape.</p>
      <p>Do not use a benchmark. The published ranges are wide, poorly sourced and usually quoted by someone selling a fix. Your own number takes an afternoon.</p>""",
    sections=[
        ("definition", "Getting the definition right first", """
    <p>The distinction is about cause, not outcome. Both kinds of churn end with a customer who is no longer paying you.</p>
    <ul>
      <li><strong>Voluntary churn</strong>: they cancelled. They clicked the button, or emailed you, or let a fixed term lapse deliberately. There was an intention.</li>
      <li><strong>Involuntary churn</strong>: the payment stopped working and the subscription eventually ended because of it. Nobody decided anything.</li>
    </ul>
    <p>The reason to separate them is that they respond to completely different interventions. Voluntary churn is a product and pricing problem. Involuntary churn is an operations problem. Averaging them together produces a number that tells you to do nothing in particular.</p>
    <div class="callout">
      <p><strong>The awkward middle case:</strong> a customer who wanted to leave, did not bother to cancel, and let the card fail. That is voluntary churn wearing involuntary clothes, and no measurement technique separates it cleanly. It is one reason to be sceptical of anyone claiming they can recover a very high share of failed payments. Some of those payments are failing on purpose.</p>
    </div>"""),
        ("calculate", "The calculation", """
    <p>Pick a period. A month is standard. Then:</p>
    <p><strong>Count based rate:</strong></p>
    <p><code>involuntary churn rate = subscriptions ended in the period with an unpaid final invoice / subscriptions active at the start of the period</code></p>
    <p><strong>Revenue based rate:</strong></p>
    <p><code>involuntary revenue churn = MRR of those subscriptions / MRR at the start of the period</code></p>
    <p>Run both. If your revenue rate is much higher than your count rate, your failures are concentrated in your larger accounts, which changes the priority entirely: fewer, more valuable conversations. If it is much lower, you are losing a tail of small accounts and the fix should be automated rather than personal.</p>"""),
        ("stripe", "Pulling it from Stripe", """
    <p>You do not need a data warehouse for the first version of this.</p>
    <ol>
      <li>List subscriptions with status <code>canceled</code> or <code>unpaid</code> whose <code>ended_at</code> falls in your period.</li>
      <li>For each, look at the most recent invoice. If it is <code>uncollectible</code> or was left <code>open</code> past the retry window, that is an involuntary end.</li>
      <li>If the last invoice was paid and the cancellation came afterwards, that is voluntary. Somebody chose.</li>
      <li>Sum the monthly amounts of the involuntary group. That is your involuntary churn in dollars.</li>
    </ol>
    <p>Then do the part most people skip: group the involuntary ones by the last decline code. That single breakdown tells you whether you have a retry problem, an email problem or a fraud problem, and those need different work. There is a <a href="/tools/decline-code-lookup/">code lookup here</a> if the codes are unfamiliar.</p>"""),
        ("benchmarks", "Why we are not giving you a benchmark", """
    <p>You will find plenty of articles confidently stating that involuntary churn is some specific percentage of all churn for SaaS businesses. Those figures circulate widely and are usually traceable to a vendor's own customer base, a small sample, or nothing at all.</p>
    <p>They also would not help you if they were accurate. Involuntary churn depends on how you bill, the card types your customers use, the countries they are in, the price point, and how long the average customer has been on the same card. A consumer app charging $9 a month to people using debit cards has a completely different profile from a $2,000 a month B2B tool billed to corporate cards.</p>
    <p>The number that should drive your decisions is the one you calculated above. Compare it to your own last quarter, not to a stranger's.</p>"""),
        ("worth", "Turning the number into a decision", """
    <p>Once you have monthly involuntary churn in dollars, the decision about whether to spend anything on it is arithmetic rather than opinion.</p>
    <p>Take your monthly involuntary churn in MRR. A recovered subscription is worth more than one month, because it keeps paying, so multiply by a conservative expected remaining lifetime in months. Then apply a recovery rate you are willing to defend, and be harsh with yourself here, because you will be recovering the easy half and some of what looks recoverable was voluntary churn in disguise.</p>
    <p>That figure is the ceiling on what any solution to this is worth to you. If it is smaller than the price of the tools, the answer is to configure Stripe's free features properly and move on. We publish <a href="/tools/recovery-estimator/">an estimator</a> that does this arithmetic against real competitor pricing, and it will tell you to do nothing when doing nothing is right.</p>"""),
    ],
    faqs=[
        ("What is involuntary churn?",
         "Involuntary churn is when a customer stops paying because a payment failed rather than because they chose to leave. The usual causes are expired cards, insufficient funds, cards reported lost or stolen, and payments that needed authentication the customer never completed."),
        ("How is involuntary churn different from voluntary churn?",
         "Voluntary churn involves a decision by the customer. Involuntary churn does not. They matter separately because voluntary churn is fixed through product and pricing, and involuntary churn is fixed through payment operations."),
        ("What is a good involuntary churn rate?",
         "There is no defensible universal answer, and anyone quoting one is usually selling something. It varies enormously with price point, card mix, country and customer age. Calculate your own from Stripe and compare it to your own previous months."),
        ("Can all involuntary churn be recovered?",
         "No. Some of it was a customer who wanted to leave and let the card fail instead of cancelling. Some of it is cards that are permanently dead with customers who will not respond. Treat any claim of very high recovery rates with suspicion."),
        ("Where in Stripe do I find this?",
         "Cross reference subscriptions that ended in your period against the status of their final invoice. An unpaid or uncollectible final invoice means involuntary; a paid final invoice followed by a cancellation means voluntary."),
    ],
    sources=[
        ("Stripe: Subscription lifecycle and statuses", "https://docs.stripe.com/billing/subscriptions/overview", ""),
        SRC_SMART,
        SRC_CODES,
    ],
    related=[
        ("Free recovery estimator", "/tools/recovery-estimator/", "Runs the arithmetic in the last section against real competitor pricing."),
        ("The nine codes that stop retries dead", "/blog/stripe-decline-codes-that-stop-retries/", "Where to look once you have grouped your failures by code."),
        ("Pricing", "/pricing/", "Including a section on when 25% of recovery is the wrong deal for you."),
    ],
))

# 6 ------------------------------------------------------------------------
ARTICLES.append(dict(
    slug="is-insufficient-funds-a-hard-decline",
    title="Is insufficient_funds a hard decline? | RecoverFlow",
    h1="Is <code>insufficient_funds</code> a hard decline?",
    desc="No. insufficient_funds is not on Stripe's list of codes that block retries, and retrying it is correct. A lot of published guidance gets this backwards.",
    answer="""      <p><strong>No.</strong> <code>insufficient_funds</code> is not on Stripe's list of decline codes that stop a retry from being executed. Stripe will retry it, and retrying is the correct thing to do.</p>
      <p>It is close to the ideal soft decline: the card is real, the customer is real, the account is real, and the only problem is timing. That problem fixes itself on payday.</p>
      <p>Several widely shared articles list it as a hard decline. Check the nine code list in Stripe's own Smart Retries documentation and you will not find it there.</p>""",
    sections=[
        ("why-confused", "Where the confusion comes from", """
    <p>Two different distinctions get collapsed into one and the result is a mess.</p>
    <p>The first is the card network's own idea of hard versus soft. In that vocabulary, a hard decline is one the issuer says never to retry and a soft decline is a temporary condition. That framing is real but it is not what Stripe's retry engine acts on.</p>
    <p>The second is the operational one, and it is the one that governs your money: <em>will Stripe actually send this charge to the network again?</em> Stripe answers that with a list of nine specific decline codes. If your code is on it, Stripe holds the retry until a new payment method exists. If it is not, Stripe retries.</p>
    <p><code>insufficient_funds</code> is not on the list. It also is not a hard decline in the network sense. It ends up mislabelled mainly because "declined for insufficient funds" sounds final when you read it, and because plenty of blog posts copy each other rather than the documentation.</p>"""),
        ("the-list", "The actual list", hard_code_table() + """
    <p>Nine codes. That is the whole set. Anything you see that is not in that table, including <code>insufficient_funds</code>, <code>expired_card</code>, <code>processing_error</code>, <code>try_again_later</code> and <code>do_not_honor</code>, will be retried by Stripe on schedule.</p>"""),
        ("handle", "How to handle insufficient funds well", """
    <p>Because the blocker is timing rather than the card, the tools that work here are different from the ones that work for a dead card.</p>
    <p><strong>Give it room.</strong> This is the one failure type where a longer retry window genuinely earns its keep. Stripe's Smart Retries window can be set to 1 week, 2 weeks, 3 weeks, 1 month or 2 months. If insufficient funds is your largest bucket, the shorter windows may be ending the schedule before the customer's next pay date.</p>
    <p><strong>Let Stripe pick the days.</strong> Smart Retries times the attempts with a model trained across the network, and payday timing is precisely the kind of pattern that benefits from data you do not have.</p>
    <p><strong>Be careful with the email.</strong> Unlike a card update request, this one carries a small social cost. The customer knows why it failed. A cheerful multi step sequence about updating their payment details reads badly when the actual situation is that money was tight this week. One clear message, no urgency theatre, and let the retries do the work.</p>
    <p><strong>Do not treat it as a churn signal on the first failure.</strong> It frequently is not. It becomes one when it repeats across several cycles.</p>"""),
        ("check", "Checking your own", """
    <p>On a failed <code>PaymentIntent</code>, the field is <code>last_payment_error.decline_code</code>. In the Dashboard, open an unpaid invoice and read the last attempt.</p>
    <p>If you want to look up any code without memorising the list, we publish a <a href="/tools/decline-code-lookup/">free decline code lookup</a> covering 48 codes, which marks the nine that block retries and flags the four you should never name in an email to the customer.</p>"""),
    ],
    faqs=[
        ("Is insufficient_funds a hard decline?",
         "No. It is not on Stripe's list of nine decline codes that prevent a retry from being executed, and the underlying condition is temporary. Stripe will retry it and that is the right behaviour."),
        ("How many times will Stripe retry insufficient funds?",
         "It follows your Smart Retries settings like any other retryable code. Stripe's recommended default is 8 attempts within 2 weeks, with the window configurable up to two months."),
        ("Should I email a customer whose payment failed for insufficient funds?",
         "One clear message is fine. A multi step sequence pressing them to update their card is not, because their card is not the problem. Give the retry schedule room to catch their next pay date."),
        ("What is the difference between a hard and soft decline?",
         "A soft decline is a temporary condition worth retrying; a hard decline is one the issuer will not approve again. In practice, the distinction that governs your Stripe account is narrower: nine specific decline codes stop Stripe executing retries, and everything else gets retried."),
        ("Which decline codes are hard declines in Stripe?",
         "incorrect_number, lost_card, pickup_card, stolen_card, revocation_of_authorization, revocation_of_all_authorizations, authentication_required, highest_risk_level and transaction_not_allowed. Those are the nine where Stripe will not execute a retry until a new payment method is added."),
    ],
    sources=[SRC_SMART, SRC_CODES, SRC_DECLINES],
    related=[
        ("The nine codes in full", "/blog/stripe-decline-codes-that-stop-retries/", "What each one means and what to do instead of retrying."),
        ("Free decline code lookup", "/tools/decline-code-lookup/", "48 codes, searchable, filterable by whether Stripe will retry."),
        ("How Smart Retries work", "/blog/how-stripe-smart-retries-work/", "Choosing the retry window that suits your failure mix."),
    ],
))


# 7 ------------------------------------------------------------------------
ARTICLES.append(dict(
    slug="stripe-subscription-past-due-vs-unpaid",
    title="Stripe past_due vs unpaid vs canceled explained | RecoverFlow",
    h1="<code>past_due</code>, <code>unpaid</code>, <code>canceled</code>: what each Stripe subscription status means",
    desc="past_due means Stripe is still trying. unpaid means it stopped but kept the subscription. canceled is terminal. Which one you get is a setting you choose.",
    answer="""      <p><strong><code>past_due</code></strong> means the latest finalised invoice failed or was not attempted, and Stripe is still working on it. <strong><code>unpaid</code></strong> means Stripe has finished retrying, kept the subscription alive, and stopped attempting payment. <strong><code>canceled</code></strong> is terminal and cannot be updated.</p>
      <p>Which of the three you end up in when the retry window closes is not fixed behaviour. It is a choice in your Stripe subscription settings, and most people have never looked at it.</p>""",
    sections=[
        ("all", "Every status, and what Stripe means by it", """
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Status</th><th scope="col">What it means</th></tr></thead>
        <tbody>
          <tr><th scope="row"><code>trialing</code></th><td>In a trial period. Safe to provision the product. Moves to <code>active</code> automatically on the first successful payment.</td></tr>
          <tr><th scope="row"><code>active</code></th><td>In good standing. Worth knowing: this does <em>not</em> mean every outstanding invoice has been paid. Paying the latest invoice on a <code>past_due</code> subscription, or marking it uncollectible, flips it back to <code>active</code>.</td></tr>
          <tr><th scope="row"><code>incomplete</code></th><td>The first payment has not succeeded yet. The customer has 23 hours to complete it, including any authentication step.</td></tr>
          <tr><th scope="row"><code>incomplete_expired</code></th><td>The 23 hours ran out. These subscriptions never bill. The status exists purely so you can count customers who failed to get started.</td></tr>
          <tr><th scope="row"><code>past_due</code></th><td>Payment on the latest finalised invoice failed or was not attempted. Invoices keep being created. Retries are still happening.</td></tr>
          <tr><th scope="row"><code>unpaid</code></th><td>The latest invoice is unpaid but the subscription is still there. Invoices keep generating. <strong>Payments are no longer attempted.</strong></td></tr>
          <tr><th scope="row"><code>canceled</code></th><td>Terminal. Automatic collection on unpaid invoices is switched off (<code>auto_advance=false</code>). This state cannot be changed.</td></tr>
          <tr><th scope="row"><code>paused</code></th><td>A trial ended with no payment method on file and your trial settings said pause rather than cancel. No further invoices are created.</td></tr>
        </tbody>
      </table>
    </div>
    <p>The distinction that costs people money is <code>past_due</code> against <code>unpaid</code>. Both look like "they owe us". Only one of them still has Stripe working on your behalf.</p>"""),
        ("first-vs-renewal", "Why a first payment fails differently from a renewal", """
    <p>A failed <em>first</em> payment does not produce <code>past_due</code>. It produces <code>incomplete</code>, and the customer gets 23 hours before it becomes <code>incomplete_expired</code> and stops mattering forever.</p>
    <p>That short clock is the argument for treating signup failures as a completely separate problem from renewal failures. A renewal failure has weeks of retries and dunning emails ahead of it. A signup failure has less than a day, and after that the record just sits there as evidence that someone tried to buy from you and could not.</p>
    <div class="callout">
      <p><strong>Worth checking:</strong> count your <code>incomplete_expired</code> subscriptions. Every one is a customer who chose you, entered a card, and did not get through. That is a different and usually more fixable problem than churn.</p>
    </div>"""),
        ("after-retries", "The three things that can happen when retries run out", """
    <p>When the retry window closes on a <code>past_due</code> subscription, Stripe does one of three things, and you choose which in your Dashboard subscription settings:</p>
    <ul>
      <li><strong>Cancel the subscription.</strong> It moves to <code>canceled</code> after the maximum number of days in the retry schedule. Clean, terminal, and unrecoverable without creating something new.</li>
      <li><strong>Mark it unpaid.</strong> It moves to <code>unpaid</code>. Invoices continue to be generated and sit in draft. The relationship stays on the books.</li>
      <li><strong>Leave it past due.</strong> It stays <code>past_due</code>, invoices keep being generated, and charging continues according to your retry settings.</li>
    </ul>
    <p>None of these is obviously right. Cancelling keeps your subscriber count honest and your Stripe data clean. Marking unpaid keeps the door open for a customer who reappears in three weeks with a new card. Leaving it past due keeps charging, which is either persistence or harassment depending on how long you leave it.</p>"""),
        ("access", "When to actually cut off access", """
    <p>Stripe's own guidance is unusually direct here: revoke access to your product when the subscription is <code>unpaid</code>, because by that point payments have already been attempted and retried while it was <code>past_due</code>.</p>
    <p>That is a sensible default and it is worth understanding why. Cutting access the moment a payment fails punishes a customer whose bank happened to decline on a Tuesday and who will pay fine on Thursday. Never cutting access means an expired card buys somebody a free year. The <code>unpaid</code> boundary sits where Stripe has genuinely exhausted the automated options, which is the natural point for a human decision.</p>
    <p>If you leave subscriptions <code>past_due</code> forever, you have no such boundary and you will have to invent one in your own code.</p>"""),
        ("reading", "How to read this from the API", """
    <p>The subscription's <code>status</code> field carries all of this. For the invoice side, <code>next_payment_attempt</code> tells you when Stripe will try again, and it is <code>null</code> when the invoice is set to <code>collection_method=send_invoice</code> rather than automatic charging.</p>
    <p>If you want the fuller picture of which fields to watch and which webhooks announce these transitions, that is covered in <a href="/blog/stripe-failed-payment-webhooks/">the webhooks guide</a>.</p>"""),
    ],
    faqs=[
        ("What is the difference between past_due and unpaid in Stripe?",
         "past_due means the latest finalised invoice failed and Stripe is still retrying it on schedule. unpaid means retrying has finished, the subscription is still in place, invoices keep generating, but no further payment attempts are made. past_due is an active process; unpaid is a resting state."),
        ("Does a Stripe subscription cancel automatically when payment fails?",
         "Only if you have configured it to. When the retry window ends, Stripe can cancel the subscription, mark it unpaid, or leave it past_due, and which one happens is a setting in your Dashboard subscription settings. There is no universal default behaviour you can assume from outside the account."),
        ("How long does a Stripe subscription stay incomplete?",
         "23 hours. If the first payment on the subscription has not succeeded within 23 hours of creation, the subscription becomes incomplete_expired, which is terminal and never bills the customer."),
        ("Can I reactivate a canceled Stripe subscription?",
         "No. canceled is described by Stripe as a terminal state that cannot be updated. You would create a new subscription instead. This is the main practical argument for choosing unpaid rather than cancel as your end-of-retry behaviour if you expect customers to come back."),
        ("Does active mean all invoices are paid?",
         "No, and this catches people out. Stripe states explicitly that active does not indicate that all outstanding invoices associated with the subscription have been paid. If you are using status alone as a proxy for 'this customer is square with us', you will be wrong sometimes."),
    ],
    sources=[SRC_SUBS, SRC_SMART, SRC_INVOICE],
    related=[
        ("Stripe is not retrying your failed invoice", "/blog/stripe-not-retrying-failed-invoice/", "Why a subscription in one of these statuses may never be charged again."),
        ("How Stripe Smart Retries actually work", "/blog/how-stripe-smart-retries-work/", "What sets the length of the window these statuses depend on."),
        ("Which Stripe webhooks tell you a payment failed", "/blog/stripe-failed-payment-webhooks/", "The events that fire on every transition above."),
        ("Retry schedule builder", "/tools/retry-schedule-builder/", "Free tool that builds a schedule and says when one will not help."),
    ],
))

# 8 ------------------------------------------------------------------------
ARTICLES.append(dict(
    slug="stripe-failed-payment-webhooks",
    title="Which Stripe webhooks tell you a payment failed | RecoverFlow",
    h1="Which Stripe webhooks tell you a payment failed",
    desc="invoice.payment_failed is the event that matters, plus payment_action_required for authentication. Which fields to read, and the one-hour delay to know about.",
    answer="""      <p><code>invoice.payment_failed</code> is the event to build on. It fires when a payment for an invoice fails, and it carries everything you need: the decline code, the attempt count, and when Stripe will try again.</p>
      <p>Pair it with <code>invoice.payment_action_required</code> for payments that need customer authentication, and <code>customer.subscription.updated</code> to catch the status change from <code>active</code> to <code>past_due</code>.</p>""",
    sections=[
        ("events", "The events worth handling", """
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Event</th><th scope="col">When Stripe sends it</th></tr></thead>
        <tbody>
          <tr><th scope="row"><code>invoice.payment_failed</code></th><td>A payment for an invoice failed. This is your primary trigger for everything dunning related.</td></tr>
          <tr><th scope="row"><code>invoice.payment_action_required</code></th><td>The invoice requires customer authentication. Somebody has to complete a 3D Secure step and only the cardholder can do it.</td></tr>
          <tr><th scope="row"><code>invoice.paid</code></th><td>The invoice was successfully paid. Provision access here, checking that the subscription status is <code>active</code>.</td></tr>
          <tr><th scope="row"><code>invoice.upcoming</code></th><td>Sent a few days before renewal. How many days is set by "Upcoming renewal events" in the Dashboard.</td></tr>
          <tr><th scope="row"><code>customer.subscription.updated</code></th><td>A subscription started or changed. Renewals, coupons, discounts, invoice items and plan changes all fire this.</td></tr>
          <tr><th scope="row"><code>customer.subscription.deleted</code></th><td>A customer's subscription ended.</td></tr>
        </tbody>
      </table>
    </div>
    <p>Stripe's own recommended actions on <code>invoice.payment_failed</code> are worth repeating because they are in the right order: notify the customer, collect new payment information, update the default payment method on the subscription, and consider enabling Smart Retries.</p>
    <p>Notice that "retry it yourself immediately" is not on that list.</p>"""),
        ("fields", "The fields to read once you have the event", """
    <p>The invoice object carries the state of the retry process. Four fields do most of the work.</p>
    <ul>
      <li><strong><code>attempt_count</code></strong> is the number of payment attempts from the perspective of the retry schedule. The first attempt counts, then only automatic retries increment it. Manual payment attempts after the first do not affect the schedule.</li>
      <li><strong><code>next_payment_attempt</code></strong> is when Stripe will next try. It is <code>null</code> for invoices where <code>collection_method=send_invoice</code>, because nothing is being charged automatically.</li>
      <li><strong><code>attempted</code></strong> is whether any attempt has been made at all.</li>
      <li><strong><code>billing_reason</code></strong> tells you why the invoice exists: <code>subscription_create</code>, <code>subscription_cycle</code>, <code>subscription_update</code>, <code>subscription_threshold</code>, <code>subscription</code>, <code>manual</code>, <code>quote_accept</code>, <code>upcoming</code>, or <code>automatic_pending_invoice_item_invoice</code>.</li>
    </ul>
    <p><code>billing_reason</code> is the field most people ignore and it is the one that lets you write good emails. A failure on <code>subscription_create</code> is a person who never got started. A failure on <code>subscription_cycle</code> is an existing customer whose card broke. Those deserve completely different messages, and the same generic "your payment failed" for both is why dunning email performance is usually mediocre.</p>"""),
        ("attempt-count-trap", "The attempt_count trap", """
    <p>Stripe's API reference spells out something that catches people building their own recovery logic. If a failure returns a non-retryable code, the invoice cannot be retried unless a new payment method is obtained. But, in Stripe's words, retries continue to be scheduled and <code>attempt_count</code> continues to increment, and retries are only executed if a new payment method is obtained.</p>
    <div class="callout">
      <p><strong>What this means in practice:</strong> a rising <code>attempt_count</code> is not evidence that anything is being attempted. On a stolen card you can watch the counter climb to eight without a single charge ever reaching the issuer.</p>
    </div>
    <p>If your escalation logic is "after four attempts, send the stronger email", it will behave sensibly for soft declines and nonsensically for the nine codes that block execution. Branch on the decline code first, then on the count. The <a href="/blog/stripe-decline-codes-that-stop-retries/">list of those nine codes</a> is short and worth memorising.</p>"""),
        ("one-hour", "The one hour delay nobody expects", """
    <p>Stripe notes that an invoice is not attempted until one hour after the <code>invoice.created</code> webhook. Their own suggestion is that you might not want to show that invoice as unpaid to your users during that window.</p>
    <p>This is a small thing that produces silly bugs. If you build a dashboard that flags every unpaid invoice the moment it appears, you will show customers a scary "payment failed" banner for an hour before anyone has tried to charge them. Check <code>attempted</code> before you tell anybody anything.</p>"""),
        ("first-payment", "First payments behave differently", """
    <p>When the failure is on a subscription's <em>first</em> invoice, the subscription goes to <code>incomplete</code>, not <code>past_due</code>, and the customer has 23 hours before it expires permanently. Renewal failures go to <code>past_due</code> and get the full retry window.</p>
    <p>So the same <code>invoice.payment_failed</code> event can mean "you have 23 hours" or "you have two weeks" depending on <code>billing_reason</code>. Handling both with one code path is the single most common mistake in home built dunning. The <a href="/blog/stripe-subscription-past-due-vs-unpaid/">status guide</a> covers what each state means.</p>"""),
    ],
    faqs=[
        ("Which Stripe webhook fires when a subscription payment fails?",
         "invoice.payment_failed. It fires when a payment for an invoice fails, and it is the correct trigger for notifying the customer and starting a dunning sequence. For failures that need customer authentication rather than a new card, invoice.payment_action_required fires instead."),
        ("Does attempt_count tell me how many times the card was actually charged?",
         "No. Stripe increments attempt_count according to the retry schedule even when it is not executing the retry, which happens on the nine decline codes that require a new payment method. It also does not increment for manual payment attempts after the first. Treat it as a position in the schedule, not a count of network requests."),
        ("What does next_payment_attempt being null mean?",
         "For invoices with collection_method=send_invoice it is always null, because Stripe is not charging a card automatically. On an automatically charged invoice, null generally means there is no further attempt scheduled."),
        ("How do I tell a signup failure from a renewal failure?",
         "Read billing_reason on the invoice. subscription_create is the first payment, and the subscription will be incomplete with a 23 hour window. subscription_cycle is a renewal, and the subscription will be past_due with the full retry window. They need different emails and different urgency."),
        ("Should I retry the payment myself when I get invoice.payment_failed?",
         "Usually not. Stripe's own recommended actions are to notify the customer, collect new payment information, update the default payment method, and consider enabling Smart Retries. If Smart Retries is on, Stripe is already retrying on a schedule chosen by a model trained on far more data than you have. Manual attempts after the first also do not affect that schedule."),
    ],
    sources=[SRC_WEBHOOKS, SRC_INVOICE, SRC_SUBS],
    related=[
        ("Stripe is not retrying your failed invoice", "/blog/stripe-not-retrying-failed-invoice/", "When no further event is coming, and the field that tells you so."),
        ("The nine decline codes that stop retries dead", "/blog/stripe-decline-codes-that-stop-retries/", "Branch on these before you branch on attempt_count."),
        ("past_due vs unpaid vs canceled", "/blog/stripe-subscription-past-due-vs-unpaid/", "What the status transitions in these events actually mean."),
        ("Stripe test cards for failed payments", "/blog/stripe-test-cards-for-failed-payments/", "How to fire every one of these events on demand."),
    ],
))

# 9 ------------------------------------------------------------------------
ARTICLES.append(dict(
    slug="stripe-test-cards-for-failed-payments",
    title="Stripe test cards for every decline scenario | RecoverFlow",
    h1="Stripe test cards for every failed payment scenario",
    desc="The exact test card numbers for generic decline, insufficient funds, lost card, stolen card, expired card and 3D Secure, and which decline_code each one produces.",
    answer="""      <p>Stripe publishes a test card for each decline reason. <code>4000000000000002</code> gives a generic decline, <code>4000000000009995</code> gives <code>insufficient_funds</code>, <code>4000000000009987</code> gives <code>lost_card</code> and <code>4000000000009979</code> gives <code>stolen_card</code>.</p>
      <p>The most useful one is <code>4000000000000341</code>, which attaches to a customer successfully and only fails when you try to charge it. That is the one that exercises your recovery code rather than your signup form.</p>""",
    sections=[
        ("cards", "The decline cards", """
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Card number</th><th scope="col">Scenario</th><th scope="col">What you get back</th></tr></thead>
        <tbody>
          <tr><th scope="row"><code>4000000000000002</code></th><td>Generic decline</td><td>error code <code>card_declined</code>, decline code <code>generic_decline</code></td></tr>
          <tr><th scope="row"><code>4000000000009995</code></th><td>Insufficient funds</td><td>error code <code>card_declined</code>, decline code <code>insufficient_funds</code></td></tr>
          <tr><th scope="row"><code>4000000000009987</code></th><td>Lost card</td><td>error code <code>card_declined</code>, decline code <code>lost_card</code></td></tr>
          <tr><th scope="row"><code>4000000000009979</code></th><td>Stolen card</td><td>error code <code>card_declined</code>, decline code <code>stolen_card</code></td></tr>
          <tr><th scope="row"><code>4000000000000069</code></th><td>Expired card</td><td>error code <code>expired_card</code></td></tr>
          <tr><th scope="row"><code>4000000000000127</code></th><td>Incorrect CVC</td><td>error code <code>incorrect_cvc</code></td></tr>
          <tr><th scope="row"><code>4000000000000119</code></th><td>Processing error</td><td>error code <code>processing_error</code></td></tr>
          <tr><th scope="row"><code>4242424242424241</code></th><td>Incorrect number</td><td>error code <code>incorrect_number</code></td></tr>
          <tr><th scope="row"><code>4000000000000341</code></th><td>Attaches fine, fails on charge</td><td>error code <code>card_declined</code></td></tr>
        </tbody>
      </table>
    </div>
    <p>Note that the last four in that list return an error code without a separate decline code. <code>expired_card</code>, <code>incorrect_cvc</code>, <code>processing_error</code> and <code>incorrect_number</code> are the error code. If your handler only reads <code>decline_code</code> and ignores <code>code</code>, it will see nothing for these and fall through to a generic branch.</p>"""),
        ("the-good-one", "Why 4000000000000341 is the one you want", """
    <p>Most test cards fail immediately at the point of entry, which tests your checkout form. Card <code>4000000000000341</code> succeeds when attached to a Customer object and fails when you attempt to charge it.</p>
    <p>That is the shape of a real recovery scenario. The customer signed up months ago, the card worked then, and it is failing now on a renewal with nobody watching. If you want to test a dunning sequence, a retry schedule, or a webhook handler end to end, this is the card that gets you there.</p>
    <div class="callout">
      <p><strong>A test worth running:</strong> attach <code>4000000000000341</code>, create a subscription, let the renewal fail, and confirm your system sends exactly one email and not four. Duplicate dunning emails from overlapping webhook handlers are the most common bug in this area and they are invisible until a real customer complains.</p>
    </div>"""),
        ("3ds", "Testing authentication and 3D Secure", """
    <p>Authentication has its own set of cards, because the interesting cases are about whether a payment can complete without the customer being present.</p>
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Card number</th><th scope="col">Behaviour</th></tr></thead>
        <tbody>
          <tr><th scope="row"><code>4000002500003155</code></th><td>Requires authentication for off-session payments unless the card was previously set up. On-session payments always require authentication.</td></tr>
          <tr><th scope="row"><code>4000002760003184</code></th><td>Requires authentication on every transaction, whatever the setup status.</td></tr>
          <tr><th scope="row"><code>4000003800000446</code></th><td>Already set up for off-session use. One-time and on-session payments need authentication; off-session payments succeed.</td></tr>
          <tr><th scope="row"><code>4000000000003220</code></th><td>3D Secure must be completed. Issued in Ireland.</td></tr>
          <tr><th scope="row"><code>4000008400000027</code></th><td>3D Secure must be completed. Issued in the US.</td></tr>
          <tr><th scope="row"><code>4000000032200000</code></th><td>3D Secure required on all transactions, goes through the frictionless flow and succeeds.</td></tr>
        </tbody>
      </table>
    </div>
    <p>The first and third of those are the pair that matters for subscriptions. Between them they show you the difference between a card that will quietly renew for years and one that will demand the customer's attention every cycle. More on that in <a href="/blog/stripe-authentication-required-recovery/">the authentication_required guide</a>.</p>"""),
        ("hard-codes", "Testing the codes that block retries", """
    <p>Three of the nine codes that stop Stripe executing retries have test cards: <code>lost_card</code>, <code>stolen_card</code> and <code>incorrect_number</code>. That is enough to verify the behaviour that surprises people, which is that the retry schedule keeps running and <code>attempt_count</code> keeps rising while nothing is actually being charged.</p>
    <p>Run <code>4000000000009979</code> through a subscription renewal and watch the invoice. If your internal reporting claims eight retry attempts on that invoice, your reporting is describing a schedule, not a set of charges. That distinction is worth catching in test mode rather than in a board meeting.</p>"""),
        ("limits", "What test mode will not tell you", """
    <p>Test cards return a fixed, documented result every time. Real cards do not. A real <code>do_not_honor</code> might approve on Thursday for reasons nobody outside the issuing bank will ever explain, and no amount of test mode work will teach you how often that happens for your customers.</p>
    <p>So use test mode to prove your logic is correct: right branch for the right code, one email not four, access revoked at the right status. Do not use it to estimate recovery rates. Those come from your own production data and from nowhere else, which is also why this site does not publish a recovery rate benchmark for you to borrow.</p>"""),
    ],
    faqs=[
        ("What is the Stripe test card for a declined payment?",
         "4000000000000002 gives a generic decline with error code card_declined and decline code generic_decline. For a specific reason use a specific card: 4000000000009995 for insufficient funds, 4000000000009987 for a lost card, 4000000000009979 for a stolen card."),
        ("Which Stripe test card fails only when charged?",
         "4000000000000341. It succeeds when attached to a Customer object and fails when you attempt the charge. This is the right card for testing renewal failures, dunning sequences and webhook handlers, because it reproduces the situation where a card worked at signup and broke later."),
        ("What is the Stripe test card for an expired card?",
         "4000000000000069. It returns the error code expired_card. Note that this comes back as an error code rather than a decline_code, so handlers that only inspect decline_code will miss it."),
        ("How do I test 3D Secure in Stripe?",
         "4000002760003184 requires authentication on every transaction. 4000002500003155 requires it for off-session payments unless the card was previously set up, which is the more realistic subscription case. 4000000032200000 requires 3D Secure but passes through the frictionless flow and succeeds."),
        ("Can I test Smart Retries with test cards?",
         "You can exercise the retry and webhook behaviour, including watching attempt_count increment on codes that block execution. What you cannot learn is real recovery rates, because test cards return a deterministic result and real issuers do not."),
    ],
    sources=[SRC_TESTING, SRC_CODES, SRC_INVOICE],
    related=[
        ("Which Stripe webhooks tell you a payment failed", "/blog/stripe-failed-payment-webhooks/", "What to handle once your test card fails."),
        ("The nine decline codes that stop retries dead", "/blog/stripe-decline-codes-that-stop-retries/", "Three of them have test cards. Use them."),
        ("Decline code lookup", "/tools/decline-code-lookup/", "All 48 codes with guidance, free and searchable."),
    ],
))

# 10 -----------------------------------------------------------------------
ARTICLES.append(dict(
    slug="stripe-automatic-card-updates",
    title="Stripe automatic card updates, and their limits | RecoverFlow",
    h1="Stripe updates expired cards for you, sometimes",
    desc="Stripe works with the card networks to refresh saved cards automatically. No setup needed, no guarantee, and no way to tell in advance which cards it covers.",
    answer="""      <p>Stripe works with the card networks to automatically update saved card details when a customer gets a new card, whether through expiry, reissue, or a lost and stolen replacement. It is on by default and needs no configuration.</p>
      <p>It is also not guaranteed. It requires the issuing bank to participate, and Stripe states plainly that it is not possible to identify in advance which cards support it. That gap is why you still see <code>expired_card</code> declines.</p>""",
    sections=[
        ("what", "What it does", """
    <p>When a customer's card is replaced, the networks can push the new details to merchants who have the old card on file. Stripe participates in this on your behalf and updates the saved payment method without you doing anything.</p>
    <p>Coverage in the United States is wide, spanning American Express, Visa, Mastercard and Discover. Internationally it varies by country. The binding constraint everywhere is the issuer: automatic card updates require card issuers to participate with the network and provide the information.</p>
    <p>This is genuinely valuable and it is free, which makes it one of the better arguments for keeping cards on file in Stripe rather than anywhere else.</p>"""),
        ("gap", "Why you still get expired card declines", """
    <p>If this worked every time, <code>expired_card</code> would not appear in anyone's dashboard. It appears in everyone's.</p>
    <p>Three reasons. The issuer may not participate. The update may not arrive before your renewal does. And the customer may have been reissued a card by a bank that treats the replacement as a new account rather than an update.</p>
    <div class="callout">
      <p><strong>The planning consequence:</strong> treat automatic updates as something that reduces your expired card volume, not something that eliminates it. Any recovery process that assumes cards fix themselves will be wrong for the remainder, and the remainder is where the churn is.</p>
    </div>
    <p>This also explains an oddity covered elsewhere on this site: <code>expired_card</code> is not one of the nine codes Stripe refuses to retry. Stripe will genuinely retry it, and occasionally that retry succeeds precisely because an update landed in between. <a href="/blog/expired-card-stripe-retries/">The expired card guide</a> goes into when that is worth waiting for.</p>"""),
        ("webhooks", "Knowing when it happens", """
    <p>Two events tell you a saved card changed:</p>
    <ul>
      <li><strong><code>payment_method.automatically_updated</code></strong> fires when the network pushed an update. This is the one that matters here.</li>
      <li><strong><code>payment_method.updated</code></strong> fires when the change came from your own API call.</li>
    </ul>
    <p>Both include the card's new expiration date and last four digits so you can keep your own records straight. That last point is more useful than it sounds. If you show customers "your card ending 4242" in billing emails and you cached that value at signup, an automatic update will silently make your emails wrong, and a customer who cannot find the card you are describing is a customer who does not update it.</p>"""),
        ("what-you-can-do", "What to do about the part it does not cover", """
    <p>The lever you control is time. A card's expiry date is not a secret: it is on the payment method as <code>exp_month</code> and <code>exp_year</code>, and you can see a failure coming weeks out.</p>
    <p>Stripe will send a card expiry email roughly a month ahead if you enable it, and that costs nothing. Beyond that, the useful move is to query for cards expiring in the next cycle and reach the customers whose renewal date falls after their expiry date. That is a small population and a high value one, because you are asking before anything has broken rather than after.</p>
    <p>Note the limits of the API here: an update call can change the name, billing address, expiration date or metadata on a card. Anything else means the customer supplies a new card. You cannot patch your way out of a genuinely dead payment method.</p>"""),
    ],
    faqs=[
        ("Does Stripe automatically update expired cards?",
         "Often, yes. Stripe works with the card networks to automatically attempt to update saved card details when a customer receives a new card. It requires no configuration. It is not guaranteed, because the issuing bank has to participate with the network and provide the information."),
        ("Which card networks support automatic updates in Stripe?",
         "In the United States, support is wide across American Express, Visa, Mastercard and Discover. International support varies by country. In all cases the individual issuer must participate."),
        ("Can I tell which of my customers' cards will update automatically?",
         "No. Stripe states that it is not possible to identify cards that support automatic updates. You find out when it happens, or when it does not and you get a decline."),
        ("How do I know when Stripe updates a card?",
         "Listen for payment_method.automatically_updated, which fires on network-driven updates. payment_method.updated covers changes you made through the API. Both include the new expiration date and last four digits."),
        ("Does automatic card updating cost extra?",
         "Stripe's documentation does not list a cost for it. It works as part of keeping cards on file, with no separate setup step."),
    ],
    sources=[SRC_CARDUPDATE, SRC_EMAILS, SRC_CODES],
    related=[
        ("Why retrying an expired card rarely works", "/blog/expired-card-stripe-retries/", "The other half of the expired card problem."),
        ("Stripe's dunning emails: what you control", "/blog/stripe-dunning-emails-what-you-control/", "Including the free card expiry notice."),
        ("Dunning email generator", "/tools/dunning-email-generator/", "Copy for the cards that did not update themselves."),
    ],
))

# 11 -----------------------------------------------------------------------
ARTICLES.append(dict(
    slug="do-not-honor-stripe-decline",
    title="What do_not_honor means on a Stripe payment | RecoverFlow",
    h1="What <code>do_not_honor</code> actually means",
    desc="do_not_honor means the issuer declined without saying why. Stripe will retry it, the bank will not explain it, and the only real fix runs through the customer.",
    answer="""      <p><code>do_not_honor</code> means the card was declined for an unknown reason. That is not a summary, it is the whole content of the message. The issuing bank refused and chose not to say why.</p>
      <p>Stripe's recommended next step is that the customer contacts their card issuer. It is not one of the nine codes that block retries, so Stripe will keep attempting it, and sometimes those attempts work.</p>""",
    sections=[
        ("meaning", "Why the bank will not tell you", """
    <p><code>do_not_honor</code> is a catch-all. Issuers use it when they have declined for a reason they do not want to publish to a merchant: a fraud model fired, an internal limit was hit, the account has a flag on it, the transaction looked wrong in a way their system will not itemise.</p>
    <p>They are not being obstructive. Telling merchants precisely why a card was refused is a good way to teach card fraudsters what to avoid. The opacity is deliberate and it is not going to change, so the useful question is not what it means but what to do.</p>"""),
        ("cousins", "It has two close relatives, and they need different handling", """
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Code</th><th scope="col">Stripe's description</th><th scope="col">Stripe's recommended next step</th></tr></thead>
        <tbody>
          <tr><th scope="row"><code>do_not_honor</code></th><td>The card was declined for an unknown reason.</td><td>The customer needs to contact their card issuer.</td></tr>
          <tr><th scope="row"><code>generic_decline</code></th><td>Declined for an unknown reason, or Stripe Radar or Adaptive Acceptance blocked the payment.</td><td>The customer needs to contact their card issuer.</td></tr>
          <tr><th scope="row"><code>try_again_later</code></th><td>The card was declined for an unknown reason.</td><td>Ask the customer to attempt the payment again. If subsequent payments are declined, they need to contact their issuer.</td></tr>
        </tbody>
      </table>
    </div>
    <p>The one to separate out is <code>generic_decline</code>, because it has a second meaning the others do not: it can be Stripe's own Radar blocking the payment rather than the bank. If you are seeing a lot of <code>generic_decline</code> on renewals for customers who have paid you happily for a year, look at your Radar rules before you write a single dunning email. That is not a customer problem.</p>
    <p><code>try_again_later</code> is the only one of the three where Stripe explicitly suggests simply trying again, which makes it the mildest of the group.</p>"""),
        ("retry", "Is it worth retrying?", """
    <p>Yes, and you do not have to do anything to make that happen. <code>do_not_honor</code> is not on the list of nine codes that stop Stripe executing retries, so Smart Retries will work through its schedule normally.</p>
    <p>Whether those retries succeed is genuinely unpredictable. The underlying cause might be a temporary hold that clears in a day, or a permanent block that will never clear. From outside the bank these look identical, which is the central frustration of this code.</p>
    <div class="callout">
      <p><strong>The one signal you have:</strong> a customer whose payments have succeeded for months and now returns <code>do_not_honor</code> is a different case from a brand new customer whose first payment returns it. The first is worth patient retries. The second is often a card that was never going to work, and the 23 hour window on <code>incomplete</code> subscriptions means you do not have long to find out.</p>
    </div>"""),
        ("email", "What to write to the customer", """
    <p>Do not paste the code into the email. "Your payment was declined with do_not_honor" tells the customer nothing they can act on and makes you sound like a log file.</p>
    <p>The message that works says three things: the payment did not go through, you do not know why because the bank did not say, and the two things that usually fix it are trying a different card or calling the number on the back of the current one. That last part is Stripe's own recommended next step and it is the only genuinely useful instruction available.</p>
    <p>Unlike the lost and stolen codes, there is no security reason to be vague here. You can be completely straight about not knowing, and being straight tends to read better than a vague apology. The <a href="/tools/dunning-email-generator/">dunning email generator</a> on this site produces reason-aware copy including this case.</p>"""),
    ],
    faqs=[
        ("What does do_not_honor mean on Stripe?",
         "It means the card was declined for an unknown reason. The issuing bank refused the charge and did not provide a reason. Stripe's recommended next step is for the customer to contact their card issuer."),
        ("Is do_not_honor a hard decline?",
         "Not in the sense that matters on Stripe. It is not one of the nine decline codes that stop Stripe executing retries, so retries proceed normally on the usual schedule. Whether they succeed depends on a cause the bank will not disclose."),
        ("What is the difference between do_not_honor and generic_decline?",
         "Both mean an unknown decline, but generic_decline can also indicate that Stripe Radar or Adaptive Acceptance blocked the payment rather than the bank. A cluster of generic_decline on long-standing customers is a reason to review your Radar rules."),
        ("How do I fix a do_not_honor decline?",
         "You cannot fix it from your side. The customer either uses a different payment method or contacts their card issuer. Both are worth offering in the same email, because some customers will do one and not the other."),
        ("Should I keep retrying do_not_honor?",
         "Stripe will, by default, and that is reasonable. Some causes are temporary holds that clear. The judgement call is when to stop treating it as a retry problem and start treating it as a conversation, which for an established customer is usually after the schedule has had a fair run."),
    ],
    sources=[SRC_CODES, SRC_SMART, SRC_DECLINES],
    related=[
        ("The nine decline codes that stop retries dead", "/blog/stripe-decline-codes-that-stop-retries/", "do_not_honor is not one of them, and that matters."),
        ("Decline code lookup", "/tools/decline-code-lookup/", "All 48 codes, searchable, free."),
        ("Is insufficient_funds a hard decline?", "/blog/is-insufficient-funds-a-hard-decline/", "Another code widely mislabelled in published guidance."),
    ],
))

# 12 -----------------------------------------------------------------------
ARTICLES.append(dict(
    slug="stripe-authentication-required-recovery",
    title="Recovering authentication_required declines on Stripe | RecoverFlow",
    h1="Recovering <code>authentication_required</code> declines",
    desc="authentication_required means the payment needs the cardholder to authenticate. Retries cannot fix it. The webhook and the hosted invoice page can.",
    answer="""      <p><code>authentication_required</code> means the payment needs Strong Customer Authentication and only the cardholder can complete it. It is one of the nine codes where Stripe will not execute a retry, because no retry can supply what is missing.</p>
      <p>The recovery path is <code>invoice.payment_action_required</code>, which fires when the invoice requires customer authentication, and a link that gets the customer to the authentication step.</p>""",
    sections=[
        ("why", "Why this one is different from the other eight", """
    <p>The other codes that block retries describe a card that will not work: lost, stolen, revoked, blocked, mistyped. <code>authentication_required</code> describes a card that works fine and a customer who has not confirmed it is them.</p>
    <p>That makes it the most recoverable of the nine by a distance. Nothing is broken, nobody has cancelled anything, and no new card is needed. The customer has to tap a button in their banking app. If you can get them to that button, the payment goes through.</p>
    <p>It also makes it the most wasteful one to mishandle. Sending a "please update your payment method" email to someone whose payment method is perfectly good is a good way to make them think about whether they still want the subscription.</p>"""),
        ("flow", "The events to handle", """
    <p><code>invoice.payment_action_required</code> is sent when the invoice requires customer authentication. That is your trigger, and it is separate from <code>invoice.payment_failed</code> for good reason: the required action is different.</p>
    <p>Handle it by getting the customer to the hosted invoice page, where Stripe will run the authentication flow. The email should say what is actually happening. Something like "your bank needs you to confirm this payment" is accurate, short, and does not imply the customer did anything wrong.</p>
    <div class="callout">
      <p><strong>The mistake to avoid:</strong> routing this into the same dunning sequence as expired cards. The ask is different, the urgency is different, and the customer's mental state on receiving it is completely different. One is "your card broke", the other is "your bank wants a thumbs up".</p>
    </div>"""),
        ("off-session", "Off-session is the whole game", """
    <p>Subscription renewals happen when the customer is not there. That is what off-session means, and it is why authentication is a problem for subscriptions in a way it is not for checkout.</p>
    <p>Stripe's test cards make the distinction visible. <code>4000002500003155</code> requires authentication for off-session payments <em>unless the card was previously set up</em>, while on-session payments always require it. <code>4000003800000446</code> is already set up for off-session use, so off-session payments succeed while one-time and on-session payments still need authentication.</p>
    <p>The practical reading: a card properly set up for off-session use at signup is far less likely to demand authentication at renewal than one that was not. If you are seeing <code>authentication_required</code> regularly on renewals, the fix is upstream in how payment methods are saved, not downstream in your dunning copy.</p>"""),
        ("testing", "Testing it", """
    <p>Use <code>4000002760003184</code> to force authentication on every transaction, which is the fastest way to see the event fire and confirm your handler works. Then use <code>4000002500003155</code> for the more realistic case, where behaviour depends on whether the card was set up for off-session use.</p>
    <p><code>4000000032200000</code> is the useful control: it requires 3D Secure on all transactions but proceeds through the frictionless flow and succeeds. If your code treats that as a failure, you are about to send dunning emails to customers who paid you. There is a fuller list in <a href="/blog/stripe-test-cards-for-failed-payments/">the test cards guide</a>.</p>"""),
        ("counting", "Finding yours", """
    <p>On a failed PaymentIntent the code sits at <code>last_payment_error.decline_code</code>. Count how many of your unpaid invoices carry <code>authentication_required</code> against how many carry a genuinely dead card.</p>
    <p>If the authentication share is meaningful, you have a recoverable population that a card-update email will not reach, because those customers do not need to update anything. Splitting that group out is usually the single highest value change to a home built dunning sequence, and it costs nothing but a branch in your code.</p>"""),
    ],
    faqs=[
        ("What does authentication_required mean on Stripe?",
         "The payment needs Strong Customer Authentication, and only the cardholder can complete it. The card itself is fine. It is one of the nine decline codes where Stripe will not execute a retry, because a retry cannot provide the authentication."),
        ("Can Stripe retry an authentication_required decline?",
         "Not usefully. It is on Stripe's list of codes that require a new payment method before retries execute. Stripe keeps the schedule running and keeps incrementing attempt_count, but the charge is not sent. The recovery path is getting the customer to authenticate, not waiting."),
        ("Which webhook fires for authentication_required?",
         "invoice.payment_action_required, which Stripe sends when the invoice requires customer authentication. Handle it separately from invoice.payment_failed, because the customer action needed is completely different."),
        ("How do I stop authentication_required happening at renewal?",
         "Make sure payment methods are set up for off-session use when the customer signs up. Stripe's test cards show the difference directly: a card previously set up for off-session use can renew without authentication, while one that was not may require it every time."),
        ("Should authentication_required customers get the same email as expired card customers?",
         "No. Their card is not broken and there is nothing to update. Asking them to add a new payment method is confusing and invites them to reconsider the subscription. Tell them their bank needs them to confirm the payment, and link them to the hosted invoice page."),
    ],
    sources=[SRC_SMART, SRC_WEBHOOKS, SRC_TESTING],
    related=[
        ("The nine decline codes that stop retries dead", "/blog/stripe-decline-codes-that-stop-retries/", "Where authentication_required sits among the others."),
        ("Stripe test cards for failed payments", "/blog/stripe-test-cards-for-failed-payments/", "Every 3D Secure test card and what it does."),
        ("Which Stripe webhooks tell you a payment failed", "/blog/stripe-failed-payment-webhooks/", "Handling payment_action_required alongside payment_failed."),
    ],
))

# 13 -----------------------------------------------------------------------
# Written 16 August 2026. Until this page existed the reattempt budget lived in
# exactly one place on the site, inside the retry waste calculator, which is a
# tool page and not something anyone links to as a reference. Every number here
# is reconciled against that calculator on purpose: the calculator says 15 per
# card per 30 days, Stripe's own support page says 15 retries of a single
# payment over 30 calendar days, and the page below states both rather than
# picking one and quietly contradicting the other tool on the same domain.
#
# No fee figure appears anywhere on this page. The per-attempt penalty amounts
# are published by processors rather than by Visa or Stripe, and the calculator
# does not quote them, so neither does this.
ARTICLES.append(dict(
    slug="visa-excessive-reattempts-rule",
    # Search Console, 31 July to 27 August 2026: 94 impressions at average
    # position 4.2 for the bare query "ai10325", and no clicks at all. The
    # bulletin number was buried in a section heading while the title and the
    # description both talked about the rule without ever naming the document
    # the searcher typed in, so the result did not look like an answer.
    title="Visa AI10325: the excessive reattempts rule on Stripe | RecoverFlow",
    h1="The Visa excessive reattempts rule: 15 tries per card, per 30 days",
    desc="Visa bulletin AI10325 caps reattempts of one payment at 15 per 30 calendar days, from 17 April 2021. What Stripe does at the limit, and where the budget is zero.",
    published="2026-08-16",
    modified="2026-08-16",
    updated="16 August 2026",
    answer="""      <p>Visa's rules broadly prohibit more than 15 retries of a single payment over 30 calendar days. Because a subscription's retries all land on the same card for the same invoice, that is usually described as a budget of <strong>15 reattempts per card per rolling 30 days</strong>.</p>
      <p>Stripe enforces it. After the 15th retry attempt on a Visa transaction, Stripe automatically blocks subsequent retry attempts where it determines there is a low chance of a successful authorization.</p>
      <p>For one group of declines the budget is not 15. It is zero, and the first reattempt is already one too many.</p>""",
    sections=[
        ("rule", "The rule, in the fewest words possible", """
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">The limit</th><th scope="col">What it is</th><th scope="col">Where it comes from</th></tr></thead>
        <tbody>
          <tr><th scope="row">Reattempts of one failed payment</th><td>No more than 15 in 30 calendar days</td><td>Visa's rules, quoted directly by Stripe</td></tr>
          <tr><th scope="row">Reattempts after a Category 1 response</th><td>None, using the same account information</td><td>Visa's authorization response code categories</td></tr>
          <tr><th scope="row">What Stripe does at the limit</th><td>Blocks subsequent retry attempts after the 15th on a Visa transaction, where it judges a low chance of authorization</td><td>Stripe support documentation</td></tr>
          <tr><th scope="row">How a blocked attempt looks in the API</th><td><code>outcome.type</code> of <code>blocked</code>, <code>outcome.reason</code> of <code>previously_declined_do_not_retry</code>, surfaced to you as <code>card_declined</code> with <code>generic_decline</code></td><td>Stripe support documentation</td></tr>
          <tr><th scope="row">Stripe Smart Retries recommended default</th><td>8 tries within 2 weeks</td><td>Stripe Billing documentation</td></tr>
        </tbody>
      </table>
    </div>
    <p>Two things in that table are worth reading twice. The budget is counted over a rolling window, not a calendar month, so it does not reset on the 1st. And the last row is the interesting one: Stripe's own recommended default sits at roughly half the network ceiling, which is not an accident.</p>

    <h3>When it started, since the date is reported wrong constantly</h3>
    <p>The rule takes effect <strong>17 April 2021</strong>. That date is in Visa's own bulletin, article AI10325, which says in its overview: "Effective 17 April 2021, Visa will update its rules for declined transaction resubmission and the use of authorization response codes." The same document is where the 15-in-30 figure comes from, in the passage moving four response codes into Category 2 "to allow merchants to reattempt up to 15 times in 30 days".</p>
    <p>April 2022 is widely repeated as the start date and it refers to something else: a later phase of the associated fee schedule, not the reattempt cap. If you are reconciling a statement line, the fee timeline and the rule timeline are two different things.</p>"""),
        ("stripe-behaviour", "What Stripe does when you cross it", """
    <p>You do not get a warning email. The attempt simply stops being an attempt.</p>
    <p>Stripe's own description is that after the 15th retry attempt on a Visa transaction, it will automatically block subsequent retry attempts if it determines there is a low chance of a successful authorization for the given charge. The charge that comes back is not an issuer decline. It never reached the issuer. In the API it carries an <code>outcome.type</code> of <code>blocked</code> and an <code>outcome.reason</code> of <code>previously_declined_do_not_retry</code>, while the error your code sees is the ordinary <code>card_declined</code> with a <code>decline_code</code> of <code>generic_decline</code>.</p>
    <div class="callout">
      <p><strong>Why this matters for your reporting:</strong> a blocked attempt shows up in your failed payments as <code>generic_decline</code>, which is the least informative code Stripe has. If you are counting decline codes to decide what to do next, budget exhaustion hides inside your <code>generic_decline</code> bucket wearing a disguise. Check <code>outcome.reason</code> on the charge, not just the decline code.</p>
    </div>
    <p>Stripe describes a related behaviour on its declines page for accounts on interchange plus pricing, where Adaptive Acceptance blocks certain payments to help avoid unnecessary network costs, giving an <code>outcome.reason</code> of <code>low_probability_of_authorization</code> and an <code>advice_code</code> of <code>do_not_try_again</code>. Different mechanism, same lesson: an attempt that the network would rather you did not send can be stopped before it is sent.</p>"""),
        ("category-one", "The declines where the budget is zero, not fifteen", """
    <p>Visa sorts its authorization response codes into categories. Category 1 is defined as the issuer will never approve, and a transaction that receives one should not be resubmitted using the same account information. Categories 2 and 3 are the ones the 15 in 30 days allowance applies to.</p>
    <p>Category 1 is where the account is gone rather than temporarily unable to pay: a card reported lost, a card reported stolen, a closed account, an account that does not exist. In Stripe's vocabulary those arrive as codes like <code>lost_card</code>, <code>stolen_card</code>, <code>pickup_card</code>, <code>incorrect_number</code> and <code>invalid_account</code>.</p>
    <p>So the arithmetic changes shape. On an ordinary soft decline you have a budget of 15 and Stripe's default spends 8 of it. On a Category 1 response you have a budget of zero, and the very first reattempt is already excessive. Nothing about the retry configuration in your Dashboard knows the difference.</p>
    <p>This is the same population as the <a href="/blog/stripe-decline-codes-that-stop-retries/">nine codes Stripe will not execute a retry for</a>, but the two lists are not identical and they exist for different reasons, which is the next section.</p>"""),
        ("eight", "Why Stripe's default is 8 and not 15", """
    <p>Smart Retries recommends 8 tries within 2 weeks. The network allows 15 over 30 days. Stripe is deliberately leaving room.</p>
    <p>Part of that is the budget itself. Attempts are not free actions, and a schedule that spends the whole allowance leaves nothing for a manual retry, a customer-triggered payment from the hosted invoice page, or the attempt that follows once a new card is finally added. Part of it is that issuers watch attempt frequency: a card being hammered looks less like a subscription renewal and more like someone testing a stolen number, and the sensible issuer response to that is to decline more.</p>
    <p>The practical read: 8 within 2 weeks is not a timid default you should raise to look thorough. It is a schedule that finishes with headroom, on purpose.</p>"""),
        ("two-limits", "Two separate limits people keep merging into one", """
    <p>This is the point almost every article on failed payments gets wrong, and it is worth being pedantic about because the two limits behave completely differently.</p>
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col"></th><th scope="col">The nine code suppression</th><th scope="col">The reattempt budget</th></tr></thead>
        <tbody>
          <tr><th scope="row">Who imposes it</th><td>Stripe</td><td>Visa</td></tr>
          <tr><th scope="row">What it limits</th><td>Whether a scheduled retry is executed at all</td><td>How many executed attempts one payment may have</td></tr>
          <tr><th scope="row">Does the attempt reach the issuer</th><td>No, and no Charge is created</td><td>Yes, until the budget runs out</td></tr>
          <tr><th scope="row">What clears it</th><td>Attaching a new payment method</td><td>Time, as the rolling 30 day window moves</td></tr>
          <tr><th scope="row">What you see</th><td><code>attempt_count</code> climbing with nothing happening</td><td>A blocked charge reported as <code>generic_decline</code></td></tr>
        </tbody>
      </table>
    </div>
    <p>They are independent. An invoice can sit well inside its 15 and still be going nowhere because it is on one of the nine. Another can be clear of all nine and still be blocked because the budget is spent. Fixing one tells you nothing about the other.</p>
    <p>The reason this gets expensive is that the two systems retrying your invoices cannot see each other's counters. Stripe's Smart Retries draw down the budget. A third-party recovery tool retrying on top of Stripe draws down the same budget. Your own manual retry from the Dashboard draws down the same budget. Nobody is keeping a running total on your behalf unless something is built to do it.</p>"""),
        ("count", "How to see where you stand", """
    <p>There is no dashboard number for this, which is the whole problem. You can get close from the API.</p>
    <ul>
      <li>On the invoice, <code>attempt_count</code> is the number of payment attempts from the perspective of the retry schedule. Stripe documents that it keeps incrementing even when a hard decline means the retry does not execute, so it is an upper bound on real network attempts rather than a count of them.</li>
      <li>On each failed charge, <code>outcome.network_status</code> tells you whether the attempt actually went out. <code>declined_by_network</code> means it did. <code>not_sent_to_network</code> means it did not, and that one did not cost you budget.</li>
      <li><code>outcome.reason</code> of <code>previously_declined_do_not_retry</code> is the marker that you have already crossed the line on that card.</li>
    </ul>
    <p>Count attempts per card rather than per invoice. A customer whose renewal failed in June and again in July is one card carrying two invoices worth of attempts inside the same rolling window, and the window does not care which invoice they belonged to.</p>
    <p>If you want the money version of this rather than the count version, the <a href="/tools/retry-waste-calculator/">retry waste calculator</a> takes your decline code counts and returns what share of your failed revenue is sitting on cards that no attempt can collect from. It runs in the browser and uploads nothing.</p>"""),
    ],
    faqs=[
        ("How many times can you retry a failed Visa payment?",
         "Visa's rules broadly prohibit more than 15 retries of a single payment over 30 calendar days, and Stripe quotes that figure directly. In a subscription those retries all land on the same card for the same invoice, which is why the rule is usually described as 15 per card per 30 days. The window is rolling, so it does not reset at the start of a month."),
        ("What happens after the 15th retry?",
         "Stripe automatically blocks subsequent retry attempts on a Visa transaction where it determines there is a low chance of a successful authorization. The attempt never reaches the issuer. In the API the charge carries an outcome type of blocked with a reason of previously_declined_do_not_retry, and it surfaces to your code as card_declined with a decline_code of generic_decline."),
        ("Do Stripe's own Smart Retries count towards the 15?",
         "Every executed attempt on the card counts, whoever sent it. Stripe's scheduled retries, a manual retry you click in the Dashboard, and a third-party recovery tool's retries all draw on the same budget, and none of those systems can see the others' counters. That is why Stripe's recommended default of 8 tries in 2 weeks leaves room rather than filling the allowance."),
        ("Are there declines where even one retry is too many?",
         "Yes. Visa's Category 1 responses mean the issuer will never approve, and the transaction should not be resubmitted using the same account information. A card reported lost, a card reported stolen, a closed account and an account that never existed all sit in that group. The permitted number of reattempts there is zero, not 15."),
        ("Is this the same thing as the nine codes Stripe will not retry?",
         "No, and conflating them is the most common mistake in this area. The nine codes are a Stripe-side suppression: Stripe keeps the schedule running but does not send the charge until a new payment method exists. The reattempt rule is a network-side budget on attempts that do get sent. A payment can be well inside its 15 and still blocked by the nine, or clear of the nine and out of budget."),
    ],
    sources=[SRC_EXCESSIVE, SRC_VISA_RESUB, SRC_VISA_CATS, SRC_SMART, SRC_DECLINES],
    related=[
        ("Stripe is not retrying your failed invoice", "/blog/stripe-not-retrying-failed-invoice/", "The nine reasons, and the API field to check for each one."),
        ("The nine codes that stop retries dead", "/blog/stripe-decline-codes-that-stop-retries/", "The other limit, the one Stripe imposes rather than Visa."),
        ("Retry waste calculator", "/tools/retry-waste-calculator/", "Free tool. What share of your failed revenue no attempt can reach."),
    ],
))

# 14 -----------------------------------------------------------------------
# Every cause below was confirmed against a primary Stripe page before it was
# written down. Four are stated outright by the Smart Retries page, four come
# from documented field behaviour on the invoice object, and one from the local
# payment method retry table on the same Smart Retries page. Nothing here is
# inferred from how the product behaves in our own account.
ARTICLES.append(dict(
    slug="stripe-not-retrying-failed-invoice",
    title="Stripe is not retrying your failed invoice: 9 reasons | RecoverFlow",
    h1="Stripe is not retrying your failed invoice. Here are the nine reasons why.",
    desc="A checklist for the moment a subscription invoice fails and no retry is scheduled. Nine documented causes, and the exact API field that proves each one.",
    published="2026-08-16",
    modified="2026-08-16",
    updated="16 August 2026",
    answer="""      <p>Stripe states four of these outright: it does not retry when no payment methods are available, when the issuer returned a hard decline code, when the card is India-issued, or when the Connect account has been disconnected.</p>
      <p>The other five are configuration. The invoice is on <code>send_invoice</code>, <code>auto_advance</code> is off, the retry schedule is finished, the subscription already moved to <code>unpaid</code> or <code>canceled</code>, or the payment is a local payment method whose retries are off by default.</p>
      <p>Read <code>next_payment_attempt</code> on the invoice first. It answers whether there is a retry coming, and everything below explains why there is not.</p>""",
    sections=[
        ("first", "Start with one field", """
    <p>Pull the invoice and look at <code>next_payment_attempt</code>. Stripe documents it as the time at which payment will next be attempted, and as <code>null</code> for invoices where <code>collection_method=send_invoice</code>.</p>
    <p>If it holds a timestamp, Stripe does intend to retry and your problem is timing, not configuration. Wait for that moment and check again. If it is <code>null</code>, one of the nine below is true.</p>
    <p>While you are there, note <code>status</code> and <code>attempt_count</code>. An invoice that is <code>draft</code> is not being collected at all. An <code>attempt_count</code> that keeps rising while nothing appears in your payments list is the signature of cause 2.</p>"""),
        ("documented", "The four Stripe states outright", """
    <p>Stripe's Smart Retries page lists these as the conditions under which it does not retry payments. They are quoted, not inferred.</p>
    <ol>
      <li>
        <p><strong>No payment methods are available.</strong> There is nothing to charge. Stripe retries against the first available payment method in a documented order: the subscription's <code>default_payment_method</code>, then the subscription's <code>default_source</code>, then the customer's <code>invoice_settings.default_payment_method</code>, then the legacy <code>customer.default_source</code>. Check all four, in that order.</p>
        <p>There is a trap here worth knowing. Stripe says that when you update payment methods after a failed attempt, you should update the field where the previous payment failed. If the subscription has a <code>default_payment_method</code> and you only update <code>customer.invoice_settings.default_payment_method</code>, Stripe carries on retrying the subscription's one. Your customer added a working card and nothing changed.</p>
      </li>
      <li>
        <p><strong>The issuer returned a hard decline code.</strong> Nine codes stop execution: <code>incorrect_number</code>, <code>lost_card</code>, <code>pickup_card</code>, <code>stolen_card</code>, <code>revocation_of_authorization</code>, <code>revocation_of_all_authorizations</code>, <code>authentication_required</code>, <code>highest_risk_level</code> and <code>transaction_not_allowed</code>. Read <code>last_payment_error.decline_code</code> on the failed PaymentIntent.</p>
        <p>Stripe is precise about what happens next, and it is not what most people expect: retries continue to be scheduled and <code>attempt_count</code> continues to increment, but retries only execute after a new payment method is detected, and unexecuted retries do not create a new Charge. So the invoice looks busy while nothing is being attempted. <a href="/blog/stripe-decline-codes-that-stop-retries/">Full breakdown of the nine</a>.</p>
      </li>
      <li>
        <p><strong>The payment card is India-issued.</strong> Check <code>card.country</code> on the payment method for <code>IN</code>. Recurring payments on India-issued cards run under the Reserve Bank of India's e-mandate rules, which require a registered mandate authenticated by the cardholder, a pre-debit notification at least 24 hours before each charge, and fresh authentication above 15,000 INR. Stripe cannot satisfy that with a silent background retry. Watch for <code>payment_intent_mandate_invalid</code> and <code>india_recurring_payment_mandate_canceled</code> on the PaymentIntent.</p>
      </li>
      <li>
        <p><strong>The Stripe Connect account has been disconnected.</strong> If you are a platform, the merchant may have revoked you. Stripe sends <code>account.application.deauthorized</code> when a user disconnects your platform from their account. If you are the merchant and an app was retrying on your behalf, the same event is why it stopped.</p>
      </li>
    </ol>"""),
        ("config", "The five that are your own configuration", """
    <p>None of these are failures. They are settings doing exactly what they say, usually set months ago by someone else.</p>
    <ol start="5">
      <li>
        <p><strong><code>collection_method</code> is <code>send_invoice</code>.</strong> Stripe documents the two values plainly: with <code>charge_automatically</code> it attempts payment using the default source attached to the customer, and with <code>send_invoice</code> it emails the invoice to the customer with payment instructions. There is no card charge to retry, which is why <code>next_payment_attempt</code> is documented as <code>null</code> for these. What you want instead is the unpaid invoice reminder, which is a separate setting and is covered in the <a href="/blog/stripe-free-dunning-settings-checklist/">free settings checklist</a>.</p>
      </li>
      <li>
        <p><strong><code>auto_advance</code> is <code>false</code>.</strong> Documented as controlling whether Stripe performs automatic collection of the invoice, and if false, the invoice's state does not automatically advance without an explicit action. An invoice created by your own code with <code>auto_advance</code> off will sit there indefinitely, correctly, forever.</p>
      </li>
      <li>
        <p><strong>The retry schedule is finished.</strong> Stripe's wording is that after the final payment attempt, no further payment attempts are made, and that changing your subscription settings only affects future retries. Widening the window today does not give an already-exhausted invoice more chances. Check the configured schedule at <strong>Billing</strong> &gt; <strong>Revenue recovery</strong> &gt; <strong>Retries</strong> and compare it with <code>attempt_count</code>.</p>
      </li>
      <li>
        <p><strong>The subscription already moved on.</strong> When recovery fails, the subscription transitions according to your setting: cancel it, mark it <code>unpaid</code>, or leave it <code>past_due</code>. The <code>unpaid</code> branch is the one that confuses people, because Stripe documents that invoices continue to be generated and stay in a draft state. New invoices keep appearing and none of them are collected, which looks exactly like a broken retry engine. Read <code>subscription.status</code> and <code>invoice.status</code> together.</p>
      </li>
      <li>
        <p><strong>It is a local payment method and retries are off.</strong> Stripe is explicit that by default it does not automatically retry failed payments made with local payment methods. ACH Direct Debit, ACSS, Bacs, SEPA and both Australian and New Zealand BECS all need the <strong>Local payment methods</strong> section turned on. Even switched on, the allowances are small and the only retryable failure is insufficient funds.</p>
      </li>
    </ol>"""),
        ("table", "The whole checklist in one table", """
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">#</th><th scope="col">Cause</th><th scope="col">Field or setting to read</th></tr></thead>
        <tbody>
          <tr><th scope="row">1</th><td>No payment method available</td><td><code>subscription.default_payment_method</code>, <code>subscription.default_source</code>, <code>customer.invoice_settings.default_payment_method</code>, <code>customer.default_source</code></td></tr>
          <tr><th scope="row">2</th><td>Hard decline code</td><td><code>last_payment_error.decline_code</code>, against the nine</td></tr>
          <tr><th scope="row">3</th><td>India-issued card</td><td><code>card.country</code> equal to <code>IN</code>, plus mandate status</td></tr>
          <tr><th scope="row">4</th><td>Connect account disconnected</td><td>the <code>account.application.deauthorized</code> event</td></tr>
          <tr><th scope="row">5</th><td>Manual collection</td><td><code>invoice.collection_method</code> equal to <code>send_invoice</code></td></tr>
          <tr><th scope="row">6</th><td>Automatic advancement off</td><td><code>invoice.auto_advance</code> equal to <code>false</code></td></tr>
          <tr><th scope="row">7</th><td>Schedule exhausted</td><td><code>invoice.attempt_count</code> against your configured retry policy</td></tr>
          <tr><th scope="row">8</th><td>Subscription already unpaid or canceled</td><td><code>subscription.status</code>, and <code>invoice.status</code> of <code>draft</code></td></tr>
          <tr><th scope="row">9</th><td>Local payment method with retries off</td><td>the Local payment methods retry setting</td></tr>
        </tbody>
      </table>
    </div>"""),
        ("not-this", "One more thing that looks identical and is not on the list", """
    <p>If you have ruled out all nine and attempts are still coming back failed without ever reaching the bank, check whether the attempt was blocked rather than declined.</p>
    <p>Visa's rules broadly prohibit more than 15 retries of a single payment over 30 calendar days, and Stripe blocks subsequent retry attempts after the 15th on a Visa transaction where it judges a low chance of authorization. That charge reports an <code>outcome.type</code> of <code>blocked</code> with an <code>outcome.reason</code> of <code>previously_declined_do_not_retry</code>, while the error your code sees is a plain <code>generic_decline</code>. It is a different failure from every cause above, because here Stripe did schedule the retry and did try to send it.</p>
    <p>This is easy to miss because it hides inside your most boring decline code. There is <a href="/blog/visa-excessive-reattempts-rule/">a full page on the reattempt budget</a>, including why it is a separate limit from the nine codes.</p>"""),
    ],
    faqs=[
        ("Why is next_payment_attempt null on my invoice?",
         "Stripe documents it as null for invoices where collection_method is send_invoice. On an automatically collected invoice, null means no further attempt is scheduled: the retry schedule is finished, the invoice is no longer open, auto_advance is off, or the subscription has already moved to unpaid or canceled."),
        ("Does attempt_count going up mean Stripe is really charging the card?",
         "No. Stripe documents that when a failure returns a non-retryable code, retries continue to be scheduled and attempt_count continues to increment, but retries only execute once a new payment method is obtained, and unexecuted retries do not create a new Charge. If attempt_count is climbing and no new charges appear, that is the reason."),
        ("Does Stripe retry failed ACH and SEPA payments?",
         "It can, but not by default. Stripe states that by default it does not automatically retry failed payments made with local payment methods, and you turn it on in the Local payment methods section. The only retryable failure is insufficient funds, and the allowances are small: ACH Direct Debit is 2 retries over 40 days, SEPA, Bacs and Australia BECS are 2 retries over 30 days, and ACSS and New Zealand BECS are 1 retry over 30 days."),
        ("Why does Stripe not retry India-issued cards?",
         "Stripe lists India-issued cards among the cases where it does not retry. Recurring charges on those cards fall under the Reserve Bank of India's e-mandate rules, which require a mandate the cardholder has authenticated, a pre-debit notification at least 24 hours before each charge, and fresh authentication above 15,000 INR. A silent background retry cannot satisfy any of that."),
        ("My subscription is unpaid and new invoices are not being charged. Is that a bug?",
         "No. If your end-of-retry setting marks the subscription unpaid, Stripe documents that invoices continue to be generated and stay in a draft state. A draft invoice is never collected. That is the configured behaviour, and the fix is a decision about what you want to happen at the end of dunning rather than a change to the retry schedule."),
    ],
    sources=[SRC_SMART, SRC_INVOICE, SRC_SUBS, SRC_INDIA, SRC_CONNECT_OAUTH],
    related=[
        ("The Visa excessive reattempts rule", "/blog/visa-excessive-reattempts-rule/", "The limit that blocks attempts Stripe did schedule and did send."),
        ("The nine codes that stop retries dead", "/blog/stripe-decline-codes-that-stop-retries/", "Cause 2 in full, and what to do instead of retrying."),
        ("past_due, unpaid, canceled: what each status means", "/blog/stripe-subscription-past-due-vs-unpaid/", "Cause 8, and how to choose your end-of-retry setting."),
    ],
))

# 15 -----------------------------------------------------------------------
# The last section of this page tells the reader not to buy anything, including
# from us, when Stripe's free settings already cover their failure mix. That is
# not a rhetorical flourish and it is not to be softened in a later edit. It is
# the only claim on this site that costs us something to make, which is exactly
# why it is worth more than the rest of the site put together.
ARTICLES.append(dict(
    slug="stripe-free-dunning-settings-checklist",
    title="The free Stripe dunning settings checklist | RecoverFlow",
    h1="The free Stripe settings to switch on before you pay anyone",
    desc="Every free retry and customer email setting in Stripe Billing, with the exact Dashboard path for each, and an honest note on when that is all you need.",
    published="2026-08-16",
    modified="2026-08-16",
    updated="16 August 2026",
    answer="""      <p>Stripe Billing already includes retry scheduling and a set of customer emails, at no extra charge. A surprising number of accounts have some of it switched off.</p>
      <p>This is the list, with the exact Dashboard path for each setting so you can work through it in one sitting.</p>
      <p>It ends with the part no vendor writes down: how to tell when this is genuinely all you need.</p>""",
    sections=[
        ("retries", "1. Turn on and tune Smart Retries", """
    <p>Go to <strong>Billing</strong> &gt; <strong>Revenue recovery</strong> &gt; <strong>Retries</strong>. For one-time invoice retries, the setting is under <strong>Advanced invoicing features</strong> in <strong>Settings</strong> &gt; <strong>Billing</strong> &gt; <strong>Invoices</strong>.</p>
    <p>Smart Retries picks retry times with a model rather than a fixed ladder. You choose the number of retries and the maximum duration, from 1 week, 2 weeks, 3 weeks, 1 month or 2 months. Stripe's recommended default is 8 tries within 2 weeks.</p>
    <p>Leave it at the default unless your own decline mix argues otherwise. Longer windows help when your failures are <code>insufficient_funds</code>, because what you are waiting for is payday. They do not help when the card needs replacing, they only delay the moment you find out. There is <a href="/blog/how-stripe-smart-retries-work/">a longer piece on choosing the window</a>.</p>
    <p>You can also disable Smart Retries and define your own rules, up to three retries each set a number of days after the previous attempt. Do that only if you have a specific reason, not because a fixed schedule feels more controlled.</p>"""),
        ("emails", "2. Turn on the emails Stripe will send for you", """
    <p>Most of these live under <strong>Email notifications and customer management</strong> in your subscriptions and emails settings, and the two revenue recovery ones live on the revenue recovery emails page.</p>
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Setting, exactly as Stripe labels it</th><th scope="col">Where</th><th scope="col">What it does</th></tr></thead>
        <tbody>
          <tr><th scope="row">Send emails when card payments fail</th><td>Revenue recovery settings</td><td>Emails the customer after each failed payment, with a link to update the payment method</td></tr>
          <tr><th scope="row">Send emails about expiring cards</th><td>Revenue recovery settings</td><td>Sends 1 month before a card on file expires, where it is the default payment method or default source</td></tr>
          <tr><th scope="row">Send reminders if a recurring invoice hasn't been paid</th><td>Billing settings, under Manage invoices sent to customers</td><td>Reminders for recurring invoices whose collection method is <code>send_invoice</code></td></tr>
          <tr><th scope="row">Send a Stripe-hosted link for customers to confirm their payments when required</th><td>Email notifications and customer management</td><td>Covers payments that need customer action, such as 3D Secure</td></tr>
          <tr><th scope="row">Send reminders if payment confirmation isn't completed</th><td>Email notifications and customer management</td><td>Keeps reminding until the customer confirms or the payment expires</td></tr>
          <tr><th scope="row">Send emails about upcoming renewals</th><td>Email notifications and customer management</td><td>Timing comes from Prevent failed payments &gt; Upcoming renewal events</td></tr>
          <tr><th scope="row">Send a reminder email 7 days before a free trial ends</th><td>Email notifications and customer management</td><td>Gives trialists a chance to add a working card before the first charge</td></tr>
        </tbody>
      </table>
    </div>
    <p>The first two are the ones that recover money. The rest prevent failures rather than chase them, which is cheaper and less annoying for everybody.</p>"""),
        ("branding", "3. Make them look like you, and give them somewhere to go", """
    <p>All of these emails and hosted pages use your branding settings, so fill those in. An unbranded email asking for card details is indistinguishable from a phishing attempt, and your customers are right to treat it that way.</p>
    <p>Payment confirmation, failed payment, trial ending, renewal and expiring card emails all include a link where the customer can update their payment method. You choose the destination: a Stripe-hosted page, or your own subscription management page. If you pick <strong>Link to a Stripe-hosted page</strong>, Stripe generates a secure private URL where the customer can update the payment method and pay any outstanding invoices.</p>
    <p>Know the expiry rules on that link. It stops working once 30 days have passed since a trial ending email, once the subscription becomes <code>cancelled</code>, <code>incomplete_expired</code> or <code>unpaid</code>, once the trial has ended and a payment method was already provided, or once the renewal period has expired. A dunning sequence that runs longer than the link it points at will send people to a dead page.</p>
    <p>Two more things worth knowing while you are in here. Email logs on the <strong>Customers</strong> page cover the last 60 days only, they update daily, and they exclude the current date. And in a sandbox Stripe does not automatically send customer emails, so to test you need an address on your verified email domain or an active team member's address.</p>"""),
        ("endings", "4. Decide what happens when the retries run out", """
    <p>This is a setting, not a default you have to accept, and most people have never looked at it. When recovery fails, the subscription transitions one of three ways.</p>
    <ul>
      <li><strong>Cancel the subscription.</strong> It changes to <code>canceled</code> after the maximum number of days in your retry schedule.</li>
      <li><strong>Mark the subscription as unpaid.</strong> It changes to <code>unpaid</code>, and invoices continue to be generated and stay in a draft state.</li>
      <li><strong>Leave the subscription past-due.</strong> It stays <code>past_due</code>, invoices continue to be generated, and the customer keeps being charged based on your retry settings.</li>
    </ul>
    <p>Pick deliberately, because the choice decides whether a customer who comes back in three weeks still has an account. <a href="/blog/stripe-subscription-past-due-vs-unpaid/">What each status means</a> goes through the trade-offs.</p>"""),
        ("local", "5. If you take direct debit, this one is off by default", """
    <p>Stripe does not automatically retry failed payments made with local payment methods unless you turn it on, in the <strong>Local payment methods</strong> section for recurring subscription invoices, one-off invoices, or both.</p>
    <div class="table-scroll">
      <table>
        <thead><tr><th scope="col">Method</th><th scope="col">Retryable failure</th><th scope="col">Maximum retries</th><th scope="col">Maximum period</th></tr></thead>
        <tbody>
          <tr><th scope="row">ACH Direct Debit</th><td>Insufficient funds</td><td>2</td><td>40 days</td></tr>
          <tr><th scope="row">ACSS Direct Debit</th><td>Insufficient funds</td><td>1</td><td>30 days</td></tr>
          <tr><th scope="row">Australia BECS Direct Debit</th><td>Insufficient funds</td><td>2</td><td>30 days</td></tr>
          <tr><th scope="row">Bacs Direct Debit</th><td>Insufficient funds</td><td>2</td><td>30 days</td></tr>
          <tr><th scope="row">New Zealand BECS Direct Debit</th><td>Insufficient funds</td><td>1</td><td>30 days</td></tr>
          <tr><th scope="row">SEPA Direct Debit</th><td>Insufficient funds</td><td>2</td><td>30 days</td></tr>
        </tbody>
      </table>
    </div>
    <p>Each has its own mandate requirements, and Stripe notes that enabling this does not make it responsible for losses if a local payment method retry does not happen.</p>"""),
        ("stop", "6. When to stop here and buy nothing", """
    <p>Work through the list above and then look at your actual decline codes. In the Dashboard, filter Payments to failed, or filter Invoices to unpaid and open the latest attempt on each. Twenty is enough to see the shape.</p>
    <p>Sort them into two piles. One pile is failures where the money exists and the timing was wrong: <code>insufficient_funds</code> above all, plus <code>processing_error</code> and <code>try_again_later</code>. The other pile is cards that are gone: lost, stolen, revoked, blocked, or expired with no updated card pushed through the network.</p>
    <p>If the first pile is most of your volume, the honest conclusion follows: retry timing is your lever, Stripe's model is better at retry timing than a rule you would write, the emails above already ask the few customers who need to act, and it is all free.</p>
    <div class="callout">
      <p><strong>Said plainly, because it is the point of this page:</strong> if you have those settings switched on and your failures are mostly <code>insufficient_funds</code>, Stripe has you covered. Do not buy a payment recovery tool. That includes RecoverFlow. Come back if your decline mix changes or if you need attribution, sequence control or history that outlives Stripe's 60 day email log, and until then keep the money.</p>
    </div>"""),
    ],
    faqs=[
        ("Does Stripe charge extra for Smart Retries or dunning emails?",
         "No. Both are part of Stripe Billing rather than separate paid products. Anyone selling you payment recovery should be adding something on top of them, not selling you what your account already does."),
        ("Where is the setting for failed payment emails?",
         "Enable Send emails when card payments fail on the revenue recovery settings page, under Billing then Revenue recovery. The expiring card email, Send emails about expiring cards, lives in the same place and sends 1 month before a card on file expires."),
        ("How long does Stripe keep a record of the emails it sent?",
         "Logs for emails sent in the last 60 days are on the Customers page in the Dashboard. They are updated daily and do not include emails from the current date. If you need a longer history than that, you need to keep it yourself."),
        ("Will Stripe email my customers while I am testing?",
         "In a sandbox Stripe does not automatically send customer emails. To test the configuration, use an address belonging to your verified email domain or to an active team member. Stripe then sends failed payment notifications, upcoming invoice reminders, trial ending reminders and card expiring reminders in the sandbox."),
        ("Should I change the retry window from 2 weeks?",
         "Only if your decline mix says so. A longer window helps when failures are insufficient_funds, because the thing you are waiting for is payday, and two weeks catches one pay cycle while a month catches two. It does not help when the card itself needs replacing, and stretching to two months mainly stretches your reporting lag."),
    ],
    sources=[SRC_EMAILS, SRC_SMART, SRC_SUBS],
    related=[
        ("How Stripe Smart Retries actually work", "/blog/how-stripe-smart-retries-work/", "Choosing the window, and when turning it off is defensible."),
        ("Stripe's dunning emails: what you control", "/blog/stripe-dunning-emails-what-you-control/", "What those settings can and cannot be made to say."),
        ("RecoverFlow vs Stripe's own features", "/compare/stripe-native/", "Where Stripe's free features stop, written by the people selling the alternative."),
    ],
))


# ---------------------------------------------------------------------------
# /blog hub
# ---------------------------------------------------------------------------

def read_page(path):
    """Pull the heading and meta description back out of a page we did not build.

    Half the blog is hand written and has no entry in ARTICLES, so the only place
    its title lives is the page itself.
    """
    with open(path, encoding="utf-8") as f:
        html = f.read()
    h1 = re.search(r"<h1[^>]*>(.*?)</h1>", html, re.S)
    desc = re.search(r'<meta name="description" content="([^"]*)"', html)
    if not h1 or not desc:
        return None
    return plain(h1.group(1)).strip(), desc.group(1).strip()


def discover(section, skip=()):
    """Every built page under docs/<section>/, so the hub cannot silently omit one.

    Listing the hub from ARTICLES alone left 14 of 29 guides reachable only by
    sitemap. They were in llms.txt and in the sitemap, so search engines had them,
    but a person who clicked Blog could not get to half the blog.
    """
    out = []
    for path in sorted(glob.glob(os.path.join(DOCS, section, "*", "index.html"))):
        slug = os.path.basename(os.path.dirname(path))
        if slug in skip:
            continue
        page = read_page(path)
        if page:
            out.append((slug, *page))
    return out


def first_sentence(text):
    """Enough of a meta description to be a card. Some open on a throwaway
    fragment ("Free browser calculator."), so keep taking sentences until the
    line actually says something."""
    taken = []
    for part in text.split(". "):
        taken.append(part.rstrip("."))
        if len(". ".join(taken)) >= 60:
            break
    return ". ".join(taken) + "."


def build_hub():
    def card(slug, heading, desc, meta="Guide"):
        return (f'    <div class="card">\n'
                f'      <p class="meta">{meta}</p>\n'
                f'      <h3><a href="/blog/{slug}/">{heading}</a></h3>\n'
                f"      <p>{desc}</p>\n"
                f"    </div>")

    extras = discover("blog", skip={a["slug"] for a in ARTICLES})
    cards = "\n".join(
        [card(a["slug"], a["h1"], a["desc"]) for a in ARTICLES]
        + [card(slug, heading, desc, "Decline code") for slug, heading, desc in extras]
    )
    tools = discover("tools")
    tool_cards = "\n".join(
        f'      <div class="card"><h3><a href="/tools/{slug}/">{heading}</a></h3>'
        f"<p>{first_sentence(desc)}</p></div>"
        for slug, heading, desc in tools
    )
    body = f"""<main>
  <div class="wrap">
    <p class="eyebrow">Writing</p>
    <h1>Guides to failed payments on Stripe</h1>
    <p>Everything here is checked against Stripe's own documentation before it goes up, and dated so you can tell how stale it is. There are no invented case studies and no recovery rate percentages we cannot stand behind, which means some of these articles end by telling you that Stripe's free features are enough.</p>

    <div class="card-grid">
{cards}
    <div class="card">
      <p class="meta">Pillar</p>
      <h3><a href="/recover-failed-stripe-payments/">How to recover failed Stripe payments</a></h3>
      <p>The overview: what fails, what can be retried, what needs an email, and where the free options run out.</p>
    </div>
    </div>

    <h2 style="margin-top:48px;">Free tools</h2>
    <p>{len(tools)} calculators and lookups that need no signup and no email address.</p>
    <div class="card-grid">
{tool_cards}
    </div>
  </div>
</main>"""
    schema = [{
        "@context": "https://schema.org",
        "@type": "CollectionPage",
        "name": "Guides to failed payments on Stripe",
        "url": f"{SITE}/blog/",
        "description": "Verified guides to Stripe payment failures, decline codes, retries and dunning.",
        "hasPart": [
            {"@type": "Article", "headline": plain(a["h1"]), "url": f"{SITE}/blog/{a['slug']}/", "description": a["desc"]}
            for a in ARTICLES
        ] + [
            {"@type": "Article", "headline": heading, "url": f"{SITE}/blog/{slug}/", "description": desc}
            for slug, heading, desc in extras
        ],
    }]
    return shell("Guides to failed payments on Stripe | RecoverFlow",
                 "Verified, dated guides to Stripe decline codes, Smart Retries, dunning emails and involuntary churn. No invented benchmarks.",
                 f"{SITE}/blog/", body, schema)


# ---------------------------------------------------------------------------
# /about and /contact
# ---------------------------------------------------------------------------

ABOUT = """<main>
  <div class="wrap">
    <p class="eyebrow">About</p>
    <h1>Who builds this</h1>

    <p>RecoverFlow is built and run by one person, Bruce McGinley, from Massachusetts. There is no team, no investors and no support rota. When you email, I answer.</p>

    <h2>Why it exists</h2>
    <p>Subscription businesses lose money to payments that fail for reasons nobody chose: a card expired, a card was reissued, a bank balance was short on the wrong day. Stripe already does a good deal about this for free, and most of the advice online is written by companies with a strong interest in you not knowing that.</p>
    <p>The gap that is actually worth filling is narrower than the marketing suggests. It is the failures where no retry can help, where the customer has to do something, and where what you say to them and when you say it decides whether you keep the subscription. That is what RecoverFlow works on.</p>

    <h2>What stage it is at</h2>
    <p>Early. Genuinely early. The product connects to Stripe, watches for failed subscription payments, decides which are worth retrying, emails the customers whose card needs replacing, and reports what it recovered and why.</p>
    <p>What it does not have is a long list of logos to show you. I am not going to invent one. Everything on this site that looks like a statistic is either arithmetic you can check or a figure from Stripe's own documentation with a link to it.</p>

    <h2>How it makes money</h2>
    <p>25% of what it actually recovers, with a $29 monthly floor and a $299 monthly ceiling, and the floor is waived for the first 30 days. If it recovers nothing beyond the floor, that is the whole bill. If it has a very good month, the bill still stops at $299. There is no contract and access is revoked from your own Stripe dashboard in two clicks, without asking me.</p>
    <p>The <a href="/pricing/">pricing page</a> includes a section explaining when 25% is a bad deal for you and what to do instead, because at high recovery volumes it genuinely is.</p>

    <h2>The rules this site is written under</h2>
    <ul>
      <li>No invented customers, quotes, testimonials or case studies.</li>
      <li>No recovery rate percentages that cannot be traced to a source, and no "industry average" churn benchmarks, because the published ones are not trustworthy.</li>
      <li>Competitor pricing is taken from their own public pages, dated, and marked "quote required" when it is not published rather than guessed at.</li>
      <li>The free tools will sometimes tell you not to buy this. That is deliberate and they are not going to be changed.</li>
      <li>Where Stripe's own free features are enough, the site says so. There is <a href="/compare/stripe-native/">a whole page</a> about it.</li>
    </ul>

    <h2>Getting in touch</h2>
    <p>Email <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a>. If you are evaluating this for a company and need something formal, including a Data Processing Agreement, say so and it will be sorted out properly. More on the <a href="/contact/">contact page</a>.</p>
  </div>
</main>"""

CONTACT = """<main>
  <div class="wrap">
    <p class="eyebrow">Contact</p>
    <h1>Get in touch</h1>

    <p>One address, one person reading it: <a href="mailto:admin@recoverflow.org"><strong>admin@recoverflow.org</strong></a>. There is no ticket queue and no chatbot in the corner of the screen.</p>

    <h2>What to expect</h2>
    <ul>
      <li><strong>Sales and pre purchase questions.</strong> Ask anything, including whether you should bother. If your numbers say Stripe's free features are enough, I will tell you that rather than sell you something.</li>
      <li><strong>Support.</strong> Same address. Include your Stripe account ID or the email you connected with and it will be faster.</li>
      <li><strong>Billing disputes.</strong> Same address, and these get answered first. If you think you were charged for a recovery that was not ours, say which invoice and it gets looked at properly. How attribution is decided is written out in section 4 of the <a href="/terms/">terms</a>.</li>
      <li><strong>Security reports.</strong> Same address, with "security" in the subject. There is no bug bounty programme and I am not going to pretend otherwise, but reports are taken seriously and answered.</li>
      <li><strong>Vendor review and procurement.</strong> Ask for a Data Processing Agreement, a sub-processor list or answers to a security questionnaire. The <a href="/security/">security page</a> already covers most of it, including the certifications RecoverFlow does not hold.</li>
      <li><strong>Press or partnerships.</strong> Same address. RecoverFlow is a one person business, so calibrate accordingly.</li>
    </ul>

    <h2>Response times, honestly</h2>
    <p>Usually within a working day, Massachusetts time. Occasionally longer at weekends. There is no 24/7 on-call rota and the <a href="/security/">security page</a> says so plainly rather than implying otherwise.</p>

    <h2>Cancelling</h2>
    <p>You do not need to email anyone to stop. Go to your Stripe dashboard, find RecoverFlow under connected applications, and revoke access. It takes effect immediately and nothing further is billed except recoveries already attributed before that point. If you want to tell me why you left I would genuinely like to know, but it is not a step in the process.</p>
  </div>
</main>"""


PAGES = [
    ("about", "About RecoverFlow | Who builds it and how it makes money",
     "RecoverFlow is built by one person in Massachusetts. What stage it is at, how the 25% pricing works, and the rules this site is written under.",
     ABOUT),
    ("contact", "Contact RecoverFlow",
     "One address, one person reading it. Sales, support, billing disputes, security reports and vendor review, all at admin@recoverflow.org.",
     CONTACT),
]


def write(path_slug, html):
    out = os.path.join(DOCS, *path_slug.split("/"))
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    print("built", f"{SITE}/{path_slug}/")


if __name__ == "__main__":
    for slug, title, desc, body in PAGES:
        write(slug, shell(title, desc, f"{SITE}/{slug}/", body))

    write("blog", build_hub())

    for a in ARTICLES:
        write("blog/" + a["slug"], build_article(
            a["slug"], a["title"], a["h1"], a["desc"], a["answer"],
            a["sections"], a["faqs"], a["sources"], a["related"],
            updated=a.get("updated"),
            published=a.get("published", "2026-07-28"),
            modified=a.get("modified", "2026-07-28")))

    print(f"\n{len(ARTICLES)} articles, hub, about, contact")
