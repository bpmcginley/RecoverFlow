# Weekly SEO cycle for recoverflow.org

The scheduler holds a pointer to this file. This file holds the logic. Edit it with a
commit so every run is reproducible against a known version.

Run this from the repo root. Everything below assumes a clean checkout of `main`.

---

## The one thing that breaks this site

`docs/` is what recoverflow.org serves. **37 of its 61 pages are generated, and hand-editing
one of those looks like it worked and is silently overwritten by the next build.** If you
want to change a generated page, change the script that emits it.

| To change | Edit |
| --- | --- |
| A blog post in `ARTICLES`, `/about/`, `/contact/`, `/blog/` | `scripts/build_content_pages.py` |
| A `/compare/` page other than `/compare/stripe-native/` | `scripts/build_compare_pages.py` |
| A `/docs/` page, `/changelog/`, `/audit/`, `/tools/retry-waste-calculator/` | `scripts/build_docs_pages.py` |
| A legal page | `scripts/build_legal_pages.py` |
| Fonts, CSS, page chrome | `scripts/apply_design_system.py` |
| `sitemap.xml` | nothing, it is derived from the filesystem |
| One of the 24 hand-written pages below | the file in `docs/` itself |

Articles live as dicts appended to the module-level `ARTICLES` list in
`build_content_pages.py`. `HARD_CODES` in that file is the single source of truth for the
nine hard decline codes, so read from it rather than retyping a code into prose.

### The 24 pages no builder emits

This file used to say every file in `docs/` was generated. It never was, and a run that
believed it either edited a script that emits nothing or refused to touch a page that was
perfectly safe to edit. These pages are hand-written and **the only way to change one is to
edit the file in `docs/` directly**:

`/`, `/pricing/`, `/recover-failed-stripe-payments/`, `/compare/stripe-native/`, `/tools/`
and its five tool pages other than the retry waste calculator, and fourteen blog posts:
the twelve single decline-code guides (`call_issuer`, `card_velocity_exceeded`,
`currency_not_supported`, `duplicate_transaction`, `fraudulent`, `generic_decline`,
`incorrect_cvc`, `incorrect_number` vs `invalid_number`, `lost_card` and `stolen_card`,
`pickup_card` vs `restricted_card`, `processing_error`, `transaction_not_allowed`,
`try_again_later`), plus `invoice-payment-failed-vs-payment-intent-payment-failed`.

Do not take that list on trust and do not retype it. Membership is measured:

```bash
python3 scripts/audit_page_sources.py
```

That snapshots modified times, runs every builder, and reports which files were rewritten.
It exits non-zero if a page gained or lost a builder, and also if rebuilding changed a
committed file, which means `docs/` and its builder had fallen out of step. It is a CI step,
so the split cannot drift again without a red build.

Editing a hand-written page directly is correct and expected. It is still published prose,
so Step 6 still sends it to a pull request. If you ever port one of these into a builder,
drop it from `HAND_WRITTEN` in that script in the same commit.

### Build order is not optional

The content builders emit inline `<style>` only. `apply_design_system.py` is what injects
`rf.css` and the fonts. Running a content build without it afterwards strips the design
system off every page it touched. That has happened once and reverted 34 of 57 pages.

```bash
python3 scripts/build_content_pages.py
python3 scripts/apply_design_system.py
python3 scripts/build_sitemap.py
python3 scripts/validate_site.py
```

Substitute whichever builder you actually changed on the first line.
`apply_design_system.py` runs after any content rebuild, always. `validate_site.py` must
exit 0.

That validator is also a CI step, so a run that skips it just moves the failure to the pull
request. Run it locally anyway; its error messages are the fastest way to find what a build
broke.

---

## Ground rules

These are not style preferences. Two of them are enforced by `validate_site.py` and the
rest exist because breaking them costs more than any ranking is worth.

1. **No results claims. Ever.** RecoverFlow has zero customers. Nothing on the site may
   state or imply a recovery rate, a customer count, a testimonial, a case study, or an
   "industry average" percentage. `build_content_pages.py` says it in its own docstring:
   there are no invented customers, no invented recovery rates and no invented averages.
   Write mechanism and arithmetic instead. "A `do_not_honor` decline retried on the same
   rail returns the same code" is a mechanism claim and is fine. "Merchants recover 38%"
   is not.
2. **No em dashes in page bodies.** House style, enforced by the validator. Use a comma, a
   semicolon, or two sentences.
