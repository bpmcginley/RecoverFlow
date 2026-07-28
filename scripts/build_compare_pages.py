#!/usr/bin/env python3
"""
Generates the /compare/* pages for recoverflow.org from verified competitor data.

Every pricing figure below was read from the competitor's own pricing page on
2026-07-27 and the source URL is printed on the rendered page. Where a vendor
does not publish a price, we say so rather than repeating a third-party guess.

Run from the repo root:  python scripts/build_compare_pages.py
"""

import os

DOCS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docs")
VERIFIED_ON = "27 July 2026"
FEE_RATE = 0.25
FLOOR = 29

# --------------------------------------------------------------------------
# Verified competitor data. `flat_monthly` is the cheapest realistic monthly
# entry cost for the recovery capability, used for the crossover arithmetic.
# None means the vendor does not publish a price.
# --------------------------------------------------------------------------
COMPETITORS = [
    {
        "slug": "churnkey",
        "name": "Churnkey",
        "title": "RecoverFlow vs Churnkey",
        "meta": "A verified comparison of Churnkey and RecoverFlow for Stripe failed payment recovery. Churnkey's published pricing, what it does beyond recovery, and which one costs less at your volume.",
        "source_label": "churnkey.co/pricing",
        "source_url": "https://churnkey.co/pricing",
        "flat_monthly": 250,
        "flat_label": "$250/month on Starter, billed yearly",
        "what_it_is": [
            "Churnkey is a broader retention product. Payment recovery is one of three things it does, alongside cancellation deflection flows and churn metrics.",
            "The Starter plan is <strong>$250 per month billed yearly</strong> and is for teams with less than $5,000 per month of churn volume. Core, Intelligence, and Enterprise are custom quotes and require $10,000 or more per month of churn volume.",
            "It is a well built, well funded product with a good reputation. If you want cancel flows as well as recovery, it does more than RecoverFlow does.",
        ],
        "choose_them": [
            "You want cancellation deflection flows, not just payment recovery. RecoverFlow does not do cancel flows at all.",
            "You want A/B testing, segmentation, and customer timelines built into the same tool.",
            "Your churn volume is high enough that a flat plan is cheaper than a percentage, which is most of the time above roughly $1,000 per month of recovered revenue.",
            "You have the budget for a $3,000 annual commitment on the entry plan.",
        ],
        "choose_us": [
            "You only want failed payment recovery and would rather not pay for a retention suite you will not use.",
            "You are early enough that $250 per month before recovering anything is hard to justify.",
            "You want to see what you are actually losing before committing to a year.",
        ],
        "faq": [
            ("How much does Churnkey cost?",
             "Churnkey's published Starter plan is $250 per month billed yearly, for teams with less than $5,000 per month of churn volume. Core, Intelligence, and Enterprise tiers are custom quotes and require contacting their sales team. Those figures are from Churnkey's own pricing page as of July 2026."),
            ("Is Churnkey better than RecoverFlow?",
             "It does more. Churnkey covers cancellation flows and metrics as well as payment recovery, and it is a mature product. RecoverFlow only does recovery, publishes its price, and costs nothing until it recovers something. Which is better depends on whether you want the wider product and whether a flat fee or a percentage is cheaper at your volume."),
            ("Which one is cheaper?",
             "Below roughly $1,000 per month of recovered revenue, RecoverFlow's percentage is cheaper than Churnkey's $250 per month Starter plan. Above that, the flat plan wins. The exact crossover is on this page."),
        ],
    },
    {
        "slug": "baremetrics",
        "name": "Baremetrics Recover",
        "title": "RecoverFlow vs Baremetrics Recover",
        "meta": "A verified comparison of Baremetrics Recover and RecoverFlow. Recover is a $129/month add-on that requires a Baremetrics base plan. Here is what that actually costs and which is cheaper at your volume.",
        "source_label": "baremetrics.com/pricing",
        "source_url": "https://baremetrics.com/pricing",
        "flat_monthly": 204,
        "flat_label": "$129/month add-on, on top of a base plan from $75/month",
        "what_it_is": [
            "Baremetrics is a subscription analytics platform. Recover is its failed payment recovery product, and it is sold as a paid add-on rather than being part of the base product.",
            "Recover costs <strong>$129 per month on top of a Baremetrics plan</strong>. Base plans are $75 per month for $0 to $360K ARR, $255 per month for $360K to $3.6M ARR, and $1,152 per month above that. So the realistic entry cost for recovery alone is about <strong>$204 per month</strong>.",
            "Baremetrics offers a guarantee on the add-on: recover more than your Baremetrics cost or your next month is free. That is a genuinely good offer and worth weighing.",
        ],
        "choose_them": [
            "You want subscription analytics anyway. If you are already paying for Baremetrics, the $129 add-on is the obvious choice and you should probably just take it.",
            "You value their recover-more-than-you-pay guarantee.",
            "You are above roughly $816 per month of recovered revenue, where the flat combined cost is cheaper than a percentage.",
        ],
        "choose_us": [
            "You do not want an analytics suite and only need recovery. Paying $75 per month for a base plan you will not use is pure overhead.",
            "You would rather pay only in months when money actually comes back.",
            "You want to see the size of the problem before you buy anything.",
        ],
        "faq": [
            ("How much does Baremetrics Recover cost?",
             "Recover is a $129 per month add-on and it requires an active Baremetrics plan. Base plans start at $75 per month for companies under $360K ARR, so the practical minimum for recovery is around $204 per month. Those figures are from Baremetrics' own pricing page as of July 2026."),
            ("Is Recover included with Baremetrics?",
             "No. It is explicitly a paid add-on and is not bundled into any base plan."),
            ("Which one is cheaper?",
             "Below roughly $816 per month of recovered revenue, RecoverFlow's percentage costs less than the combined Baremetrics base plan plus Recover add-on. Above that, the flat cost wins. If you already pay for Baremetrics for analytics, the comparison changes and the $129 add-on is likely the better deal."),
        ],
    },
    {
        "slug": "stunning",
        "name": "Stunning",
        "title": "RecoverFlow vs Stunning",
        "meta": "A verified comparison of Stunning and RecoverFlow for Stripe dunning and failed payment recovery. Stunning's MRR-based pricing versus a percentage of what is actually recovered.",
        "source_label": "stunning.co",
        "source_url": "https://stunning.co",
        "flat_monthly": 120,
        "flat_label": "MRR-based, around $120/month at the example tier shown",
        "what_it_is": [
            "Stunning is the closest direct comparison to RecoverFlow. It is a Stripe-focused dunning and failed payment recovery tool, and it has been doing this considerably longer than we have.",
            "Pricing scales with your MRR. The example shown on their own site is <strong>$120 per month</strong>, and they offer a 15 day free trial. Because the price is MRR-banded rather than published as a full table, check their site for the band that applies to you.",
            "It supports Stripe, Foxy, and Subbly. They also sell failed payment consulting separately, starting around $500.",
        ],
        "choose_them": [
            "You want a tool with a long track record. Stunning has been in this category for years and RecoverFlow launched in July 2026.",
            "You bill on Foxy or Subbly rather than Stripe. RecoverFlow is Stripe only.",
            "Your recovery volume is above roughly $480 per month, where a flat monthly fee beats a percentage.",
            "You want the option of paid consulting on your billing setup.",
        ],
        "choose_us": [
            "Your recovery volume is modest and a fixed monthly fee would cost more than the revenue it brings back.",
            "You want to pay only when something is actually recovered, and nothing in the months when nothing fails.",
            "You want to see 90 days of your own history before spending anything.",
        ],
        "faq": [
            ("How much does Stunning cost?",
             "Stunning prices on your MRR. The example figure shown on their own site is $120 per month, with a 15 day free trial. Because the pricing is banded by MRR, check their site for the tier that matches your revenue. Figures verified July 2026."),
            ("What is the difference between Stunning and RecoverFlow?",
             "Both do Stripe dunning and failed payment recovery. The main differences are pricing model and maturity. Stunning charges a monthly fee based on your MRR and has years of track record. RecoverFlow charges a percentage of what it actually recovers, launched in July 2026, and shows you a free 90 day backtest before you commit."),
            ("Which one is cheaper?",
             "Below roughly $480 per month of recovered revenue, RecoverFlow's percentage is cheaper than a $120 per month flat fee. Above it, the flat fee wins."),
        ],
    },
    {
        "slug": "paddle-retain",
        "name": "Paddle Retain",
        "title": "RecoverFlow vs Paddle Retain (formerly ProfitWell Retain)",
        "meta": "A verified comparison of Paddle Retain and RecoverFlow. Retain is free if you bill on Paddle, integrates with Stripe standalone, and does not publish standalone pricing. Here is what that means for you.",
        "source_label": "paddle.com/retain-standalone",
        "source_url": "https://www.paddle.com/retain-standalone",
        "flat_monthly": None,
        "flat_label": "Not published. Free if you bill on Paddle; standalone pricing requires contacting sales",
        "what_it_is": [
            "Paddle Retain is the product formerly known as ProfitWell Retain. It is a full churn reduction suite covering payment recovery, cancellation deflection, term optimization, and reactivation.",
            "It integrates with <strong>Stripe, Chargebee, Zuora, Recurly, and Braintree</strong>, so you do not have to move billing platforms to use it.",
            "If you already bill through Paddle, Retain is <strong>included at no extra cost</strong>. In that situation you should simply use it, and there is no argument for paying us instead.",
            "For everyone else, Paddle does not publish standalone pricing. Third party sites quote figures for it, but since Paddle does not state them we are not going to repeat numbers we cannot verify. You will need to talk to their sales team to find out what it costs.",
        ],
        "choose_them": [
            "You bill on Paddle. Retain is already included and free, so use it.",
            "You want the whole churn suite, including cancellation deflection and reactivation campaigns, not just recovery.",
            "You are large enough that a sales-led procurement process is normal for you and probably gets you a better rate.",
        ],
        "choose_us": [
            "You want to know the price before you talk to anyone. Ours is published on the pricing page and you never have to book a call.",
            "You only need failed payment recovery and do not want to evaluate a suite.",
            "You want a free look at your own last 90 days before any commercial conversation happens.",
        ],
        "faq": [
            ("How much does Paddle Retain cost?",
             "If you bill through Paddle, Retain is included at no extra cost. For standalone use alongside Stripe or another processor, Paddle does not publish pricing and you need to contact their sales team. We are not going to quote a number Paddle itself does not state publicly."),
            ("Does Paddle Retain work with Stripe?",
             "Yes. Paddle's own page states Retain integrates with Stripe, Chargebee, Zuora, Recurly, and Braintree, so you can use it without moving off Stripe."),
            ("Is Retain the same as ProfitWell Retain?",
             "Yes. ProfitWell was acquired by Paddle and Retain is the same product line under the Paddle name."),
        ],
    },
]

