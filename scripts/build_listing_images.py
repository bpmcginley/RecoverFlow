#!/usr/bin/env python3
"""Generates the three key-feature images for the Stripe Marketplace listing.

Stripe wants images at least 1600px wide, under 10MB, PNG or JPG, and explicitly
forbids real customer data in them. Generating them from a script rather than
screenshotting a real account makes that guarantee structural: the numbers below
are synthetic and committed, so no merchant's data can ever leak into a listing
image, including by accident on a re-render.

Feature 1 uses the exact figures the report renders for the sample account in
RetryWasteReportEmailTests, so the headline image cannot drift from real output.
Feature 2's card list is illustrative: the test fixture only exercises one
over-cap card, and three reads better. Both images say "synthetic data" on their
face, because an unlabelled invented number is the thing this product exists to
argue against.

  python scripts/build_listing_images.py

Writes stripe-app/feature-1.png, feature-2.png, feature-3.png.
"""

import os
from PIL import Image, ImageDraw, ImageFont

W, H = 1600, 1000
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "stripe-app")

BG = (11, 15, 20)
PANEL = (15, 21, 28)
BORDER = (30, 39, 51)
TEXT = (230, 237, 243)
DIM = (159, 176, 192)
MUTE = (107, 122, 138)
ACCENT = (129, 140, 248)
DANGER = (248, 113, 113)
GOOD = (74, 222, 128)

F = "C:/Windows/Fonts/"


def font(name, size):
    return ImageFont.truetype(F + name, size)


TITLE = lambda s: font("seguisb.ttf", s)
BODY = lambda s: font("segoeui.ttf", s)
MONO = lambda s: font("consola.ttf", s)
MONOB = lambda s: font("consolab.ttf", s)


def canvas():
    im = Image.new("RGB", (W, H), BG)
    return im, ImageDraw.Draw(im)


def panel(d, x, y, w, h):
    d.rounded_rectangle([x, y, x + w, y + h], radius=18, fill=PANEL, outline=BORDER, width=2)


def header(d, title, subtitle):
    d.text((80, 74), title, font=TITLE(52), fill=TEXT)
    d.text((80, 146), subtitle, font=BODY(27), fill=DIM)


def footer(d, text):
    d.text((80, H - 74), text, font=BODY(21), fill=MUTE)


def feature_1():
    """The headline claim: attempts that could never have reached the network."""
    im, d = canvas()
    header(d, "Find the retries that were never going to work",
           "From the emailed report. Sample account, synthetic data.")

    panel(d, 80, 220, W - 160, 516)
    x, y = 130, 268

    d.text((x, y), "WHAT COULD NEVER HAVE WORKED", font=MONOB(26), fill=TEXT)
    y += 46
    d.text((x, y), "14 charges (27%), $936, on the nine decline codes Stripe", font=MONO(25), fill=DIM)
    y += 34
    d.text((x, y), "will not send to the card network until a new payment", font=MONO(25), fill=DIM)
    y += 34
    d.text((x, y), "method is attached.", font=MONO(25), fill=DIM)
    y += 62

    for code, n, amt in [("stolen_card", "5", "$495"), ("lost_card", "9", "$441")]:
        d.text((x + 40, y), code, font=MONO(26), fill=DANGER)
        d.text((x + 460, y), n, font=MONO(26), fill=TEXT, anchor="ra")
        d.text((x + 640, y), amt, font=MONO(26), fill=TEXT, anchor="ra")
        y += 40

    y += 44
    d.text((x, y), "WHAT NEEDED A NEW CARD, NOT ANOTHER ATTEMPT", font=MONOB(26), fill=TEXT)
    y += 46
    d.text((x, y), "7 charges (13%), $203. Stripe does retry these, so they", font=MONO(25), fill=DIM)
    y += 34
    d.text((x, y), "burn attempts, but the card cannot succeed until it is", font=MONO(25), fill=DIM)
    y += 34
    d.text((x, y), "replaced.", font=MONO(25), fill=DIM)

    footer(d, "Every figure is arithmetic on your own charges. No recovery-rate estimate appears anywhere.")
    return im


def feature_2():
    """The thing no other listing in the category claims to do."""
    im, d = canvas()
    header(d, "Check your Visa reattempt budget",
           "Visa caps reattempts at 15 per card per 30 days. Stripe blocks further attempts past it. Synthetic data.")

    panel(d, 80, 240, W - 160, 496)
    x, y = 130, 292

    d.text((x, y), "VISA REATTEMPT BUDGET (cap is 15 per card per 30 days)", font=MONOB(26), fill=TEXT)
    y += 56
    d.text((x, y), "3 card(s) reached 10 or more attempts in 30 days.", font=MONO(25), fill=DIM)
    y += 34
    d.text((x, y), "1 of them hit 15 or more, where Stripe starts blocking", font=MONO(25), fill=DIM)
    y += 34
    d.text((x, y), "further attempts outright.", font=MONO(25), fill=DIM)
    y += 66

    rows = [("fp_8c21ba49", 17, True), ("fp_1d90fe72", 12, False), ("fp_4a7b0c15", 11, False)]
    for ref, n, over in rows:
        d.text((x + 40, y), ref, font=MONO(26), fill=TEXT)
        d.text((x + 500, y), f"{n} attempts", font=MONO(26), fill=DANGER if over else DIM)
        if over:
            d.text((x + 720, y), "<-- at or over the cap", font=MONO(26), fill=DANGER)
        y += 44

    y += 34
    d.text((x, y), "Counted per card, in any rolling 30-day window.", font=BODY(24), fill=MUTE)

    footer(d, "Card fingerprints are opaque identifiers from Stripe. They are not card numbers.")
    return im


def feature_3():
    """The trust argument, which is the conversion problem this app exists to solve."""
    im, d = canvas()
    header(d, "Read-only, and it says so",
           "Three read permissions. No write permission of any kind is requested.")

    panel(d, 80, 250, W - 160, 300)
    x, y = 130, 296
    for perm, why in [
        ("invoice_read", "count what failed and what it was worth"),
        ("charge_read", "read the decline code on each failed charge"),
        ("subscription_read", "tell failed renewals from one-off invoices"),
    ]:
        d.text((x, y), "READ", font=MONOB(24), fill=GOOD)
        d.text((x + 90, y), perm, font=MONO(26), fill=TEXT)
        d.text((x + 420, y), why, font=BODY(24), fill=DIM)
        y += 58

    y += 12
    d.text((x, y), "WRITE", font=MONOB(24), fill=MUTE)
    d.text((x + 90, y), "none requested", font=MONO(26), fill=MUTE)

    panel(d, 80, 590, W - 160, 230)
    x, y = 130, 632
    for line in [
        "We do not store your Stripe access token. The authorization code is",
        "exchanged only to learn which account authorized the audit.",
        "Running the audit does not create an account, and the report is not retained.",
    ]:
        d.text((x, y), line, font=BODY(26), fill=DIM)
        y += 44

    footer(d, "If most of your failures are the ordinary retryable kind, the report says so and tells you to buy nothing.")
    return im


def main():
    for i, build in enumerate([feature_1, feature_2, feature_3], start=1):
        path = os.path.join(OUT, f"feature-{i}.png")
        im = build()
        im.save(path, "PNG", optimize=True)
        kb = os.path.getsize(path) / 1024
        assert im.size[0] >= 1600, im.size
        assert kb < 10 * 1024, kb
        print(f"built {path}  {im.size[0]}x{im.size[1]}  {kb:.0f} KB")


if __name__ == "__main__":
    main()
