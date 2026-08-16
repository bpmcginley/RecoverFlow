#!/usr/bin/env python3
"""
Generates /privacy, /terms and /security for recoverflow.org.

Everything stated in these pages was checked against the actual codebase on
2026-07-28: the Domain entities for what is stored, Program.cs and the
Infrastructure project for which third parties are called, and the deployment
config for where data lives. Nothing here describes a control RecoverFlow does
not actually have.

Deliberately absent: any SOC 2, ISO 27001 or penetration-test claim, because
none exist yet. The security page says so explicitly.

Run from the repo root:  python scripts/build_legal_pages.py
"""

import os

DOCS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docs")
UPDATED = "4 August 2026"
# The fee clause changed after that, so the terms carry their own revision date
# rather than borrowing the privacy and security one.
TERMS_UPDATED = "16 August 2026"

TRACKING = """<!-- Google tag (gtag.js) -->
<script async src="https://www.googletagmanager.com/gtag/js?id=G-XNWPM9VELB"></script>
<script>
  window.dataLayer = window.dataLayer || [];
  function gtag(){dataLayer.push(arguments);}
  gtag('js', new Date());
  gtag('config', 'G-XNWPM9VELB');
</script>
<!-- LinkedIn Insight Tag -->
<script type="text/javascript">
_linkedin_partner_id = "9673204";
window._linkedin_data_partner_ids = window._linkedin_data_partner_ids || [];
window._linkedin_data_partner_ids.push(_linkedin_partner_id);
</script><script type="text/javascript">
(function(l) {
if (!l){window.lintrk = function(a,b){window.lintrk.q.push([a,b])};
window.lintrk.q=[]}
var s = document.getElementsByTagName("script")[0];
var b = document.createElement("script");
b.type = "text/javascript";b.async = true;
b.src = "https://snap.licdn.com/li.lms-analytics/insight.min.js";
s.parentNode.insertBefore(b, s);})(window.lintrk);
</script>
<noscript>
<img height="1" width="1" style="display:none;" alt="" src="https://px.ads.linkedin.com/collect/?pid=9673204&fmt=gif" />
</noscript>"""