CSS = """
  :root {
    --font-display: "Space Grotesk", ui-sans-serif, system-ui, sans-serif;
    --font-body: "Inter", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
    --bg: #ffffff; --bg-alt: #f5f6fc; --bg-elevated: #ffffff;
    --text: #0f1222; --text-dim: #4b5266; --text-mute: #8a92a6;
    --border: #e6e8f0; --border-strong: #d3d7e3;
    --accent: #6366f1; --accent-2: #8b5cf6; --accent-3: #a855f7; --accent-ink: #ffffff;
    --accent-grad: linear-gradient(135deg, #6366f1 0%, #8b5cf6 55%, #a855f7 100%);
    --positive: #0e9f6e; --positive-bg: #e7f7f0; --warn: #b7791f;
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
      --positive: #34d399; --positive-bg: #0f2a20; --warn: #f0c070;
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
    font-family: var(--font-body); font-size: 16px; line-height: 1.65;
    -webkit-font-smoothing: antialiased; overflow-x: hidden;
  }
  img { max-width: 100%; height: auto; }
  a { color: var(--accent); }
  .wrap { max-width: 800px; margin: 0 auto; padding: 0 24px; }
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
  article { padding: 56px 0 40px; }
  .eyebrow { color: var(--accent); font-weight: 600; font-size: 0.9rem; letter-spacing: 0.02em; margin: 0 0 12px; }
  article h1 {
    font-family: var(--font-display); font-weight: 700;
    font-size: clamp(1.9rem, 4.2vw, 2.7rem); line-height: 1.12; letter-spacing: -0.03em; margin: 0 0 16px;
  }
  .byline { color: var(--text-mute); font-size: 0.92rem; margin: 0 0 28px; }
  article h2 {
    font-family: var(--font-display); font-weight: 600;
    font-size: 1.5rem; letter-spacing: -0.02em; margin: 44px 0 14px;
  }
  article h3 { font-family: var(--font-display); font-weight: 600; font-size: 1.12rem; margin: 28px 0 8px; }
  article p { margin: 0 0 18px; color: var(--text); }
  article ul, article ol { margin: 0 0 18px; padding-left: 24px; color: var(--text); }
  article li { margin-bottom: 9px; }
  article strong { font-weight: 600; }
  article em { color: var(--text-dim); }
  .verified {
    background: var(--bg-alt); border: 1px solid var(--border);
    border-left: 3px solid var(--accent); border-radius: 12px;
    padding: 16px 20px; margin: 0 0 32px; font-size: 0.93rem;
  }
  .verified p { margin: 0; color: var(--text-dim); }
  .table-scroll { overflow-x: auto; margin: 0 0 22px; -webkit-overflow-scrolling: touch; }
  table { border-collapse: collapse; width: 100%; min-width: 540px; font-size: 0.94rem; }
  th, td { text-align: left; padding: 12px 14px; border-bottom: 1px solid var(--border); vertical-align: top; }
  thead th {
    font-family: var(--font-display); font-weight: 600; font-size: 0.9rem;
    color: var(--text-dim); border-bottom: 1px solid var(--border-strong);
  }
  tbody th { font-weight: 600; color: var(--text); }
  td.num, th.num { font-variant-numeric: tabular-nums; }
  .callout {
    background: var(--positive-bg); border: 1px solid color-mix(in srgb, var(--positive) 30%, transparent);
    border-radius: 12px; padding: 18px 20px; margin: 0 0 24px;
  }
  .callout p { margin: 0; color: var(--text); }
  .warn-box {
    background: var(--bg-alt); border: 1px solid var(--border);
    border-left: 3px solid var(--warn); border-radius: 12px;
    padding: 18px 20px; margin: 0 0 24px;
  }
  .warn-box p { margin: 0; color: var(--text); }
  .cols { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin: 0 0 12px; }
  .col-card {
    background: var(--card-bg); border: 1px solid var(--border);
    border-radius: 14px; padding: 20px 22px; box-shadow: var(--shadow-sm);
  }
  .col-card h3 { margin: 0 0 10px; font-size: 1.05rem; }
  .col-card ul { padding-left: 20px; margin: 0; font-size: 0.94rem; }
  .faq { margin-top: 12px; }
  .faq h3 { margin-bottom: 6px; }
  .cta-box {
    margin: 48px 0 8px; padding: 28px; border-radius: 16px;
    background: var(--bg-alt); border: 1px solid var(--border); text-align: center;
  }
  .cta-box h2 { margin: 0 0 8px; }
  .cta-box p { color: var(--text-dim); margin: 0 0 18px; }
  .btn {
    display: inline-flex; align-items: center; justify-content: center;
    padding: 13px 26px; border-radius: 11px; font-family: var(--font-body);
    font-weight: 600; font-size: 0.98rem; text-decoration: none;
    background: var(--accent-grad); color: var(--accent-ink);
    box-shadow: 0 8px 22px -8px var(--ring), var(--shadow-sm);
    transition: transform .16s ease, box-shadow .16s ease, filter .16s ease;
  }
  .btn:hover { transform: translateY(-2px); filter: saturate(1.05) brightness(1.03); }
  .cmp-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); gap: 18px; margin: 0 0 20px; }
  .cmp-card {
    background: var(--card-bg); border: 1px solid var(--border); border-radius: 14px;
    padding: 22px; box-shadow: var(--shadow-sm); text-decoration: none; color: var(--text);
    transition: transform .16s ease, box-shadow .16s ease, border-color .16s ease;
    display: block;
  }
  .cmp-card:hover { transform: translateY(-3px); box-shadow: var(--shadow-lg); border-color: var(--border-strong); }
  .cmp-card h3 { margin: 0 0 6px; font-size: 1.08rem; }
  .cmp-card p { margin: 0; color: var(--text-dim); font-size: 0.92rem; }
  footer {
    border-top: 1px solid var(--border); padding: 36px 0; text-align: center;
    color: var(--text-mute); font-size: 0.88rem;
  }
  footer a { color: var(--text-dim); text-decoration: none; }
  footer a:hover { color: var(--accent); }
  .footer-trust { margin-bottom: 10px; font-size: 0.85rem; color: var(--text-dim); }
  @media (max-width: 700px) { .cols { grid-template-columns: 1fr; } }
  @media (max-width: 640px) {
    .wrap { padding: 0 18px; }
    nav a:not(.nav-cta) { display: none; }
  }
"""

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

