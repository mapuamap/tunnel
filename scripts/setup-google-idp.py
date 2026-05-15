#!/usr/bin/env python3
"""Add Google as an Identity Provider in the dnofagents Keycloak realm.

Idempotent: re-running with the same client-id is safe (updates config).

Usage:
    KC_PASS=<admin_password> python3 setup-google-idp.py \
        --client-id  "xxx.apps.googleusercontent.com" \
        --client-secret "GOCSPX-xxx"

Or set via env vars:
    KC_PASS, GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE = "https://dno.auth.denys.fast"
REALM = "dnofagents"
ADMIN = "admin"


def req(method, path, token, body=None):
    r = urllib.request.Request(
        BASE + path, method=method,
        headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
        data=json.dumps(body).encode() if body else None,
    )
    try:
        resp = urllib.request.urlopen(r)
        return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()


def get_token(password):
    data = urllib.parse.urlencode({
        "grant_type": "password",
        "client_id": "admin-cli",
        "username": ADMIN,
        "password": password,
    }).encode()
    r = urllib.request.Request(
        BASE + "/realms/master/protocol/openid-connect/token",
        data=data,
        headers={"Content-Type": "application/x-www-form-urlencoded"},
    )
    resp = urllib.request.urlopen(r)
    return json.loads(resp.read())["access_token"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--client-id", default=os.environ.get("GOOGLE_CLIENT_ID"))
    parser.add_argument("--client-secret", default=os.environ.get("GOOGLE_CLIENT_SECRET"))
    args = parser.parse_args()

    kc_pass = os.environ.get("KC_PASS")
    if not kc_pass:
        print("ERROR: set KC_PASS env var", file=sys.stderr)
        sys.exit(2)
    if not args.client_id or not args.client_secret:
        print("ERROR: provide --client-id and --client-secret", file=sys.stderr)
        sys.exit(2)

    print("Authenticating with Keycloak admin...")
    token = get_token(kc_pass)

    idp_body = {
        "alias": "google",
        "displayName": "Google",
        "providerId": "google",
        "enabled": True,
        "trustEmail": True,
        "storeToken": False,
        "addReadTokenRoleOnCreate": False,
        "authenticateByDefault": False,
        "firstBrokerLoginFlowAlias": "first broker login",
        "config": {
            "clientId": args.client_id,
            "clientSecret": args.client_secret,
            "syncMode": "IMPORT",
            "useJwksUrl": "true",
        },
    }

    # Check if google IDP already exists
    status, body = req("GET", f"/admin/realms/{REALM}/identity-provider/instances/google", token)
    if status == 200:
        # Update existing
        status, body = req("PUT", f"/admin/realms/{REALM}/identity-provider/instances/google", token, idp_body)
        if status == 204:
            print("  UPDATED  google IDP")
        else:
            print(f"  ERROR updating: HTTP {status}  {body[:300]}", file=sys.stderr)
            sys.exit(1)
    elif status == 404:
        # Create new
        status, body = req("POST", f"/admin/realms/{REALM}/identity-provider/instances", token, idp_body)
        if status == 201:
            print("  CREATED  google IDP")
        else:
            print(f"  ERROR creating: HTTP {status}  {body[:300]}", file=sys.stderr)
            sys.exit(1)
    else:
        print(f"  ERROR checking: HTTP {status}  {body[:300]}", file=sys.stderr)
        sys.exit(1)

    print(f"\nDone. Google login button will appear on:")
    print(f"  {BASE}/realms/{REALM}/account/")
    print(f"\nAll SSO-protected sites now support Google login via oauth2-proxy.")


if __name__ == "__main__":
    main()