CSS = """
  :root {
    --font-display: "Space Grotesk", ui-sans-serif, system-ui, sans-serif;
    --font-body: "Inter", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    --font-mono: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, monospace;
    --bg: #ffffff; --bg-alt: #f5f6fc; --bg-elevated: #ffffff;
    --text: #0f1222; --text-dim: #4b5266; --text-mute: #8a92a6;
    --border: #e6e8f0; --border-strong: #d3d7e3;
    --accent: #6366f1; --accent-2: #8b5cf6; --accent-3: #a855f7; --accent-ink: #ffffff;
    --accent-grad: linear-gradient(135deg, #6366f1 0%, #8b5cf6 55%, #a855f7 100%);
    --positive: #0e9f6e; --positive-bg: #e7f7f0; --warn: #b7791f; --danger: #d64545;
    --card-bg: #ffffff; --radius: 14px;
    --shadow-sm: 0 1px 3px rgba(17,20,45,.06), 0 1px 2px rgba(17,20,45,.04);
    --shadow-lg: 0 24px 60px -24px rgba(60,50,160,.35);
    --ring: rgba(99,102,241,.35);
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg: #0a0b11; --bg-alt: #111320; --bg-elevated: #151827;
      --text: #eef0f7; --text-dim: #a3abbf; --text-mute: #6f778c;
      --border: #232637; --border-strong: #2f3346;
      --accent: #818cf8; --accent-2: #a78bfa; --accent-3: #c084fc; --accent-ink: #ffffff;
      --accent-grad: linear-gradient(135deg, #818cf8 0%, #a78bfa 55%, #c084fc 100%);
      --positive: #34d399; --positive-bg: #0f2a20; --warn: #f0c070; --danger: #f87171;
      --card-bg: #131623;
      --shadow-sm: 0 1px 3px rgba(0,0,0,.4);
      --shadow-lg: 0 24px 60px -24px rgba(0,0,0,.6);
      --ring: rgba(129,140,248,.4);
    }
  }
  * { box-sizing: border-box; }
  html { -webkit-text-size-adjust: 100%; scroll-behavior: smooth; }
  html, body {
    margin: 0; padding: 0; background: var(--bg); color: var(--text);
    font-family: var(--font-body); font-size: 16px; line-height: 1.68;
    -webkit-font-smoothing: antialiased; overflow-x: hidden;
  }
  img { max-width: 100%; height: auto; }
  a { color: var(--accent); }
  .wrap { max-width: 780px; margin: 0 auto; padding: 0 24px; }
  :focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: 6px; }
  header {
    padding: 18px 0; border-bottom: 1px solid var(--border);
    background: color-mix(in srgb, var(--bg) 88%, transparent);
    -webkit-backdrop-filter: saturate(180%) blur(12px); backdrop-filter: saturate(180%) blur(12px);
    position: sticky; top: 0; z-index: 50;
  }
  header .wrap { max-width: 1080px; display: flex; align-items: center; justify-content: space-between; gap: 16px; }
  .logo { display: flex; align-items: center; gap: 10px; font-family: var(--font-display); font-weight: 700; font-size: 1.2rem; letter-spacing: -0.02em; text-decoration: none; color: var(--text); }
  .logo span { background: var(--accent-grad); -webkit-background-clip: text; background-clip: text; color: transparent; }
  .logo img { height: 30px; width: 30px; border-radius: 8px; box-shadow: var(--shadow-sm); }
  nav a { color: var(--text-dim); text-decoration: none; margin-left: 22px; font-size: 0.95rem; font-weight: 500; }
  nav a:hover { color: var(--text); }
  nav a.nav-cta { border: 1px solid var(--border-strong); border-radius: 10px; padding: 8px 16px; color: var(--text); font-weight: 600; }
  nav a.nav-cta:hover { border-color: var(--accent); color: var(--accent); }
  main { padding: 52px 0 40px; }
  .eyebrow { color: var(--accent); font-weight: 600; font-size: 0.9rem; letter-spacing: 0.02em; margin: 0 0 12px; }
  h1 { font-family: var(--font-display); font-weight: 700; font-size: clamp(1.9rem, 4.4vw, 2.6rem); line-height: 1.14; letter-spacing: -0.03em; margin: 0 0 12px; }
  .updated { color: var(--text-mute); font-size: 0.9rem; margin: 0 0 30px; }
  h2 { font-family: var(--font-display); font-weight: 600; font-size: 1.38rem; letter-spacing: -0.02em; margin: 44px 0 12px; }
  h3 { font-family: var(--font-display); font-weight: 600; font-size: 1.06rem; margin: 26px 0 8px; }
  p { margin: 0 0 16px; }
  ul, ol { margin: 0 0 16px; padding-left: 24px; }
  li { margin-bottom: 8px; }
  code { font-family: var(--font-mono); font-size: 0.88em; background: var(--bg-alt); border: 1px solid var(--border); border-radius: 5px; padding: 1px 6px; }
  .lawyer {
    background: var(--bg-alt); border: 1px solid var(--border); border-left: 3px solid var(--warn);
    border-radius: 12px; padding: 18px 20px; margin: 0 0 30px;
  }
  .lawyer p { margin: 0; font-size: 0.95rem; }
  .lawyer p + p { margin-top: 10px; }
  .callout { background: var(--positive-bg); border: 1px solid color-mix(in srgb, var(--positive) 30%, transparent); border-radius: 12px; padding: 18px 20px; margin: 0 0 22px; }
  .callout p { margin: 0; } .callout p + p { margin-top: 10px; }
  .table-scroll { overflow-x: auto; margin: 0 0 20px; -webkit-overflow-scrolling: touch; }
  table { border-collapse: collapse; width: 100%; min-width: 520px; font-size: 0.92rem; }
  th, td { text-align: left; padding: 11px 13px; border-bottom: 1px solid var(--border); vertical-align: top; }
  thead th { font-family: var(--font-display); font-weight: 600; font-size: 0.87rem; color: var(--text-dim); border-bottom: 1px solid var(--border-strong); }
  tbody th { font-weight: 600; }
  .toc { background: var(--card-bg); border: 1px solid var(--border); border-radius: 12px; padding: 18px 22px; margin: 0 0 34px; }
  .toc p { margin: 0 0 8px; font-weight: 600; font-size: 0.92rem; }
  .toc ol { margin: 0; padding-left: 20px; font-size: 0.94rem; }
  .toc li { margin-bottom: 5px; }
  footer { border-top: 1px solid var(--border); padding: 36px 0; text-align: center; color: var(--text-mute); font-size: 0.88rem; }
  footer a { color: var(--text-dim); text-decoration: none; }
  footer a:hover { color: var(--accent); }
  .footer-trust { margin-bottom: 10px; font-size: 0.85rem; color: var(--text-dim); }
  @media (max-width: 640px) {
    .wrap { padding: 0 18px; }
    nav a:not(.nav-cta) { display: none; }
  }
"""

