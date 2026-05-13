#!/usr/bin/env python3
"""Add an audience mapper to the oauth2-proxy client.

Keycloak's default access tokens have `aud: ["account"]`. oauth2-proxy requires
the audience to match its own client_id. Adding an oidc-audience-mapper makes
Keycloak include `oauth2-proxy` in the aud claim.
"""
import json, os, urllib.request

BASE = "https://dno.auth.denys.fast"
REALM = "dnofagents"
CLIENT_ID = "oauth2-proxy"
PASS = os.environ["KC_PASS"]

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
cuuid = lst[0]["id"]

mapper = {
    "name": "audience-self",
    "protocol": "openid-connect",
    "protocolMapper": "oidc-audience-mapper",
    "consentRequired": False,
    "config": {
        "included.client.audience": CLIENT_ID,
        "id.token.claim": "false",
        "access.token.claim": "true",
    },
}

req = urllib.request.Request(
    BASE + f"/admin/realms/{REALM}/clients/{cuuid}/protocol-mappers/models",
    method="POST", data=json.dumps(mapper).encode(),
    headers={"Authorization": "Bearer " + tok, "Content-Type": "application/json"},
)
try:
    resp = urllib.request.urlopen(req)
    print(f"  HTTP {resp.status} — audience mapper added")
except urllib.error.HTTPError as e:
    print(f"  HTTP {e.code} — {e.read().decode()[:200]}")
