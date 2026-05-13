#!/usr/bin/env python3
"""Create the SSO OIDC clients in the dnofagents realm via Admin API.

Idempotent: if a client already exists it's reused (we still fetch its
current secret). Writes /tmp/sso-clients.json with {clientId: {uuid, secret, redirects}}.

Usage:
    KC_PASS=<password> python3 create-keycloak-clients.py
"""
import json
import os
import sys
import urllib.error
import urllib.request

BASE = "https://dno.auth.denys.fast"
REALM = "dnofagents"
ADMIN = "admin"
PASS = os.environ.get("KC_PASS")
if not PASS:
    print("ERROR: set KC_PASS env var", file=sys.stderr); sys.exit(2)


def req(method, path, token, body=None):
    r = urllib.request.Request(
        BASE + path, method=method,
        headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
        data=json.dumps(body).encode() if body else None,
    )
    try:
        resp = urllib.request.urlopen(r)
        return resp.status, resp.read().decode(), dict(resp.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(), dict(e.headers)


tok_r = urllib.request.urlopen(urllib.request.Request(
    BASE + "/realms/master/protocol/openid-connect/token",
    data=f"grant_type=password&client_id=admin-cli&username={ADMIN}&password={PASS}".encode(),
    headers={"Content-Type": "application/x-www-form-urlencoded"},
))
TOKEN = json.loads(tok_r.read())["access_token"]

CLIENTS = [
    # (clientId, redirectUris[])
    ("oauth2-proxy",   ["https://*.denys.fast/oauth2/callback"]),
    ("gpu-web",        ["https://gpu.denys.fast/signin-oidc",
                        "https://server1gpu.denys.fast/signin-oidc"]),
    ("simuqlator",     ["https://simuqlator.denys.fast/signin-oidc"]),
    ("denys-ai",       ["https://tg.denys.fast/signin-oidc"]),
    ("tunnel-manager", ["https://tunnel.denys.fast/signin-oidc"]),
    ("proxmox",        ["https://server1.denys.fast/"]),
    ("homeassistant",  ["https://homeassistantmobile.denys.fast/auth/external/callback"]),
]

out = {}
for cid, redirects in CLIENTS:
    body = {
        "clientId": cid, "enabled": True, "protocol": "openid-connect",
        "publicClient": False, "standardFlowEnabled": True,
        "directAccessGrantsEnabled": False, "serviceAccountsEnabled": False,
        "redirectUris": redirects, "webOrigins": ["+"],
        "attributes": {
            "pkce.code.challenge.method": "S256",
            "post.logout.redirect.uris": "+",
        },
    }
    status, txt, headers = req("POST", f"/admin/realms/{REALM}/clients", TOKEN, body)
    if status == 201:
        cuuid = headers.get("Location", "").rsplit("/", 1)[1]
        action = "CREATED"
    elif status == 409:
        _, lst, _ = req("GET", f"/admin/realms/{REALM}/clients?clientId={cid}", TOKEN)
        cuuid = json.loads(lst)[0]["id"]
        action = "EXISTS"
    else:
        print(f"  ERROR    {cid}  HTTP {status}  {txt[:200]}")
        continue
    _, sec, _ = req("GET", f"/admin/realms/{REALM}/clients/{cuuid}/client-secret", TOKEN)
    secret = json.loads(sec).get("value", "")
    out[cid] = {"uuid": cuuid, "secret": secret, "redirects": redirects}
    print(f"  {action:7}  {cid}  uuid={cuuid}  secret={secret[:8]}...")

json.dump(out, open("/tmp/sso-clients.json", "w"), indent=2)
print(f"\n  Wrote /tmp/sso-clients.json with {len(out)} clients")