HEADER = """<header>
  <div class="wrap">
    <a class="logo" href="/"><img src="/assets/logo-mark.png" alt="">Recover<span>Flow</span></a>
    <nav>
      <a href="/pricing/">Pricing</a>
      <a href="/compare/">Compare</a>
      <a href="/tools/">Tools</a>
      <a href="/audit/">Free audit</a> &middot; <a href="/blog/">Guides</a>
      <a href="/docs/">Docs</a>
      <a class="nav-cta" href="https://app.recoverflow.org/app/">Sign in</a>
    </nav>
  </div>
</header>"""

FOOTER = """<footer>
  <div class="wrap">
    <div class="footer-trust">Built on Stripe Connect &middot; Access you control, revoke anytime from your Stripe dashboard &middot; No contracts, no lock-in</div>
    <div class="footer-trust"><a href="/audit/">Free audit</a> &middot; <a href="/blog/">Guides</a> &middot; <a href="/docs/">Docs</a> &middot; <a href="/tools/">Free tools</a> &middot; <a href="/compare/">Comparisons</a> &middot; <a href="/pricing/">Pricing</a> &middot; <a href="/changelog/">Changelog</a> &middot; <a href="/about/">About</a> &middot; <a href="/contact/">Contact</a></div>
    <div class="footer-trust"><a href="/security/">Security</a> &middot; <a href="/privacy/">Privacy</a> &middot; <a href="/terms/">Terms</a></div>
    RecoverFlow. Subscription payment recovery for Stripe. <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a>
  </div>
</footer>"""

LAWYER_NOTE = """<div class="lawyer">
  <p><strong>Plain English, not a substitute for legal advice.</strong> RecoverFlow is run by one person and these documents were written to describe accurately what the software actually does, rather than copied from a generator. They have not been reviewed by a solicitor.</p>
  <p>If you need something more formal for a procurement or vendor-review process, including a signed Data Processing Agreement, email <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a> and it will be sorted out properly.</p>
</div>"""

SUBPROCESSORS = """<div class="table-scroll">
  <table>
    <thead><tr><th scope="col">Sub-processor</th><th scope="col">What it does</th><th scope="col">Data it touches</th></tr></thead>
    <tbody>
      <tr><th scope="row">Stripe</th><td>Payment processing, account connection, and all card handling</td><td>Payment and subscription data, cardholder data (held entirely by Stripe)</td></tr>
      <tr><th scope="row">Render</th><td>Application hosting and the PostgreSQL database, United States region</td><td>All application data at rest</td></tr>
      <tr><th scope="row">SendGrid (Twilio)</th><td>Delivery of recovery emails to your customers</td><td>Customer email address and message content</td></tr>
      <tr><th scope="row">Cloudflare</th><td>DNS and proxying for the application subdomains</td><td>Request metadata in transit</td></tr>
      <tr><th scope="row">GitHub Pages</th><td>Hosting of the public marketing site only</td><td>No account or customer data</td></tr>
      <tr><th scope="row">Google Analytics</th><td>Marketing site traffic measurement only</td><td>Marketing site visitors, not application data</td></tr>
      <tr><th scope="row">LinkedIn</th><td>Advertising measurement on the marketing site only</td><td>Marketing site visitors, not application data</td></tr>
    </tbody>
  </table>
</div>"""


def shell(title, desc, canonical, body):
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
<meta property="og:type" content="website">
<meta property="og:site_name" content="RecoverFlow">
<meta property="og:title" content="{title}">
<meta property="og:description" content="{desc}">
<meta property="og:url" content="{canonical}">
<meta property="og:image" content="https://recoverflow.org/assets/og-image.png">
<meta name="twitter:card" content="summary_large_image">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&family=Space+Grotesk:wght@500;600;700&display=swap" rel="stylesheet">
<style>{CSS}</style>
</head>
<body>

{HEADER}

{body}

{FOOTER}