HEADER = """<header>
  <div class="wrap">
    <a class="logo" href="/"><img src="/assets/logo-mark.png" alt="">Recover<span>Flow</span></a>
    <nav>
      <a href="/#how-it-works">How it works</a>
      <a href="/pricing/">Pricing</a>
      <a href="/compare/">Compare</a>
      <a href="/tools/recovery-estimator/">Calculator</a>
      <a href="/tools/decline-code-lookup/">Decline codes</a>
      <a href="/#faq">FAQ</a>
      <a class="nav-cta" href="https://app.recoverflow.org/app/">Sign in</a>
    </nav>
  </div>
</header>"""

FOOTER = """<footer>
  <div class="wrap">
    <div class="footer-trust">Built on Stripe Connect &middot; Access you control, revoke anytime from your Stripe dashboard &middot; No contracts, no lock-in</div>
    <div class="footer-trust"><a href="/compare/">All comparisons</a> &middot; <a href="/pricing/">Pricing</a> &middot; <a href="/tools/recovery-estimator/">Estimator</a> &middot; <a href="/tools/decline-code-lookup/">Decline codes</a> &middot; <a href="/recover-failed-stripe-payments/">Recovery guide</a></div>
    RecoverFlow. Subscription payment recovery for Stripe. <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a>
  </div>
</footer>"""


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def faq_schema(pairs):
    items = ",\n".join(
        '        {\n'
        '          "@type": "Question",\n'
        f'          "name": "{esc(q)}",\n'
        f'          "acceptedAnswer": {{ "@type": "Answer", "text": "{esc(a)}" }}\n'
        '        }'
        for q, a in pairs
    )
    return '    {\n      "@type": "FAQPage",\n      "mainEntity": [\n' + items + "\n      ]\n    }"


