#!/usr/bin/env python3
"""Fix redirectUris of the oauth2-proxy client.

Keycloak doesn't match wildcards in the middle of URLs reliably. Replace with
explicit per-domain entries for the 5 protected vhosts.
"""
import json, os, urllib.request

BASE = "https://dno.auth.denys.fast"
REALM = "dnofagents"
PASS = os.environ["KC_PASS"]
CLIENT_ID = "oauth2-proxy"

DOMAINS = [
    "tunnel", "opencodephone", "aiserver",
    "homeassistantmobile", "loggermm",
]

tok = json.loads(urllib.request.urlopen(urllib.request.Request(
    BASE + "/realms/master/protocol/openid-connect/token",
    data=f"grant_type=password&client_id=admin-cli&username=admin&password={PASS}".encode(),
    headers={"Content-Type": "application/x-www-form-urlencoded"},
)).read())["access_token"]

# Find client UUID
lst = json.loads(urllib.request.urlopen(urllib.request.Request(
    BASE + f"/admin/realms/{REALM}/clients?clientId={CLIENT_ID}",
    headers={"Authorization": "Bearer " + tok}
)).read())
client = lst[0]
cuuid = client["id"]

new_redirects = [f"https://{d}.denys.fast/oauth2/callback" for d in DOMAINS]
client["redirectUris"] = new_redirects

req = urllib.request.Request(
    BASE + f"/admin/realms/{REALM}/clients/{cuuid}",
    method="PUT", data=json.dumps(client).encode(),
    headers={"Authorization": "Bearer " + tok, "Content-Type": "application/json"},
)
resp = urllib.request.urlopen(req)
print(f"  HTTP {resp.status}, redirects now:")
for r in new_redirects:
    print(f"    - {r}")