</body>
</html>
"""


PRIVACY = f"""<main>
  <div class="wrap">
    <p class="eyebrow">Legal</p>
    <h1>Privacy policy</h1>
    <p class="updated">Last updated {UPDATED}</p>

    {LAWYER_NOTE}

    <div class="toc">
      <p>On this page</p>
      <ol>
        <li><a href="#roles">The two different roles we play</a></li>
        <li><a href="#collect">What we actually store</a></li>
        <li><a href="#audit">The Marketplace audit, which stores nothing</a></li>
        <li><a href="#never">What we never store</a></li>
        <li><a href="#emails">Email tracking, stated plainly</a></li>
        <li><a href="#subprocessors">Sub-processors</a></li>
        <li><a href="#retention">How long we keep things</a></li>
        <li><a href="#rights">Your rights, and your customers' rights</a></li>
        <li><a href="#transfers">Where data lives</a></li>
        <li><a href="#site">The marketing site</a></li>
        <li><a href="#changes">Changes and contact</a></li>
      </ol>
    </div>

    <h2 id="roles">1. The two different roles we play</h2>
    <p>This matters more than anything else in this document, and most privacy policies in this category blur it.</p>
    <p><strong>For your own account data, we are a controller.</strong> Your email address, your company name and your billing records are ours to look after and we decide how they are used.</p>
    <p><strong>For your customers' data, we are a processor.</strong> When a subscription payment fails, we receive the email address of the person whose payment failed so we can send them a message asking them to update their card. That person is your customer, not ours. We act on your instructions, we do not market to them, we do not sell that data, and we do not use it for anything except recovering that specific payment on your behalf.</p>
    <p>If you are subject to the UK GDPR or the EU GDPR, that makes you the controller and RecoverFlow the processor for that data, and you are entitled to a Data Processing Agreement. Email us and you will get one.</p>

    <h2 id="collect">2. What we actually store</h2>
    <p>This list is taken from the database schema rather than written from memory. It describes a full RecoverFlow account. If you only ran the free Marketplace audit, almost none of it applies to you: see <a href="#audit">section 3</a>.</p>
    <h3>About you, the merchant</h3>
    <ul>
      <li>Your email address and company name</li>
      <li>Your Stripe account identifier</li>
      <li>An <strong>encrypted</strong> Stripe access token, which is what lets us read your invoices and retry failed payments</li>
      <li>Your plan, your account settings, and the date you signed up</li>
      <li>Billing records for fees we have charged you, and a Stripe customer identifier for that billing</li>
    </ul>
    <h3>About your failed payments</h3>
    <ul>
      <li>Stripe invoice, subscription and customer identifiers</li>
      <li><strong>The email address of the customer whose payment failed</strong></li>
      <li>The amount and currency</li>
      <li>The decline code and decline reason returned by the card issuer</li>
      <li>When it first failed, when it recovered, or when it was written off</li>
      <li>A record of each retry we attempted and its outcome</li>
      <li>A record of each recovery email we sent, including which step in the sequence it was</li>
      <li>Short-lived tokens for the card update page we send customers to</li>
    </ul>
    <h3>About your history</h3>
    <ul>
      <li>The results of the 90 day backward-looking scan we run when you first connect, which is a summary of what failed and what was estimated recoverable</li>
    </ul>

    <h2 id="audit">3. The Marketplace audit, which stores nothing</h2>
    <p>The <strong>Retry Waste Audit</strong> on the Stripe Marketplace is a separate thing from the product described above, and it has a much shorter privacy story. Everything in section 2 describes what happens when you sign up for RecoverFlow. Almost none of it applies if you only ran the audit.</p>
    <div class="callout">
      <p><strong>The audit requests read access only, and we do not keep your Stripe access token.</strong> The authorisation code is exchanged solely to learn <em>which</em> Stripe account authorised the audit. Every read after that uses our own platform key with your account identifier in a request header, which means the access token is never written to our database at all.</p>
      <p><strong>Running the audit does not create an account.</strong> No merchant record, no password, no login. An audit is not a signup.</p>
    </div>
    <h3>What the audit reads</h3>
    <ul>
      <li>Your failed charges in the scan window: amount, currency, the decline code returned by the card issuer, and when each one was attempted</li>
      <li>Stripe's <strong>card fingerprint</strong>, an opaque identifier Stripe uses to recognise the same card across payments. It is not a card number and cannot be turned back into one. We use it only to count how many attempts hit the same card, which is the whole point of the Visa reattempt section of the report.</li>
    </ul>
    <h3>What the audit keeps</h3>
    <p>Nothing. The figures are calculated in memory, written into an email, and sent to you. The report is not stored, and neither are the charges it was calculated from.</p>
    <p>Your email address is used to deliver that one report, through the same email provider listed in the sub-processors section. It is not added to a mailing list and you will not receive anything else because of it. If you would like a copy of the report re-sent, we cannot do it from our records, because we do not have any: you would need to run the audit again.</p>

    <h2 id="never">4. What we never store</h2>
    <div class="callout">
      <p><strong>We never see or store card numbers, CVCs, expiry dates or bank details.</strong> Stripe holds all of that and processes every charge. Card data does not reach our servers at any point, which is why our card update page is built on Stripe's own hosted elements rather than a form we wrote.</p>
      <p>We also do not store passwords. Signing in uses a one-time link sent to your email address, so there is no password for us to lose.</p>
    </div>

    <h2 id="emails">5. Email tracking, stated plainly</h2>
    <p>The recovery emails we send to your customers record whether the message was <strong>opened</strong> and whether the link was <strong>clicked</strong>. We do this so you can see which messages actually recover money, and so we can attribute a recovery to a specific action rather than guessing.</p>
    <p>We are telling you this because it is a tracking pixel and you should know it exists, particularly if you have your own commitments to your customers about tracking. If you would rather it was switched off for your account, email us and we will turn it off. Attribution gets less precise as a result, which in practice means we charge you for fewer recoveries rather than more.</p>

    <h2 id="subprocessors">6. Sub-processors</h2>
    <p>These are the third parties that touch data in order for the service to work.</p>
    {SUBPROCESSORS}
    <p>Google Analytics and the LinkedIn tag run on the public marketing site only. They are not present in the application you log into, and they never see your account data or your customers' data.</p>

    <h2 id="retention">7. How long we keep things</h2>
    <ul>
      <li><strong>While your account is open:</strong> we keep your account data and your recovery history so the dashboard and reporting work.</li>
      <li><strong>After you disconnect:</strong> your Stripe access token is the thing that matters most, and revoking access in your Stripe dashboard makes it useless immediately regardless of what we hold.</li>
      <li><strong>On request:</strong> ask us to delete your account and we will delete your data. Tell us and it gets done, including your customers' email addresses.</li>
      <li><strong>Billing records:</strong> we keep invoices and fee records for as long as we are required to for tax and accounting purposes, even after deletion of the rest.</li>
    </ul>
    <p>We are being deliberately non-specific about exact retention windows rather than inventing a policy we do not yet operate. If you need a committed retention schedule in writing for a vendor review, ask and we will agree one with you.</p>

    <h2 id="rights">8. Your rights, and your customers' rights</h2>
    <p>Depending on where you are, you may have the right to access the data we hold about you, correct it, delete it, get a copy of it in a portable form, or object to how it is used. Email <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a> and we will action it.</p>
    <p>If one of <em>your</em> customers contacts us directly about their data, we will not act unilaterally. We will point them to you, because you are the controller of that relationship, and then we will do whatever you instruct.</p>
    <p>We do not sell personal information, and we do not share it for cross-context behavioural advertising. If you are a California resident, that means there is nothing for you to opt out of on that front.</p>

    <h2 id="transfers">9. Where data lives</h2>
    <p>The application and its database are hosted in the <strong>United States</strong>. If you or your customers are in the UK, the EU or elsewhere, using RecoverFlow means data is transferred to and processed in the US. If that is a problem for your compliance position, say so before you connect rather than after.</p>

    <h2 id="site">10. The marketing site</h2>
    <p>The pages at recoverflow.org that you can read without logging in use Google Analytics and the LinkedIn Insight Tag to measure traffic and advertising. These set cookies.</p>
    <p>We do not currently show a cookie consent banner. That is a gap rather than a position, and it is on the list to fix. In the meantime, blocking third-party cookies or using an ad blocker prevents both, and nothing on the marketing site breaks if you do.</p>
    <p>The free tools run entirely in your browser. Nothing you type into the estimator, the retry builder or the email generator is transmitted to us or stored anywhere.</p>

    <h2 id="changes">11. Changes and contact</h2>
    <p>If this policy changes in a way that materially affects you, we will email you rather than quietly editing the page. The date at the top always reflects the last change.</p>
    <p>Questions, requests, or complaints: <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a>. It goes to Bruce, who is the whole company, so you will get a real answer rather than a ticket number.</p>
  </div>
