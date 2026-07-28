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
{TRACKING}
<meta charset="UTF-8">
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


def article_schema(slug, h1, desc):
    return {
        "@context": "https://schema.org",
        "@type": "Article",
        "headline": plain(h1),
        "description": desc,
        "url": f"{SITE}/blog/{slug}/",
        "datePublished": "2026-07-28",
        "dateModified": "2026-07-28",
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


CTA = """<div class="cta-band">
  <h2>If you would rather not build this yourself</h2>
  <p>RecoverFlow watches your Stripe account for failed subscription payments, stops retrying the ones that cannot succeed, and emails the customers whose card simply needs replacing. It charges 25% of what it actually recovers, with a $29 monthly floor, and nothing at all if it recovers nothing.</p>
  <p style="color:var(--text-dim);">It is early. It is run by one person. If Stripe's own free retry settings are enough for you, use those instead, and there is a page on this site that says exactly when that is the right call.</p>
  <a class="btn" href="/pricing/">See how the pricing works</a>
</div>"""


def build_article(slug, title, h1, desc, answer, sections, faqs, sources, related):
    toc = "\n".join(f'        <li><a href="#{sid}">{head}</a></li>' for sid, head, _ in sections)
    body_sections = "\n\n".join(
        f'    <h2 id="{sid}">{head}</h2>\n{html}' for sid, head, html in sections
    )
    body = f"""<main>
  <article class="wrap">
    <p class="eyebrow">Guide</p>
    <h1>{h1}</h1>
    <p class="updated">Last updated {UPDATED} &middot; written by Bruce McGinley, who builds RecoverFlow</p>

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
    schema = [article_schema(slug, h1, desc)]
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
    </div>"""),
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
      <p><strong>Worth saying plainly:</strong> if you have not yet switched Smart Retries on and configured the window deliberately, do that before you buy anything from anyone, including us. It is free and it is the largest single improvement available to most Stripe accounts.</p>
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


# ---------------------------------------------------------------------------
# /blog hub
# ---------------------------------------------------------------------------

def build_hub():
    cards = "\n".join(
        f"""    <div class="card">
      <p class="meta">Guide</p>
      <h3><a href="/blog/{a['slug']}/">{a['h1']}</a></h3>
      <p>{a['desc']}</p>
    </div>"""
        for a in ARTICLES
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
    <p>Four calculators and lookups that need no signup and no email address.</p>
    <div class="card-grid">
      <div class="card"><h3><a href="/tools/decline-code-lookup/">Decline code lookup</a></h3><p>48 Stripe decline codes, searchable, with the nine that block retries flagged.</p></div>
      <div class="card"><h3><a href="/tools/recovery-estimator/">Recovery estimator</a></h3><p>What failed payments cost you, against real competitor pricing at your scale.</p></div>
      <div class="card"><h3><a href="/tools/retry-schedule-builder/">Retry schedule builder</a></h3><p>Builds a schedule, and tells you when a schedule is the wrong answer.</p></div>
      <div class="card"><h3><a href="/tools/dunning-email-generator/">Dunning email generator</a></h3><p>Reason aware copy, including the reasons you should never name.</p></div>
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
    <p>25% of what it actually recovers, with a $29 monthly floor. If it recovers nothing beyond the floor, that is the whole bill. There is no contract and access is revoked from your own Stripe dashboard in two clicks, without asking me.</p>
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
            a["sections"], a["faqs"], a["sources"], a["related"]))

    print(f"\n{len(ARTICLES)} articles, hub, about, contact")