def shell(title, meta_desc, canonical, og_type, schema_blocks, body):
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
{TRACKING}
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{esc(title)}</title>
<meta name="description" content="{esc(meta_desc)}">
<link rel="canonical" href="{canonical}">
<link rel="icon" type="image/png" href="/assets/logo-mark.png">
<meta property="og:type" content="{og_type}">
<meta property="og:site_name" content="RecoverFlow">
<meta property="og:title" content="{esc(title)}">
<meta property="og:description" content="{esc(meta_desc)}">
<meta property="og:url" content="{canonical}">
<meta property="og:image" content="https://recoverflow.org/assets/og-image.png">
<meta name="twitter:card" content="summary_large_image">
<script type="application/ld+json">
{{
  "@context": "https://schema.org",
  "@graph": [
{schema_blocks}
  ]
}}
</script>
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


def crossover_table(c):
    """Monthly cost of each option across recovery volumes."""
    rows = []
    for rec in (0, 250, 500, 1000, 2500):
        ours = max(FLOOR, round(rec * FEE_RATE))
        if c["flat_monthly"] is None:
            theirs = "Not published"
            winner = "Cannot compare without their price"
        else:
            theirs = f"${c['flat_monthly']:,}"
            winner = "RecoverFlow" if ours < c["flat_monthly"] else (
                c["name"] if c["flat_monthly"] < ours else "Level")
        rows.append(
            f'          <tr>\n'
            f'            <th scope="row" class="num">${rec:,}</th>\n'
            f'            <td class="num">${ours:,}</td>\n'
            f'            <td class="num">{theirs}</td>\n'
            f'            <td>{winner}</td>\n'
            f'          </tr>'
        )
    return "\n".join(rows)