</main>"""


TERMS = f"""<main>
  <div class="wrap">
    <p class="eyebrow">Legal</p>
    <h1>Terms of service</h1>
    <p class="updated">Last updated {TERMS_UPDATED}</p>

    {LAWYER_NOTE}

    <div class="toc">
      <p>On this page</p>
      <ol>
        <li><a href="#who">Who this agreement is between</a></li>
        <li><a href="#service">What the service does</a></li>
        <li><a href="#account">Your account and your Stripe connection</a></li>
        <li><a href="#fees">Fees and how attribution works</a></li>
        <li><a href="#billing">How and when you are billed</a></li>
        <li><a href="#yours">Your responsibilities</a></li>
        <li><a href="#ours">What we do and do not promise</a></li>
        <li><a href="#stopping">Stopping</a></li>
        <li><a href="#liability">Liability</a></li>
        <li><a href="#changes">Changes</a></li>
        <li><a href="#law">Governing law and disputes</a></li>
      </ol>
    </div>

    <h2 id="who">1. Who this agreement is between</h2>
    <p>These terms are between you, the business connecting a Stripe account, and RecoverFlow, a one person business operated by Bruce McGinley. By connecting your Stripe account you accept them.</p>

    <h2 id="service">2. What the service does</h2>
    <p>RecoverFlow connects to your Stripe account, watches for failed subscription payments, retries the ones that are worth retrying, and emails your customers asking them to update their card when that is the actual problem. It reports what it recovered and why.</p>
    <p>It runs alongside Stripe's own recovery features rather than replacing them. Stripe continues to process every charge and hold every card.</p>

    <h2 id="account">3. Your account and your Stripe connection</h2>
    <ul>
      <li>You connect through Stripe Connect using OAuth. You grant the access, and you can revoke it at any time from your own Stripe dashboard without asking us.</li>
      <li>You are responsible for keeping access to the email address you sign in with, because sign-in works by one-time link to that address.</li>
      <li>You must be authorised to connect the Stripe account you connect. Do not connect an account that is not yours to connect.</li>
    </ul>

    <h2 id="fees">4. Fees and how attribution works</h2>
    <p><strong>25% of the failed payments RecoverFlow recovers, subject to a minimum of $29 per month that begins after your first 30 days and a maximum of $299 per month.</strong> Nothing is payable up front and no card is required to connect.</p>
    <p>The word "recovers" is doing real work in that sentence, so here is exactly what it means:</p>
    <ul>
      <li>A recovery is billable only when it can be <strong>attributed</strong> to a specific action RecoverFlow took, being either a retry it scheduled or a card update email it sent that the customer acted on.</li>
      <li>Payments that come back on their own, or that Stripe's own retries recover, are recorded as unattributed and <strong>are not billed</strong>.</li>
      <li>Where attribution is ambiguous, the system is built to resolve it in your favour rather than ours.</li>
      <li>Fees are calculated in whole cents and rounded down.</li>
    </ul>
    <p>During your first 30 days the monthly minimum does not apply at all. If nothing is recovered in that period, you owe nothing.</p>
    <p>Fees are currently charged in US dollars. If your Stripe account operates in another currency, contact us before connecting so we can tell you honestly whether the service is ready for you.</p>

    <h2 id="billing">5. How and when you are billed</h2>
    <ul>
      <li>Billing runs monthly. We total the attributed recoveries for the period, apply the 25%, add a top-up if that total is below the $29 minimum and your first 30 days have passed, and reduce it to $299 if it comes out above the maximum.</li>
      <li>You receive a Stripe invoice by email with a hosted payment page. Payment is due within 7 days.</li>
      <li>We do not hold a card on file for you and we do not auto-charge. You pay the invoice.</li>
      <li>Every invoice itemises which recoveries it is charging for, so you can check the arithmetic against your own Stripe data.</li>
    </ul>

    <h2 id="yours">6. Your responsibilities</h2>
    <ul>
      <li><strong>Your customer relationships remain yours.</strong> Emails we send go out on your behalf and about your business. You are responsible for those relationships and for whatever commitments you have made to those customers.</li>
      <li>You are responsible for complying with the laws that apply to your business, including marketing and data protection law in the places your customers are.</li>
      <li>You must not use RecoverFlow to pursue payments you are not genuinely owed, or to contact people who have asked you not to.</li>
      <li>You are responsible for the accuracy of what is in your Stripe account. We act on what Stripe tells us.</li>
    </ul>

    <h2 id="ours">7. What we do and do not promise</h2>
    <p><strong>We do not promise any particular recovery rate.</strong> RecoverFlow launched in July 2026 and does not have recovery figures to publish. Anyone in this category quoting you a guaranteed percentage is guessing, and we would rather say so.</p>
    <p>We do not promise the service will be uninterrupted or error-free. It is early software run by one person. It is built carefully, with the billing path in particular designed so that a crash cannot double-charge you, but it is not an enterprise platform with an SLA and we are not going to pretend otherwise.</p>
    <p>If you need an availability commitment in writing, ask, and we will tell you honestly whether we can meet it.</p>

    <h2 id="stopping">8. Stopping</h2>
    <ul>
      <li><strong>You can stop at any time.</strong> Revoke RecoverFlow's access in your Stripe dashboard. It takes one click, nothing in your billing changes, and your subscriptions carry on exactly as before.</li>
      <li>There is no contract term, no notice period and no exit fee.</li>
      <li>Fees already incurred for recoveries already attributed remain payable.</li>
      <li>We may suspend or end an account that is being used to break the law, to harass people, or in a way that puts our Stripe platform status at risk. If that happens you will get an explanation, not a form letter.</li>
    </ul>

    <h2 id="liability">9. Liability</h2>
    <p>To the extent the law allows, our total liability to you for any claim is limited to the fees you paid us in the 12 months before the claim arose. We are not liable for indirect or consequential losses such as lost profits or lost customers.</p>
    <p>Nothing here limits liability for fraud, or for anything else that cannot lawfully be limited.</p>

    <h2 id="changes">10. Changes</h2>
    <p>If these terms change in a way that materially affects you, particularly anything to do with fees, we will email you before it takes effect. You can always stop by revoking access, and we would rather you did that than felt trapped.</p>
    <p>Questions: <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a>.</p>

    <h2 id="law">11. Governing law and disputes</h2>
    <p>RecoverFlow is established in the Commonwealth of Massachusetts, United States. These terms are governed by the laws of Massachusetts, without regard to its conflict of laws rules. The United Nations Convention on Contracts for the International Sale of Goods does not apply.</p>
    <p><strong>Talk to us first.</strong> If something goes wrong, email <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a> with what happened and what you want done about it. We will reply. Most things that look like disputes are billing questions, and those get sorted in a day. Please give us 30 days to fix it before starting anything formal.</p>
    <p>If that does not work, any claim goes to the state or federal courts located in Massachusetts, and both of us agree those courts have jurisdiction. If you are a business outside the United States, that may be inconvenient for you, which is worth knowing before you connect rather than after.</p>
    <p>Nothing here takes away a right you have under the law of your own country that cannot be waived by agreement. If you are a UK or EU business, your statutory rights are unaffected.</p>
  </div>
