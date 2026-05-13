#!/usr/bin/env python3
"""Apply edge-SSO to an nginx vhost on the VPS.

For each domain, rewrites /etc/nginx/sites-available/<domain> to:
  - keep the existing TLS / port-80→443 redirect server blocks intact
  - drop any auth_basic / auth_basic_user_file lines
  - insert `include snippets/sso-auth.conf;` at the server level
  - add `location @sso_redirect { ... }` for the 401 -> sign_in chain
  - insert `include snippets/sso-headers.conf;` inside the `location /` block
  - optionally add bypass `location ~ <pattern>` blocks BEFORE `location /`

Idempotent: detects already-SSO'd files via marker comment.

Usage:
    python3 apply-sso.py <domain> [--bypass '<regex>'] [--bypass '<regex>']
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

SSH = ["ssh", "-o", "BatchMode=yes", "root@159.223.76.215"]
MARKER = "# === SSO_APPLIED ==="


def remote_read(path: str) -> str:
    r = subprocess.run(SSH + ["cat", path], capture_output=True, text=True, check=True)
    return r.stdout


def remote_write(path: str, content: str) -> None:
    subprocess.run(
        SSH + [f"cat > {path}"], input=content, text=True, check=True
    )


def remote_run(cmd: str) -> str:
    r = subprocess.run(SSH + [cmd], capture_output=True, text=True)
    if r.returncode != 0:
        raise RuntimeError(f"remote: {cmd}\n{r.stderr}")
    return r.stdout


def transform(src: str, bypass_patterns: list[str], upstream_ws: bool) -> str:
    if MARKER in src:
        return src  # idempotent

    # Drop Basic Auth lines
    out = []
    for ln in src.splitlines():
        stripped = ln.strip()
        if stripped.startswith("auth_basic ") or stripped.startswith("auth_basic_user_file"):
            continue
        out.append(ln)
    src = "\n".join(out)

    # Find the `server {` block that has the TLS listen (the real one, not the :80 redirect).
    # Heuristic: the server block containing 'location /'.
    # Insert `include snippets/sso-auth.conf;` and `location @sso_redirect { ... }`
    # at the server level, right before the first `location /`.

    sso_server_block = (
        f"    {MARKER}\n"
        f"    include snippets/sso-auth.conf;\n"
        f"    location @sso_redirect {{\n"
        f"        return 302 https://$host/oauth2/sign_in?rd=$scheme://$host$request_uri;\n"
        f"    }}\n"
    )

    # Build bypass blocks (no auth_request, just proxy to upstream)
    bypass_blocks = ""
    for pat in bypass_patterns:
        bypass_blocks += (
            f"    # BYPASS SSO — machine endpoint with its own auth\n"
            f"    location ~ {pat} {{\n"
            f"        # === bypass body — copy of `location /` proxy_pass kept by next step ===\n"
            f"        __BYPASS_BODY__\n"
            f"    }}\n\n"
        )

    # Inject before first `location /` (within outermost server block).
    # We'll do a regex split.
    pattern = re.compile(r"(^\s*location\s+/\s*\{)", re.MULTILINE)
    m = pattern.search(src)
    if not m:
        raise SystemExit("could not find `location / {` in vhost — refusing to edit")

    head = src[:m.start()]
    tail = src[m.start():]

    # Extract body of `location /` for bypass blocks
    depth = 0
    body_start = tail.index("{")
    i = body_start
    while i < len(tail):
        c = tail[i]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                body_end = i
                break
        i += 1
    loc_body = tail[body_start + 1 : body_end]

    # Strip SSO-headers include from bypass copy
    loc_body_for_bypass = "\n".join(
        ln for ln in loc_body.splitlines()
        if "snippets/sso-headers.conf" not in ln
    )

    bypass_blocks = bypass_blocks.replace("__BYPASS_BODY__", loc_body_for_bypass.strip())

    # Insert sso-headers include at top of location /
    new_loc_body = (
        "\n        include snippets/sso-headers.conf;\n"
        + loc_body
    )

    new_tail = "location / {" + new_loc_body + tail[body_end:]

    # Compose: head + sso_server_block + bypass_blocks + new_tail
    out_src = head + sso_server_block + bypass_blocks + new_tail
    return out_src


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("domain")
    ap.add_argument("--bypass", action="append", default=[],
                    help="regex(es) to bypass SSO, e.g. '^/api/' or '^/(webhook|webhook-test)/'")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    path = f"/etc/nginx/sites-available/{args.domain}"
    src = remote_read(path)

    transformed = transform(src, args.bypass, upstream_ws=True)
    if transformed == src:
        print(f"  SKIP {args.domain} — already SSO'd")
        return

    if args.dry_run:
        print(transformed)
        return

    # Backup
    remote_run(f"cp {path} {path}.bak.before-sso-$(date +%Y%m%d-%H%M%S)")
    remote_write(path, transformed)
    # Test nginx
    test = subprocess.run(SSH + ["nginx -t 2>&1"], capture_output=True, text=True)
    if "syntax is ok" not in test.stdout + test.stderr or "test is successful" not in test.stdout + test.stderr:
        print(f"  ERROR nginx -t failed for {args.domain}:")
        print(test.stdout, test.stderr)
        # rollback
        remote_run(f"cp {path}.bak.before-sso-$(date +%Y%m%d-%H%M%S) {path}")
        sys.exit(1)
    remote_run("systemctl reload nginx")
    print(f"  OK   {args.domain} → SSO applied, bypass={args.bypass}")


if __name__ == "__main__":
    main()