def build_competitor(c):
    canonical = f"https://recoverflow.org/compare/{c['slug']}/"
    article_schema = (
        '    {\n'
        '      "@type": "Article",\n'
        f'      "headline": "{esc(c["title"])}",\n'
        f'      "description": "{esc(c["meta"])}",\n'
        '      "author": { "@type": "Person", "name": "Bruce McGinley" },\n'
        '      "publisher": {\n'
        '        "@type": "Organization",\n'
        '        "name": "RecoverFlow",\n'
        '        "logo": { "@type": "ImageObject", "url": "https://recoverflow.org/assets/logo-mark.png" }\n'
        '      },\n'
        '      "datePublished": "2026-07-27",\n'
        '      "dateModified": "2026-07-27",\n'
        f'      "mainEntityOfPage": "{canonical}"\n'
        '    }'
    )
    schema = article_schema + ",\n" + faq_schema(c["faq"])

    what = "\n".join(f"    <p>{p}</p>" for p in c["what_it_is"])
    theirs = "\n".join(f"        <li>{p}</li>" for p in c["choose_them"])
    ours = "\n".join(f"        <li>{p}</li>" for p in c["choose_us"])
    faqs = "\n\n".join(f"      <h3>{esc(q)}</h3>\n      <p>{esc(a)}</p>" for q, a in c["faq"])

    if c["flat_monthly"] is None:
        crossover_note = (
            f'<div class="warn-box"><p><strong>We cannot give you a straight cost comparison here.</strong> '
            f'{c["name"]} does not publish standalone pricing, and we are not going to invent a number or repeat '
            f'a third party guess as if it were fact. Ask them for a quote and compare it against the table above.</p></div>'
        )
    else:
        cross = round(c["flat_monthly"] / FEE_RATE)
        crossover_note = (
            f'<div class="warn-box"><p><strong>The crossover is about ${cross:,} per month of recovered revenue.</strong> '
            f'Below that, our percentage costs you less than {c["name"]}. Above it, their flat fee is the cheaper '
            f'structure and we would be the more expensive choice. That is simply how percentage pricing works, and '
            f'you should do this arithmetic before you buy from either of us.</p></div>'
        )

    body = f"""<article>
  <div class="wrap">
    <p class="eyebrow">Comparison</p>
    <h1>{esc(c['title'])}</h1>
    <p class="byline">By Bruce McGinley, founder of RecoverFlow &middot; Updated July 2026</p>

    <div class="verified">
      <p><strong>Pricing verified {VERIFIED_ON}</strong> from <a href="{c['source_url']}" rel="nofollow noopener" target="_blank">{c['source_label']}</a>.
      Vendors change prices, so check theirs before deciding. If anything here is out of date or unfair, email
      <a href="mailto:admin@recoverflow.org">admin@recoverflow.org</a> and it gets corrected.</p>
    </div>

    <h2>What {esc(c['name'])} is</h2>
{what}

    <h2>What each one costs</h2>
    <div class="table-scroll">
      <table>
        <thead>
          <tr>
            <th scope="col" class="num">Recovered per month</th>
            <th scope="col" class="num">RecoverFlow</th>
            <th scope="col" class="num">{esc(c['name'])}</th>
            <th scope="col">Cheaper</th>
          </tr>
        </thead>
        <tbody>
{crossover_table(c)}
        </tbody>
      </table>
    </div>
    <p><strong>RecoverFlow:</strong> 25% of what it recovers, with a $29 per month minimum after the first 30 days.
    <strong>{esc(c['name'])}:</strong> {c['flat_label']}.</p>

    {crossover_note}

    <h2>Which one to pick</h2>
    <div class="cols">
      <div class="col-card">
        <h3>Choose {esc(c['name'])} if</h3>
        <ul>
{theirs}
        </ul>
      </div>
      <div class="col-card">
        <h3>Choose RecoverFlow if</h3>
        <ul>
{ours}
        </ul>
      </div>
    </div>

    <h2>A note on how we write these</h2>
    <p>Every figure on this page came from the other company's own pricing page, linked at the top, and the date it was
    checked is stated. Where a company does not publish a price, we say that instead of guessing. We are the newest
    product in this category and the fastest way to lose your trust would be to misrepresent the alternatives.</p>
    <p>Worth saying plainly: our pricing is better for smaller recovery volumes and worse for large ones. If your
    numbers are big, one of the flat fee tools will cost you less and you should take it.</p>

    <h2 id="faq">Frequently asked questions</h2>
    <div class="faq">
{faqs}
    </div>

    <div class="cta-box">
      <h2>Start by finding out what you are actually losing</h2>
      <p>Connect your Stripe account and RecoverFlow scans the last 90 days and shows you what failed and what was
      recoverable. It costs nothing, needs no card, and you can disconnect in one click.</p>
      <a class="btn" href="/">Run the free 90 day scan</a>
    </div>
  </div>
</article>"""

    out = os.path.join(DOCS, "compare", c["slug"])
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(shell(c["title"], c["meta"], canonical, "article", schema, body))
    return canonical


