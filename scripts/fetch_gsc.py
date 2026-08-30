#!/usr/bin/env python3
"""Pull Search Console performance data into seo/gsc/<date>.json.

Two ways in, one shape out, so seo/ROUTINE.md does not care which was used:

    python3 scripts/fetch_gsc.py fetch                    # service account, needs a key
    python3 scripts/fetch_gsc.py import-csv Queries.csv   # manual Performance export

The key lives at ~/.recoverflow/gsc-key.json and never in this repo. The repo has already
had one PII leak and one GitGuardian alert; .gitignore denies key-shaped filenames as a
second line of defence, but the first line is keeping the file outside the tree.

The manual export path needs no credentials at all and is the reason the weekly routine
still works before anyone has been near Google Cloud.
"""

import argparse
import csv
import datetime
import json
import os
import pathlib
import sys
import urllib.error
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "seo" / "gsc"
KEY_PATH = pathlib.Path(os.environ.get("GSC_KEY", "~/.recoverflow/gsc-key.json")).expanduser()

# A domain property covers http, https and every subdomain, so it is the one to add the
# service account to. Override with GSC_SITE if the property is a URL-prefix property.
SITE = os.environ.get("GSC_SITE", "sc-domain:recoverflow.org")

API = "https://searchconsole.googleapis.com/webmasters/v3/sites/{site}/searchAnalytics/query"
SCOPE = "https://www.googleapis.com/auth/webmasters.readonly"
ROW_LIMIT = 1000


def die(msg):
    print(msg, file=sys.stderr)
    raise SystemExit(1)


# --------------------------------------------------------------------------- auth

def access_token():
    """Exchange the service account key for a read-only Search Console token.

    Tries google-auth first because it handles clock skew and retries properly. Falls back
    to signing the JWT assertion by hand so a box with cryptography but no google-auth
    still works. If neither is available, say so and point at import-csv rather than
    failing with an ImportError nobody can act on.
    """
    if not KEY_PATH.exists():
        die(
            "No service account key at {}.\n"
            "Either create one (see 'Search Console data' in seo/ROUTINE.md) or use:\n"
            "  python3 scripts/fetch_gsc.py import-csv <export.csv>".format(KEY_PATH)
        )

    try:
        from google.oauth2 import service_account
        import google.auth.transport.requests

        creds = service_account.Credentials.from_service_account_file(
            str(KEY_PATH), scopes=[SCOPE]
        )
        creds.refresh(google.auth.transport.requests.Request())
        return creds.token
    except ImportError:
        pass

    try:
        return _token_by_hand()
    except ImportError:
        die(
            "Need either google-auth or cryptography to sign the token:\n"
            "  pip install google-auth requests\n"
            "Or skip credentials entirely and use:\n"
            "  python3 scripts/fetch_gsc.py import-csv <export.csv>"
        )


def _token_by_hand():
    import base64
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import padding

    key = json.loads(KEY_PATH.read_text(encoding="utf-8"))
    now = int(datetime.datetime.now(datetime.timezone.utc).timestamp())

    def b64(obj):
        raw = obj if isinstance(obj, bytes) else json.dumps(obj, separators=(",", ":")).encode()
        return base64.urlsafe_b64encode(raw).rstrip(b"=")

    claims = {
        "iss": key["client_email"],
        "scope": SCOPE,
        "aud": key["token_uri"],
        "iat": now,
        "exp": now + 3600,
    }
    signing_input = b64({"alg": "RS256", "typ": "JWT"}) + b"." + b64(claims)
    private_key = serialization.load_pem_private_key(key["private_key"].encode(), password=None)
    signature = private_key.sign(signing_input, padding.PKCS1v15(), hashes.SHA256())
    assertion = (signing_input + b"." + b64(signature)).decode()

    body = urllib.parse.urlencode(
        {"grant_type": "urn:ietf:params:oauth:grant-type:jwt-bearer", "assertion": assertion}
    ).encode()
    req = urllib.request.Request(key["token_uri"], data=body)
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.load(resp)["access_token"]


# --------------------------------------------------------------------------- api

