---
name: growth
description: Run a RecoverFlow outbound cycle - check for replies, send the follow-ups that are due, source new prospects, and report the funnel. Use when asked to run growth, do outreach, follow up with prospects, work the pipeline, find customers, or when a scheduled routine reviews business activity and outbound is owed a touch.
---

# Running an outbound cycle

The ledger at `growth/pipeline.csv` is the source of truth, not memory and not the inbox.
Read `growth/README.md` for the operating rules and `growth/icp.md` for targeting. Never
duplicate what those files say into a reply; act on them.

All commands run from the repo root.

## Where this can run

**This cycle only works where the ledger is, which today means Bruce's own machine.**
`pipeline.csv`, `sequences/` and `sender.txt` are gitignored — deliberately, after they
published 22 prospects' names and work emails on a public repo — and they live under OneDrive
instead. A Claude Code web or scheduled cloud session is a fresh clone of the repo and gets
none of them, so `due` exits with `No ledger`, there are no templates to render, and there is
no postal address to sign with.

If that is where you are: **stop at step 1 and say so.** Do not reconstruct the ledger from
the inbox, and never `git add -f` it to make the cloud copy work — that re-publishes the exact
PII the gitignore exists to keep off the internet. Report the funnel from what you can see and
leave the sending to a local run.

This is also why a **Claude routine** cannot yet run this cycle: routines fire in a fresh
clone, so they land in exactly this state. Nothing here is fixable from inside the container.
Giving a routine real outbound reach means moving the ledger into a private store both a
laptop and a scheduled run can reach — HubSpot already holds these prospects and their
last-contacted dates, so it is the short path — and that is Bruce's call to make, not
something to start unasked. If a routine fires here, say that in one line and push it; do not
re-derive the whole argument each time.

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
python3 growth/scripts/pipeline.py preflight
```

If nothing is due, say so plainly and skip to step 3. Do not invent work by sending early;
the cadence is deliberate.

**`preflight` decides whether anything may go out.** It exits non-zero on a missing or
placeholder postal address, a missing template, a follow-up with no thread to reply on, a
weekend, or a day whose send cap is already spent. If it fails, **send nothing**, fix what it
names or report it, and go to step 3. Do not talk yourself past a failed check: every one of
them was a warning a human used to read on the way to the send button, and there is no human
in this loop now.

### Sending

Bruce authorised this loop to send on its own on 19 August 2026, standing until he says
otherwise. It replaces the old draft-and-wait posture.

Work **one prospect at a time**, and never start the next before the current one is logged:

```
python3 growth/scripts/pipeline.py render --email <email>
```

The first line of the output is a subject instruction, not a subject line; strip it from the
body.

**Send from the mailbox the prospect's `transport` names, which `preflight` prints.** New
prospects default to `outlook`, which sends as `admin@recoverflow.org`. Anything contacted
before 19 August 2026 is `gmail` and stays there: it has a Gmail thread Outlook cannot reply
into, and a follow-up from a different address on a new thread reads as a stranger quoting
nothing. Finish those where they started. `preflight` fails rather than let the two mix.

**Outlook** (`transport: outlook`) — draft, send, then log:

- **T1** — `mcp__Microsoft-365__outlook_create_draft(to=[<email>], subject=…, body=…,
  bodyType="html")`, then `outlook_send_draft(messageId=<draft id>)`.
  Sending moves the message to Sent Items and the draft id does not reliably survive it, so
  find the sent copy before logging:
  `outlook_email_search(folderName="Sent Items", query="<subject>", limit=5)` and record
  *that* id — it is what T2 replies into:
  `pipeline.py log --email <email> --event t1_sent --thread-id <sent id>`
- **T2 and T3** — `outlook_create_reply_draft(messageId=<thread_id>, body=…,
  bodyType="html")`, then `outlook_send_draft(messageId=<draft id>)`, then
  `pipeline.py log --email <email> --event t2_sent`

Bodies must be HTML built from the allowlist: paragraphs in `<p>`, breaks as `<br>`, links as
`<a>`. Images, `<span>`, `<style>` and comments are **rejected outright**, not stripped, so a
send fails on them rather than going out mangled.

**Gmail** (`transport: gmail`, legacy sequences only) — `mcp__Gmail__send_message` for T1
capturing the thread id, `mcp__Gmail__reply(messageId=<thread_id>, body=…)` for T2 and T3,
then log the same way.

**Log each send before the next send, not at the end of the batch.** On 18 August five
prospects each received the same T2 twice, four minutes apart, because a batch went out and
was replayed before the ledger caught up. `due` reads status, so a logged send is invisible to
the next run and an unlogged one is not: the gap between sending and logging is the entire
window in which a person gets mailed twice. Keep it at one send wide.

Stop the batch and report if a send errors. Do not retry a send whose outcome you cannot
determine — a duplicate costs more than a missed touch, and the prospect stays due tomorrow.

Two things still stop a send outright, no matter what preflight says: **no true
personalisation sentence** (drop the prospect and say why), and **any address on an opt-out**.
If Bruce wants to eyeball a batch first, `mcp__Gmail__create_draft` instead of sending and
leave the ledger unlogged — an unlogged prospect is simply still due.

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

Report what changed, not what exists: replies logged, **who was mailed and at which touch**,
prospects added, anything dropped and why, and any preflight failure that held the batch.
Include the funnel numbers only when they moved.

When this runs unattended, the reply is written into a session nobody reads, so a run that
sent mail or was blocked from sending has to reach Bruce through `PushNotification`. A quiet
run that sent nothing and found nothing does not.

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

- `preflight` is the gate, not advice. Non-zero means send nothing.
- 10 sends a day maximum, one mailbox, counted per day and not per run.
- Log every send before making the next one.
- No true personalisation sentence, no send.
- Cold mail now goes from `admin@recoverflow.org` via Outlook, by Bruce's decision on
  19 August 2026, overriding the rule that used to forbid exactly this. **Watch the
  deliverability of that address**, because `render.yaml` sets `Email__FromAddress` to it
  too: the product's dunning mail, audit reports and sign-in links leave from the same
  address the cold campaign now does. If dunning starts landing in spam, or SendGrid
  reputation drops, this is the first thing to suspect and outbound moves off it.
- One opt-out request ends contact permanently.
- Never fabricate a prospect's details, funding, headcount or tooling to fill a template.