def build_index():
    cards = [
        '      <a class="cmp-card" href="/compare/stripe-native/">\n'
        '        <h3>vs Stripe built-in</h3>\n'
        '        <p>What Smart Retries and Stripe\'s failed payment emails already do for free, and where they stop.</p>\n'
        '      </a>'
    ]
    for c in COMPETITORS:
        price = c["flat_label"] if c["flat_monthly"] else "Pricing not published"
        cards.append(
            f'      <a class="cmp-card" href="/compare/{c["slug"]}/">\n'
            f'        <h3>vs {esc(c["name"])}</h3>\n'
            f'        <p>{esc(price)}.</p>\n'
            f'      </a>'
        )

    faq = [
        ("Which failed payment recovery tool is cheapest?",
         "It depends entirely on how much you recover each month. Flat fee tools such as Stunning at around $120 per month, Baremetrics Recover at $129 per month on top of a base plan, and Churnkey at $250 per month on Starter become cheaper than a percentage once your recovered volume is high enough. RecoverFlow charges 25% of what it recovers with a $29 per month minimum, which is cheaper at lower volumes. Each comparison page shows the exact crossover point."),
        ("Do I need a recovery tool at all if I use Stripe?",
         "Not necessarily. Stripe's own Smart Retries are machine learning timed and Stripe can send failed payment and card expiry emails using your branding, all at no cost. Turn those on first. A paid tool is only worth it if you want control over the dunning sequence and sender, attribution of which recovery came from which action, or history beyond Stripe's 60 day email log retention."),
        ("How were these prices verified?",
         "Each price was read from the vendor's own public pricing page on 27 July 2026 and the source is linked at the top of every comparison page. Where a vendor does not publish standalone pricing, such as Paddle Retain and Gravy, that is stated rather than filled in with a third party estimate."),
    ]
    schema = faq_schema(faq)

    body = f"""<article>
  <div class="wrap">
    <p class="eyebrow">Comparisons</p>
    <h1>RecoverFlow compared with the alternatives</h1>
    <p class="byline">Prices verified {VERIFIED_ON} from each vendor's own pricing page</p>

    <div class="verified">
      <p>These pages are written to be useful rather than flattering. Several of these tools will be a better fit
      than RecoverFlow depending on your volume and what else you need, and each page says so directly.</p>
    </div>

    <div class="cmp-grid">
{chr(10).join(cards)}
    </div>

    <h2>The short version</h2>
    <p>Failed payment recovery tools price one of two ways. Most charge a <strong>flat monthly fee</strong>, often
    banded by your MRR or ARR. RecoverFlow charges a <strong>percentage of what it actually recovers</strong>, with a
    small monthly minimum.</p>
    <p>Neither is better in the abstract. A percentage is cheaper when your recovery volume is modest, because you are
    not paying a subscription in the months when little failed. A flat fee is cheaper once volume is high, because the
    percentage keeps growing and the fee does not. Every comparison page below shows the exact figure where that
    crossover happens.</p>

    <div class="callout">
      <p><strong>Before any of this, check Stripe's own settings.</strong> Stripe includes machine learning timed
      retries and failed payment emails at no cost. If those are switched off, switch them on and see where that gets
      you before paying anyone. The <a href="/compare/stripe-native/">Stripe comparison page</a> covers exactly what
      you already have.</p>
    </div>

    <h2>Ones we have not written up</h2>
    <p><strong>Gravy</strong> is a human powered recovery service rather than pure software, and its own site describes
    a customised flat fee arranged through a sales conversation. Third party sites quote percentage figures that
    contradict that. Since we cannot verify a number from Gravy directly, we have not published a comparison page
    rather than publish something we would have to guess at. If you are evaluating them, ask for the quote and compare
    it against the tables here.</p>

    <h2 id="faq">Frequently asked questions</h2>
    <div class="faq">
{chr(10).join(f'      <h3>{esc(q)}</h3>{chr(10)}      <p>{esc(a)}</p>{chr(10)}' for q, a in faq)}
    </div>

    <div class="cta-box">
      <h2>Or skip the comparison and get your own number</h2>
      <p>The 90 day scan reads your recent Stripe invoices and tells you what you actually lost. Free, no card, one
      click to disconnect.</p>
      <a class="btn" href="/">Run the free 90 day scan</a>
    </div>
  </div>
</article>"""

    out = os.path.join(DOCS, "compare")
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(shell(
            "Failed Payment Recovery Tools Compared (Verified Pricing)",
            "Verified pricing comparison of RecoverFlow against Churnkey, Baremetrics Recover, Stunning, Paddle Retain, and Stripe's built-in recovery. Every figure read from the vendor's own pricing page.",
            "https://recoverflow.org/compare/",
            "website", schema, body))
    return "https://recoverflow.org/compare/"


if __name__ == "__main__":
    urls = [build_index()]
    for comp in COMPETITORS:
        urls.append(build_competitor(comp))
    for u in urls:
        print("built", u)
