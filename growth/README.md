# Outbound, as a loop rather than an evening

On 12 August 2026, twelve prospects were emailed in a single evening. All twelve were
delivered, none bounced, none replied, and none were followed up. That is the failure mode
this directory exists to prevent: outbound as an event that happens when someone remembers,
instead of a queue that says what is owed today.

**The ledger is the only source of truth.** `pipeline.csv` holds the state of the pipeline, so
it survives closed laptops, new sessions and lost context. Nothing here depends on anyone
remembering where it was left.

It used to be committed for that durability, which published 22 people's names and work
emails on a public repo for anyone who found the raw URL. It is gitignored now. The repo
lives under OneDrive, so the file is still backed up and nothing is lost by keeping it out of
git. **Do not re-add it**, and the same goes for `sequences/` and `sender.txt`.

## The daily loop, about ten minutes

```
python3 growth/scripts/pipeline.py due
```

That prints who is owed a touch today and which one. For each name:

```
python3 growth/scripts/pipeline.py render --email chris@loops.so
```

Send it from Gmail. **Follow-ups go as replies on the original thread**, not as new
messages: the first email is the context, and a fresh thread throws it away. Then record it:

```
python3 growth/scripts/pipeline.py log --email chris@loops.so --event t2_sent
```

When someone replies, log that instead, and the sequence stops for them automatically:

```
python3 growth/scripts/pipeline.py log --email chris@loops.so --event replied --outcome positive
```

Events: `t1_sent` `t2_sent` `t3_sent` `replied` `bounced` `disqualified` `closed`
`connected` `customer`. `connected` means they linked Stripe. `customer` means they are
paying.

## Running it unattended

Two mechanisms exist and they are not interchangeable. The difference is not convenience, it
is whether the run can see the ledger at all.

### A Claude routine, once the ledger can be reached

A routine is a prompt Claude fires on a schedule, set up where the daily business-review
routine already is. The prompt is one line, because the skill holds the procedure:

```
Run the growth skill's outbound cycle for today: log replies, send what preflight
clears, top up the queue, and push me a summary of what went out.
```

**This does not work yet, and the reason is not the scheduler.** Routines fire in a fresh
clone of the repo, and `pipeline.csv`, `sequences/` and `sender.txt` are gitignored and live
only on Bruce's machine. A routine firing today finds no ledger, sends nothing, and says so.
Committing them to fix it is not an option: that is exactly what put 22 prospects' names and
work emails on a public repo in August.

What unblocks it is moving the ledger somewhere both a laptop and a scheduled run can reach
privately. **HubSpot is the obvious candidate and is already most of the way there** — it
holds the same prospects, and `notes_last_contacted` already tracks sends. Finishing it means
custom properties for the fields HubSpot has no home for (`status`, `t1_date`/`t2_date`/
`t3_date`, `thread_id`, `transport`) and teaching `pipeline.py` to read and write there
instead of a CSV. That is a real change to where the pipeline's truth lives, so it is Bruce's
call, not a thing to do quietly.

### An OS scheduler, which works today

Until then this is the only automation that can actually send, because it runs on the machine
the ledger is on. `run_outbound.py` checks whether anything is
due, runs `preflight`, and only then hands the cycle to Claude:

```
python3 growth/scripts/run_outbound.py --dry-run   # decide and report, send nothing
python3 growth/scripts/run_outbound.py             # decide and run
```

Nothing due exits 0 without waking Claude. A failed preflight exits 1 with the reason and
sends nothing. Run history appends to `growth/outbound.log`, which is gitignored because it
names who was mailed.

**It only runs where the ledger is**, which is this machine and not a fresh clone: a cloud or
CI session has no `pipeline.csv`, no templates and no `sender.txt`, so the script says so and
stops rather than improvising.

Schedule it Monday to Thursday, mid-morning. `preflight` refuses weekends anyway, so a
weekday-only schedule is belt and braces rather than the actual guard.

```
# macOS / Linux, crontab -e — 09:30 Mon-Thu
30 9 * * 1-4 cd /path/to/RecoverFlow && /usr/bin/python3 growth/scripts/run_outbound.py

# Windows, PowerShell as the same user Claude Code is signed in as
schtasks /create /tn "RecoverFlow outbound" /sc weekly /d MON,TUE,WED,THU /st 09:30 ^
  /tr "python C:\path\to\RecoverFlow\growth\scripts\run_outbound.py"
```

Set `CLAUDE_BIN` if `claude` is not on the scheduler's PATH, which on Windows it usually is
not. The scheduled task runs as a specific user and reaches Claude Code's MCP connections
through that user's config, so it has to be the account signed in to Outlook as
`admin@recoverflow.org`. A task running as SYSTEM will not have that mailbox.

## The weekly loop, about thirty minutes

Top the queue back up so the daily loop has something to do:

1. Source against the Apollo recipe in `icp.md`.
2. Run the three hand checks in that file. Roughly half will fail them. Good.
3. Add survivors: `pipeline.py add --email … --first-name … --company … --track …`
4. Write the one true sentence for each, into the `>>` slot in the `t1-*` template.

Target 30 to 40 added a week. That is roughly 200 a month, which is both the Apollo credit
ceiling and the realistic personalised-send ceiling for one person. Those two numbers
agreeing is a coincidence, but a convenient one.

## The cadence

| Touch | When | Ask |
|---|---|---|
| T1 | day 0 | The product, plus one true sentence about them |
| T2 | +3 business days | The free audit: reply with three numbers |
| T3 | +5 business days after T2 | Nothing. Hand over the calculator and leave |

T2 carries the sequence. It asks for a reply with three numbers instead of a signup, needs
no Stripe connection and no card, and it openly says the answer might be "Stripe already
covers you, don't buy anything." That is the lowest-friction, highest-trust ask available,
and it is the one the first evening never made.

The script skips weekends and warns on Fridays. Send Monday to Thursday.

## Read the numbers honestly

```
python3 growth/scripts/pipeline.py stats
```

Before rewriting copy because nothing is landing, do this arithmetic. If the true reply rate
were 5%, the chance of seeing zero replies across 12 sends is `0.95¹² = 54%`. **A coin flip.**
Twelve silent prospects is not evidence the copy is bad; it is not evidence of anything.

At 60 sends the same 5% rate gives `0.95⁶⁰ = 4.6%` odds of total silence. So:

- **Under ~60 sends: do not touch the copy.** There is no signal there to respond to.
- **At 60+ with zero replies:** now something is wrong. Change one thing, not four, or you
  will not know which one worked.

The one number that matters more than reply rate is **replies per hour spent**. Volume is
capped by personalisation, and personalisation is what makes the replies happen.

## Guardrails

- **10 sends a day, hard.** One mailbox sending cold. Past that, deliverability drops
  and personalisation quality drops faster. The script flags it.
- **No true sentence, no send.** Drop the prospect instead. A generic middle paragraph costs
  a real reply and burns the address for any future attempt.
- **One opt-out request ends it forever.** `log --event disqualified` the moment it arrives.
- **Cold mail goes from `admin@recoverflow.org` via Outlook.** Bruce decided this on
  19 August 2026. The rule here used to say the opposite, and the reason it did has not gone
  away, so it is written down rather than deleted: `render.yaml` sets `Email__FromAddress` to
  that same address, so every dunning mail, audit report and sign-in link the product sends
  leaves from the address the cold campaign now uses. Cold mail draws spam complaints at
  rates transactional mail never does, and reputation is tracked per From address, so the
  downside is not a full inbox — it is merchants' recovery rates falling because dunning
  stopped landing. That is the product.

  **So watch it.** If dunning starts hitting spam or SendGrid reputation drops, move outbound
  off this address first and ask questions after. Moving it is cheap: give outreach its own
  mailbox (`bruce@` or an `outreach.recoverflow.org` subdomain with its own DKIM), point the
  ledger's `transport` at it, and the product's address goes back to carrying only
  transactional mail.

## Compliance, currently incomplete

Cold B2B sales email is commercial email under CAN-SPAM. It requires a valid physical postal
address and a working opt-out in every message. **The twelve already sent had neither.**

`sender.txt` now supplies both, and every template ends with it, but it still reads
`TODO-POSTAL-ADDRESS`. `render` refuses to stay quiet about that and prints a warning.

**Fill it in before the next send.** A PO box or a registered-agent address satisfies the
requirement; a home address is not required. This is the one item in this directory that
cannot be resolved from inside the repo.

Four of the twelve are outside the US (Hungary, UK, and probably France). UK PECR generally
permits B2B cold email to corporate addresses with an opt-out, and GDPR legitimate interest
generally covers it, but "generally" is doing real work in that sentence. The opt-out line
is what most of it rests on, so it needs to be there.

## Files

| | |
|---|---|
| `pipeline.csv` | The ledger. Source of truth. Local only, gitignored. |
| `scripts/pipeline.py` | The queue. Stdlib only, no install. |
| `sequences/*.txt` | The copy. Edit freely, no code changes needed. |
| `sender.txt` | Signature, postal address and opt-out, shared by every template. |
| `icp.md` | Who to write to, the arithmetic behind the range, the Apollo recipe. |

`.claude/skills/growth/SKILL.md` drives all of this from `/growth` in a Claude Code session,
including the scheduled daily routine.

## Why outbound and SEO carry this alone

The Stripe App Marketplace would have been the third channel and it is closed: Stripe will
not grant public distribution to a Connect platform account, which is what RecoverFlow must
be in order to read merchant data at all (`stripe-app/BLOCKED.md`). At $29 to $299 a month,
paid acquisition does not clear its own cost either. Two channels, and this is one of them.
