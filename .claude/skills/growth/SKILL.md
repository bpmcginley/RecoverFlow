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

If fewer than 20 prospects are in `sourced` or `t1_sent`, source more. Use the Apollo recipe
in `icp.md` via `mcp__Apollo-io__apollo_mixed_people_api_search`. Budget is tight: check
`apollo_usage_stats_credit_usage_stats` first, and there are no export credits, so add
contacts to the ledger directly rather than trying to dump a CSV.

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

## 5. Commit

The ledger is state. Commit it every time it changes, or the next session starts from a
stale picture:

```
git add growth/ && git commit -m "growth: <what changed>"
```

## Guardrails

- 10 sends a day maximum, one mailbox.
- No true personalisation sentence, no send.
- Never send cold mail from `admin@recoverflow.org`; it carries audits, support and the
  product's own dunning mail.
- One opt-out request ends contact permanently.
- Never fabricate a prospect's details, funding, headcount or tooling to fill a template.