def query(token, start, end, dimensions):
    payload = json.dumps(
        {
            "startDate": start,
            "endDate": end,
            "dimensions": dimensions,
            "rowLimit": ROW_LIMIT,
            "dataState": "final",
        }
    ).encode()
    req = urllib.request.Request(
        API.format(site=urllib.parse.quote(SITE, safe="")),
        data=payload,
        headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            rows = json.load(resp).get("rows", [])
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", "replace")[:500]
        if exc.code == 403:
            detail += (
                "\n\n403 usually means the service account is not a user on the property. "
                "Add its ...iam.gserviceaccount.com address as a Restricted user in Search "
                "Console, or check GSC_SITE matches the property type."
            )
        die("Search Console returned {}: {}".format(exc.code, detail))

    out = []
    for row in rows:
        rec = dict(zip(dimensions, row.get("keys", [])))
        rec.update(
            clicks=row.get("clicks", 0),
            impressions=row.get("impressions", 0),
            ctr=round(row.get("ctr", 0.0), 4),
            position=round(row.get("position", 0.0), 2),
        )
        out.append(rec)
    return out


def do_fetch(args):
    end = datetime.date.today() - datetime.timedelta(days=args.lag)
    start = end - datetime.timedelta(days=args.days - 1)
    token = access_token()
    write(
        {
            "site": SITE,
            "source": "api",
            "range": {"start": start.isoformat(), "end": end.isoformat()},
            "queries": query(token, start.isoformat(), end.isoformat(), ["query"]),
            "pages": query(token, start.isoformat(), end.isoformat(), ["page"]),
            "query_pages": query(token, start.isoformat(), end.isoformat(), ["query", "page"]),
        }
    )


# --------------------------------------------------------------------------- csv

# The Performance export labels the first column by what was exported. Everything else is
# the same four metric columns, and CTR arrives as a percentage string.
FIRST_COL = {
    "top queries": "query",
    "query": "query",
    "queries": "query",
    "top pages": "page",
    "page": "page",
    "pages": "page",
}


def num(value, pct=False):
    text = (value or "").strip().replace("%", "").replace(",", "")
    if not text:
        return 0
    try:
        val = float(text)
    except ValueError:
        return 0
    if pct:
        return round(val / 100.0, 4)
    return int(val) if val.is_integer() else round(val, 2)


def do_import(args):
    path = pathlib.Path(args.path).expanduser()
    if not path.exists():
        die("No such file: {}".format(path))

    with path.open(encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    if len(rows) < 2:
        die("{} has no data rows. Export the Performance report, not a filtered empty view.".format(path))

    header = [c.strip().lower() for c in rows[0]]
    kind = FIRST_COL.get(header[0])
    if kind is None:
        die(
            "First column is {!r}; expected a queries or pages export.\n"
            "Use the Queries.csv or Pages.csv from the Performance report zip.".format(rows[0][0])
        )

    records = []
    for row in rows[1:]:
        if not row or not row[0].strip():
            continue
        cells = row + [""] * (5 - len(row))
        records.append(
            {
                kind: cells[0].strip(),
                "clicks": num(cells[1]),
                "impressions": num(cells[2]),
                "ctr": num(cells[3], pct=True),
                "position": num(cells[4]),
            }
        )

    payload = {
        "site": SITE,
        "source": "csv",
        "range": {"start": None, "end": None},
        "queries": [],
        "pages": [],
        "query_pages": [],
        "note": "Imported from {}. The export carries no date range, so trend comparison "
                "against an earlier file assumes both cover the same window length.".format(path.name),
    }
    payload["queries" if kind == "query" else "pages"] = records

    # A second import on the same day merges rather than clobbers, so importing Queries.csv
    # and then Pages.csv leaves one file holding both.
    existing = OUT_DIR / (datetime.date.today().isoformat() + ".json")
    if existing.exists():
        prior = json.loads(existing.read_text(encoding="utf-8"))
        if prior.get("source") == "csv":
            for key in ("queries", "pages", "query_pages"):
                payload[key] = payload[key] or prior.get(key, [])
    write(payload)


# --------------------------------------------------------------------------- io

def write(payload):
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    payload["fetched"] = datetime.date.today().isoformat()
    out = OUT_DIR / (payload["fetched"] + ".json")
    out.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(
        "{}: {} queries, {} pages, {} query+page rows".format(
            out.relative_to(ROOT),
            len(payload["queries"]),
            len(payload["pages"]),
            len(payload["query_pages"]),
        )
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    subs = parser.add_subparsers(dest="cmd", required=True)

    api = subs.add_parser("fetch", help="pull from the Search Console API")
    api.add_argument("--days", type=int, default=28, help="window length, default 28")
    api.add_argument(
        "--lag",
        type=int,
        default=3,
        help="days to skip at the end, default 3, because recent data is still settling",
    )
    api.set_defaults(func=do_fetch)

    imp = subs.add_parser("import-csv", help="normalise a manual Performance export")
    imp.add_argument("path", help="Queries.csv or Pages.csv from the Performance report")
    imp.set_defaults(func=do_import)

    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