3. **Meta descriptions stay at 165 characters or fewer**, or they truncate in results. The
   validator counts them.
4. **Every factual claim about card networks, decline codes or issuer behaviour needs a
   source** in the page's `sources` list. Link the primary document, meaning Stripe docs, a
   network rulebook, or a regulator, and not a blog post about one.
5. **Never touch `growth/`.** `pipeline.csv`, `growth/sequences/` and `growth/sender.txt`
   are gitignored after a real PII leak. Do not read them, stage them, or `git add -f` them.
   This routine has no business in the outbound pipeline.
6. **Never commit a credential.** If a step needs a key, the key lives outside the repo at
   `~/.recoverflow/gsc-key.json`. See "Search Console data" below.

---

## Step 0. Orient

```bash
git status --short
git log --oneline -5
ls seo/log/
```

Read the most recent file in `seo/log/`. It records what the last run changed, what it
deliberately left alone, and anything it asked for. Do not redo work the previous run
already did, and do not re-propose an idea a previous run recorded as rejected.

## Step 1. Health check before anything else

```bash
python3 scripts/validate_site.py
python3 scripts/audit_page_sources.py
```

If either fails on a clean checkout, that is the week's work. Something landed broken. Fix
it and stop there. A broken canonical or a dropped sitemap entry costs more than a new
article gains. Causes, in the order they actually happen:

- a content build ran without `apply_design_system.py` afterwards
- a `related` link points at a slug that was renamed or never built
- an edit broke the JSON-LD and it no longer parses
- someone hand-edited a generated file in `docs/`
- a builder changed and nobody rebuilt, which only `audit_page_sources.py` sees

## Step 2. Read the Search Console data

```bash
ls seo/gsc/
```

The newest `seo/gsc/*.json` file is the input for Step 3. It is written either by
`python3 scripts/fetch_gsc.py fetch` using a service account, or by
`python3 scripts/fetch_gsc.py import-csv <export.csv>` from a manual Performance export.
Both produce the same shape.

**If `seo/gsc/` is empty or the newest file is more than 30 days old, say so once in your
report and continue.** Do not stop, do not guess at numbers, and do not repeat the warning.
Steps 1, 4d and 5 need no Search Console data at all, and they are most of the value.

Where data exists, pull out:

- **Queries at position 5 to 20 with impressions and few clicks.** These are the highest
  return target on the site. The page already ranks; it is losing the click. Usually the
  title or the meta description answers a different question than the query asks.
- **Queries where the site gets impressions but has no page dedicated to the question.**
  These are article candidates.
- **Pages that lost position since the previous file in `seo/gsc/`.** Compare against the
  second-newest file, not against memory.

## Step 3. Decide, and write the decision down

Pick **at most three** changes. A weekly cycle that ships three real improvements beats one
that ships twelve edits nobody reviewed. Prefer, in this order:

1. Fixing something the validator or a lost position says is broken.
2. Rewriting a title or meta description for a page already ranking 5 to 20. The cheapest
   possible win: the page exists, the ranking exists, only the click is missing.
3. Adding internal links from existing articles to an under-linked page. Check the
   `related` lists in `build_content_pages.py` first. The site already has a dense internal
   link graph, and a link added twice is worse than none.
4. Writing one new article, and only if the query evidence supports it and you can source
   every claim in it.

Anything you consider and reject goes in the log with the reason. That is what stops the
next run re-proposing it.

## Step 4. Make the changes

**4a. Titles and meta descriptions.** Edit the `title` and `desc` fields on the article
dict in `build_content_pages.py`. Keep `desc` at 165 characters or fewer. The title should
contain the words the query actually uses, not the words we would prefer it used.

On one of the 24 hand-written pages, edit `docs/<page>/index.html` directly. The same text
is repeated in five places there, and all five have to move together or the page contradicts
itself: `<title>`, `<meta name="description">`, `og:title`, `og:description`, and the
`description` in the JSON-LD block. Check the blog index too, which repeats the description
of every post it lists.

**4b. Internal links.** Add to the `related` list on the source article as a
`(title, url, blurb)` tuple. The blurb is a sentence, not a keyword string.

**4c. A new article.** Append a dict to `ARTICLES`. Keys: `slug`, `title`, `h1`, `desc`,
`answer`, `sections`, `faqs`, `sources`, `related`. `sections` is a list of
`(id, heading, html)` tuples and `faqs` a list of `(question, answer)` pairs. Match the
structure of the neighbouring articles rather than inventing a new one. The `answer` field
is the direct answer to the page's question and is what answer engines lift, so write it to
stand alone.

