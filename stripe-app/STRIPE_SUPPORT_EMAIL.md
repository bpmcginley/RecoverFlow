# Draft: asking Stripe about the Connect platform restriction

**Not sent.** Review and send it yourself.

**Where to send it.** https://support.stripe.com/contact/login while signed in as
`acct_1Trk3HLjCE5YmPhU`, so the account is attached automatically. Topic: Stripe Apps, or
Connect if Apps is not offered. If there is a partner or Apps-specific contact route in the
Dashboard under Developers > Apps, prefer that: this is a publishing-policy question rather
than a bug, and general support will likely reroute it.

**What a good outcome looks like.** Not necessarily "yes". A clear answer to "is this
permanent, and if not, what is the process" is worth as much, because it decides whether to
spend real engineering time on a separate account.

---

**Subject:** Connect platform account blocked from public app distribution

> Hello,
>
> I am trying to publish an app to the App Marketplace from account
> `acct_1Trk3HLjCE5YmPhU` and `stripe apps upload` returns:
>
> > Because your account is a Connect platform, you cannot choose the public distribution at
> > this time.
>
> I could not find this restriction documented in the publishing guide or on the
> distribution-options page, so I have three questions.
>
> 1. Is this permanent for Connect platform accounts, or is there a process to request public
>    distribution?
> 2. If it is permanent, is the expectation that app publishers use a separate Stripe account
>    that is not a Connect platform? I would rather follow the intended pattern than work
>    around it.
> 3. Does the restriction apply to the whole account, or only while a Connect integration is
>    active on it?
>
> Some context in case it matters. The app is a free, read-only audit. It requests three read
> permissions and no write permission of any kind, creates no account, and does not retain the
> access token. It reads a merchant's recent failed charges and emails back a one-page report
> showing how much of their failed revenue sits on decline codes that cannot be recovered by
> retrying at all, plus where they stand against Visa's 15-reattempts-per-card-per-30-days cap.
> If the report shows most of their failures are ordinary retryable ones, it tells them
> Stripe's own Smart Retries already cover them and that they should not buy anything.
>
> The account is a Connect platform because our paid product uses Standard Connect OAuth to
> read and retry invoices for merchants who ask us to. The app is deliberately a separate,
> narrower, read-only thing.
>
> The manifest, listing copy, icon and images are all ready. I am happy to send them if that
> helps assess the request.
>
> Thanks,
> Bruce McGinley
> RecoverFlow
> admin@recoverflow.org

---

## If they say no

Then the decision is whether a listing is worth running the app from a separate, non-Connect
Stripe account. That is not just an account signup: the audit currently reads through the
platform key plus the `Stripe-Account` header, and on a standalone account it would have to use
the merchant's OAuth access token directly. That weakens the "we never use your token" claim,
which measured research on 4 August found to be the one genuinely unoccupied trust claim in the
category, at 0 of 31 apps.

Do not make that trade before the ten-account validation in `VALIDATION.md` says the
retry-waste argument actually lands on real merchants. Buying distribution for an unproven
argument is the expensive order to do this in.
