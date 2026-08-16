---
name: growth
description: Run a RecoverFlow outbound cycle - check for replies, send the follow-ups that are due, source new prospects, and report the funnel. Use when asked to run growth, do outreach, follow up with prospects, work the pipeline, find customers, or when a scheduled routine reviews business activity and outbound is owed a touch.
---

# Running an outbound cycle

The ledger at `growth/pipeline.csv` is the source of truth, not memory and not the inbox.
Read `growth/README.md` for the operating rules and `growth/icp.md` for targeting. Never
duplicate what those files say into a reply; act on them.

All commands run from the repo root.

## 1. Log replies before anything else

Always do this first. Following up someone who already replied is the one unrecoverable
mistake available here.

For each prospect whose `status` is `t1_sent`/`t2_sent`/`t3_sent` and whose `thread_id` is
set, check the thread for a message that is not from Bruce:

```
mcp__Gmail__get_thread(threadId=<thread_id>, messageFormat="PLAIN_TEXT")
```

A thread with only `SENT` messages means no reply. Where there is a reply, log it and stop
the sequence for that person:

```
python3 growth/scripts/pipeline.py log --email <email> --event replied --outcome <positive|not-now|no>
```

If they asked not to be contacted, use `--event disqualified` instead. That is permanent.
Also sweep for bounces:

```
mcp__Gmail__search_threads(query="newer_than:7d (from:mailer-daemon OR subject:\"Delivery Status Notification\")")
```

## 2. Send what is due

```
python3 growth/scripts/pipeline.py validate
python3 growth/scripts/pipeline.py due
```

If nothing is due, say so plainly and skip to step 3. Do not invent work by sending early;
the cadence is deliberate.

For each due prospect:

```
python3 growth/scripts/pipeline.py render --email <email>
```

The first line of the output is a subject instruction, not a subject line. Follow-ups reply
on the existing thread and must not start a new one. Create the draft with `thread_id` as
the reply target, and strip that first instruction line from the body:

```
mcp__Gmail__create_draft(replyToMessageId=<thread_id>, to=[<email>], body=<rendered body>)
```

**Create drafts. Do not send.** Sending cold email from Bruce's mailbox is outward-facing
and irreversible; he reviews and sends. Ask for explicit confirmation if he wants that
changed, and treat approval as covering that batch only.

Once he confirms a batch went out:

```
python3 growth/scripts/pipeline.py log --email <email> --event t2_sent
```

Refuse to draft anything while `growth/sender.txt` still reads `TODO-POSTAL-ADDRESS`, unless
Bruce has been told and says to proceed anyway. CAN-SPAM requires a physical postal address
on commercial email and only he can supply one.

## 3. Top up the queue

If fewer than 20 prospects are in `sourced` or `t1_sent`, source more. **Do not reach for
Apollo.** It was tested on 14 August 2026 and cannot serve this ICP on the current plan: the
search API is plan-excluded, the Stripe technology filter is paid-only, and the AI-research
fallback qualified 0 of 25. `icp.md` records the detail. Going further needs a plan upgrade,
which is Bruce's call and not something to spend into unasked.

Source by hand from the places listed in `icp.md`, verifying Stripe by opening the company's
checkout. Forty good rows a week is the target and it is enough; the constraint is the
personalisation sentence, not the list.

Run the three hand checks from `icp.md` on every result. Roughly half should fail them. Then:

```
python3 growth/scripts/pipeline.py add --email … --first-name … --company … --track …
```

Do not write the T1 personalisation sentence from the company's tagline. Read something they
published. If you cannot write one true, specific sentence, drop the prospect rather than
sending a generic paragraph, and say in your report that you dropped it and why.

## 4. Report

```
python3 growth/scripts/pipeline.py stats
```

Report what changed, not what exists: replies logged, drafts created, prospects added,
anything dropped and why. Include the funnel numbers only when they moved.

Before suggesting the copy is underperforming, apply the arithmetic in `README.md`: under
roughly 60 sends, silence carries no signal, and rewriting copy on that basis destroys the
only comparison worth having. Say so if asked to change copy too early.

## 5. Do not commit the ledger

`growth/pipeline.csv`, `growth/sequences/` and `growth/sender.txt` are gitignored. They were
tracked until 16 August 2026, which published 22 prospects' names and work emails on a public
repo. The repo sits under OneDrive, so the ledger is backed up without git.

Never `git add -f` them back. If a growth change touches something that *is* tracked, such as
`icp.md` or the scripts, commit only that:

```
git add growth/icp.md growth/scripts && git commit -m "growth: <what changed>"
```

## Guardrails

- 10 sends a day maximum, one mailbox.
- No true personalisation sentence, no send.
- Never send cold mail from `admin@recoverflow.org`; it carries audits, support and the
  product's own dunning mail.
- One opt-out request ends contact permanently.
- Never fabricate a prospect's details, funding, headcount or tooling to fill a template.