**4d. Answer-engine surface.** `docs/robots.txt` already allows GPTBot, ClaudeBot,
PerplexityBot and Google-Extended and points them at `/llms.txt`. If you added a page, check
that `docs/llms.txt` still reflects the site. If `llms.txt` is generated, fix the generator;
if it is static, edit it directly.

Then run the build chain from the top of this file.

## Step 5. Verify

```bash
python3 scripts/validate_site.py
```

```bash
python3 scripts/audit_page_sources.py
```

```bash
git diff --stat
```

Read the diff. A one-line change to a `desc` field should not produce a 4,000-line diff. If
it does, a builder rewrote pages it should not have touched, most often because
`apply_design_system.py` ran out of order. Fix that before committing rather than committing
the noise.

Confirm by hand, because the validator does not check these:

- no em dash slipped into new prose
- no sentence claims a result, a rate, a customer or an average
- every new factual claim has a matching entry in `sources`

## Step 6. Commit

**Maintenance fixes commit straight to `main`.** That means validator failures, broken
internal links, sitemap regeneration, meta descriptions trimmed to length, the design system
reapplied. These are corrections back to a known-good state, and reviewing them individually
wastes the reviewer's time.

```bash
git add docs scripts seo && git commit -m "seo: what changed and why" && git push origin main
```

**New or rewritten public prose goes on a branch and opens a pull request.** New articles,
rewritten titles, rewritten answers. This is published writing under Bruce's name on a site
with no customers to correct it, and a person should read it before it ships.

```bash
git checkout -b seo/2026-01-01-short-slug
```

```bash
git add docs scripts seo && git commit -m "seo: what changed and why" && git push -u origin HEAD
```

```bash
gh pr create --title "seo: what changed" --body "What, why, and the query evidence."
```

If `gh` is unavailable or the push is refused, leave the work committed on the branch and
say so in the report. Do not force anything, and do not fall back to committing prose to
`main`.

Never stage `growth/pipeline.csv`, `growth/sequences/` or `growth/sender.txt`. Never stage
anything under `~/.recoverflow/`.

## Step 7. Log and report

Write `seo/log/<yyyy-mm-dd>.md` containing:

- what the Search Console data showed, or that it was missing
- the changes made, one line each
- what was considered and rejected, with the reason
- anything needing Bruce: a decision, a credential, a claim you could not source
- what the next run should look at first

Commit that file with the rest, then report the same thing back briefly. Report what
changed, not what exists.

---

## Permissions

`.claude/settings.json` pre-authorises everything above: reads and writes under `docs/`,
`scripts/` and `seo/`, the git and `gh` commands in Step 6, `WebSearch` and `WebFetch`. A
scheduled run has nobody to answer a permission prompt, so an unlisted tool means the run
blocks and quietly does nothing. If a step fails for lack of permission, the fix is a commit
to that file, not a workaround in this one.

That file also denies reads of the growth ledger outright. Deny beats allow, so rule 5 holds
even if a future edit to this routine forgets it.

## Search Console data

Preferred path, a service account:

1. Enable the Search Console API in a Google Cloud project.
2. Create a service account and create a JSON key for it.
3. In Search Console, add the `...iam.gserviceaccount.com` address as a **Restricted** user
   on `recoverflow.org`.
4. Save the key at `~/.recoverflow/gsc-key.json`, outside the repo. This repo has already
   had one PII leak and one GitGuardian alert. `.gitignore` denies `*gsc-key*.json` and
   `*service-account*.json` anywhere in the tree as a second line of defence, but the first
   line is not putting it here at all.
5. Run the fetch.

```bash
python3 scripts/fetch_gsc.py fetch
```

Fallback, a manual export, which needs no credentials at all. In Search Console open
Performance, set the date range, choose Export, and download the CSV. Then:

```bash
python3 scripts/fetch_gsc.py import-csv ~/Downloads/Queries.csv
```

Either way the result is `seo/gsc/<yyyy-mm-dd>.json` in one shape, and Step 2 does not care
which produced it.

## If something is missing

Missing Search Console data: note it once, continue with Steps 1, 4d and 5.

Missing this file: the scheduler prompt says to stop and notify rather than improvise. That
is deliberate. An SEO run improvising against a live public site is worse than no run.

Missing a build script, or a build that fails for a reason you cannot trace to your own
change: stop, commit nothing, and report it. That is a repo problem, not an SEO problem.