</main>"""


SECURITY = f"""<main>
  <div class="wrap">
    <p class="eyebrow">Trust</p>
    <h1>Security</h1>
    <p class="updated">Last updated {UPDATED}</p>

    <p>You are being asked to connect the system that takes your money. That deserves a straight account of how the connection works, what we can and cannot reach, and what protections we do <em>not</em> yet have. This page includes the last part, which most security pages leave out.</p>

    <h2>How the connection works</h2>
    <p>There are two, and they are not equally powerful. Which one you used decides what we can reach.</p>
    <ul>
      <li>RecoverFlow connects through <strong>Stripe Connect OAuth</strong>. You authorise it from Stripe's own screen, not from a form we wrote.</li>
      <li>We never ask for, and never receive, your Stripe API keys.</li>
      <li><strong>The full product</strong> requests read and write access, because retrying an invoice is a write. The access token we receive for it is <strong>encrypted at rest</strong> in our database.</li>
      <li><strong>The free Retry Waste Audit</strong> requests <strong>read access only</strong>, and we keep no token for it at all. See below.</li>
      <li><strong>You can revoke either in one click</strong> from your Stripe dashboard, at any time, without contacting us. That immediately ends our ability to read or act on your account.</li>
    </ul>

    <h2>The audit is read-only, and keeps nothing</h2>
    <div class="callout">
      <p>The <strong>Retry Waste Audit</strong> requests three read permissions and no write permission of any kind. Stripe enforces that on its own side, so it is not a promise you have to take on trust: the permissions screen shows you exactly what was granted.</p>
      <p><strong>We do not store the access token.</strong> The authorisation code is exchanged only to learn which account authorised the audit; the reads themselves go through our platform key with your account identifier in a request header. The token is never written to our database.</p>
      <p><strong>Running the audit does not create an account.</strong> It reads, it calculates, it emails you the report, and it keeps nothing.</p>
    </div>

    <h2>What we never see</h2>
    <div class="callout">
      <p><strong>Card numbers, CVCs, expiry dates and bank details never reach our servers.</strong> Stripe holds all of it and performs every charge.</p>
      <p>When a customer updates their card through a link in a recovery email, the form is built on Stripe's own hosted card elements. The card details go from the customer straight to Stripe. We receive only a reference to the new payment method, never the card itself.</p>
      <p>This is why RecoverFlow falls under the lightest PCI scope, SAQ A. Not because we built clever infrastructure, but because we deliberately never touch the data in the first place.</p>
    </div>
    <p>We also never store passwords. Signing in uses a one-time link to your email address, so there is no password database to breach.</p>

    <h2>How your data is separated from everyone else's</h2>
    <ul>
      <li>Every query against merchant data is <strong>scoped to a single merchant at the database layer</strong>, using query filters applied globally rather than remembered case by case. This is the protection that stops one merchant's data ever appearing in another's dashboard, and it is enforced in one place rather than trusted to every individual query.</li>
      <li>Background jobs that legitimately run across all merchants, such as monthly billing, filter explicitly by merchant and are written to be safe to re-run.</li>
    </ul>

    <h2>Infrastructure</h2>
    <ul>
      <li>Hosted on <strong>Render</strong>, in a United States region, with a managed PostgreSQL database.</li>
      <li>All traffic is over <strong>HTTPS</strong>. The marketing site, the API and the application all redirect plain HTTP to HTTPS.</li>
      <li>Database encryption at rest is provided by the managed database.</li>
      <li>Secrets are held as environment variables, not in the source code. The repository is public and contains no credentials.</li>
      <li>Session and token-signing keys are persisted on a mounted disk so that a redeploy does not silently invalidate your session or your sign-in links.</li>
    </ul>

    <h2>Billing safety</h2>
    <p>The billing path is the place where a bug costs you real money, so it is written defensively. Before any charge is created, the invoice record and the specific recovery cases it covers are written in a single atomic database transaction. If the process crashes at any point after that, the next run resumes that exact invoice rather than billing the same recoveries twice.</p>
    <p>Fees are calculated in integer cents and rounded down, so rounding always favours you.</p>

    <h2>What we do not have</h2>
    <div class="lawyer">
      <p>Being straight about the gaps, because you will find them out eventually anyway and it is better you hear it here.</p>
      <p><strong>No SOC 2.</strong> Not Type I, not Type II. It is a months-long process and RecoverFlow launched in July 2026. If your procurement requires SOC 2, we do not meet your bar today and you should not buy.</p>
      <p><strong>No third-party penetration test</strong> has been carried out yet.</p>
      <p><strong>No ISO 27001</strong>, and no other formal certification.</p>
      <p><strong>No SSO or SCIM</strong>, and no bug bounty programme.</p>
      <p><strong>No 24/7 on-call.</strong> There is one person. Serious problems get attention quickly, but not instantly at 4am.</p>
    </div>
    <p>These are all on the roadmap. None of them are done. When any of them changes, this page changes with it and the date at the top will move.</p>

    <h2>Reporting a vulnerability</h2>
    <p>If you find a security problem, email <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a> with enough detail to reproduce it. It goes straight to Bruce.</p>
    <p>There is no cash bounty, because there is no revenue to fund one yet. What you will get is a fast, non-defensive response, a fix, and public credit if you want it. Please give a reasonable window before disclosing publicly.</p>

    <h2>Related</h2>
    <p>The full list of sub-processors and exactly what data each one touches is in the <a href="/privacy/#subprocessors">privacy policy</a>. What we store, down to the field, is in <a href="/privacy/#collect">section 2</a> of the same document.</p>
  </div>
</main>"""


PAGES = [
    ("privacy", "Privacy Policy | RecoverFlow",
     "How RecoverFlow handles your data and your customers' data: exactly what is stored, what is never stored, sub-processors, retention, and your rights.",
     PRIVACY),
    ("terms", "Terms of Service | RecoverFlow",
     "RecoverFlow's terms: what the service does, how the 25% fee and attribution work, how billing runs, and how to stop at any time with no contract.",
     TERMS),
    ("security", "Security | RecoverFlow",
     "How RecoverFlow connects to Stripe, what it can and cannot see, how merchant data is isolated, and an honest list of the certifications it does not yet hold.",
     SECURITY),
]

if __name__ == "__main__":
    for slug, title, desc, body in PAGES:
        out = os.path.join(DOCS, slug)
        os.makedirs(out, exist_ok=True)
        canonical = f"https://recoverflow.org/{slug}/"
        with open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n") as f:
            f.write(shell(title, desc, canonical, body))
        print("built", canonical)
