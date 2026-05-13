#!/usr/bin/env python3
"""Ensure a user exists in the dnofagents realm with given password + roles.

Usage:
    KC_PASS=<admin> python3 ensure-keycloak-user.py <username> <password> <email>
"""
import json, os, sys, urllib.error, urllib.request

BASE  = "https://dno.auth.denys.fast"
REALM = "dnofagents"
ADMIN_PASS = os.environ["KC_PASS"]
USERNAME = sys.argv[1]
PASSWORD = sys.argv[2]
EMAIL    = sys.argv[3] if len(sys.argv) > 3 else f"{USERNAME}@local"

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

tok = json.loads(urllib.request.urlopen(urllib.request.Request(
    BASE + "/realms/master/protocol/openid-connect/token",
    data=f"grant_type=password&client_id=admin-cli&username=admin&password={ADMIN_PASS}".encode(),
    headers={"Content-Type": "application/x-www-form-urlencoded"},
)).read())["access_token"]

# Find or create user
status, lst, _ = req("GET", f"/admin/realms/{REALM}/users?username={USERNAME}&exact=true", tok)
users = json.loads(lst)
if users:
    uid = users[0]["id"]
    print(f"  EXISTS  {USERNAME}  uid={uid}")
else:
    status, _, hdrs = req("POST", f"/admin/realms/{REALM}/users", tok, {
        "username": USERNAME, "email": EMAIL, "enabled": True, "emailVerified": True,
        "firstName": USERNAME, "lastName": "",
    })
    if status != 201:
        print("  ERROR creating user:", status); sys.exit(1)
    uid = hdrs.get("Location", "").rsplit("/", 1)[1]
    print(f"  CREATED {USERNAME}  uid={uid}")

# Reset password
status, _, _ = req("PUT", f"/admin/realms/{REALM}/users/{uid}/reset-password", tok, {
    "type": "password", "value": PASSWORD, "temporary": False
})
print(f"  PASSWORD SET  ({status})")

# Assign developer realm role (default for the realm)
status, role, _ = req("GET", f"/admin/realms/{REALM}/roles/developer", tok)
if status == 200:
    r = json.loads(role)
    status, _, _ = req("POST", f"/admin/realms/{REALM}/users/{uid}/role-mappings/realm", tok, [
        {"id": r["id"], "name": r["name"]}
    ])
    print(f"  ROLE 'developer' ASSIGNED  ({status})")

print(f"\n  done; user={USERNAME} uid={uid}")
