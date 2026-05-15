# Auth: Per-Site Keycloak SSO Toggle + Google IDP

**Date:** 2026-05-15  
**Status:** Approved

## Context

TunnelManager manages nginx configs on VPS (159.223.76.215) for all `*.denys.fast` sites.
oauth2-proxy runs on VPS at `127.0.0.1:4180`, backed by Keycloak realm `dnofagents` at `dno.auth.denys.fast`.

Current state:
- **Basic Auth** (htpasswd) — managed via UI (AuthManage dialog)
- **Keycloak SSO** — applied manually by editing nginx files (add `include snippets/sso-auth.conf`)
- **No auth** — direct proxy

Sites currently with SSO: `aiserver`, `homeassistantmobile`, `loggermm`, `opencodephone`, `tunnel`.

## Task 1: Per-Site Auth Type in UI

### Auth Types

```csharp
public enum AuthType { None, BasicAuth, KeycloakSSO }
```

### Source of Truth

nginx config on VPS is the single source of truth. Detection:
- Has `auth_basic` → `BasicAuth`
- Has `include snippets/sso-auth.conf` → `KeycloakSSO`
- Neither → `None`

### NginxAuthService Changes

New methods:
- `GetAuthType(domain)` → reads nginx config, returns `AuthType`
- `EnableSso(domain)` → adds to nginx config:
  - `include snippets/sso-auth.conf;` at server block level
  - `location @sso_redirect { return 302 https://$host/oauth2/sign_in?rd=...; }` at server block level
  - `include snippets/sso-headers.conf;` inside `location /` block
  - reloads nginx
- `DisableSso(domain)` → removes the above three additions, reloads nginx

Transitions:
- `Basic → SSO`: `RemoveAuth()` then `EnableSso()`
- `SSO → Basic`: `DisableSso()` then `AddAuth()`
- `Any → None`: `RemoveAuth()` or `DisableSso()` depending on current type

### AuthManage.razor Changes

Add at the top of the dialog:
- `MudRadioGroup<AuthType>` with labels: "Без защиты" / "Basic Auth" / "Keycloak SSO"
- On open: call `GetAuthType(domain)` to pre-select current value

Conditional UI below the radio group:
- `None` selected: just a "Сохранить" button (removes auth)
- `BasicAuth` selected: existing username/password fields (unchanged)
- `KeycloakSSO` selected: info text "oauth2-proxy настроен, дополнительная конфигурация не требуется" + "Включить" button

## Task 2: Google IDP in Keycloak

### Script: `scripts/setup-google-idp.py`

Args: `--client-id`, `--client-secret`  
Keycloak URL: read from `KEYCLOAK_ISSUER_URL` env or default `https://dno.auth.denys.fast`  
Admin creds: `KEYCLOAK_ADMIN` / `KEYCLOAK_ADMIN_PASSWORD` env vars (from dnofagents `.env`)

Steps:
1. POST to `/realms/master/protocol/openid-connect/token` → get admin token
2. POST to `/admin/realms/dnofagents/identity-provider/instances` with Google IDP config:
   - `alias: google`
   - `providerId: google`
   - `trustEmail: true`
   - `storeToken: false`
   - `config.clientId`, `config.clientSecret`
3. POST mapper: email → email attribute (`hardcoded-attribute-idp-mapper`)

Result: "Sign in with Google" button appears on Keycloak login page. All SSO-protected sites get Google login automatically.

### User Action Required

Before running the script, user must create a Google OAuth 2.0 app with redirect URI:
```
https://dno.auth.denys.fast/realms/dnofagents/broker/google/endpoint
```

## Files to Change

| File | Change |
|------|--------|
| `Models/AuthType.cs` | New enum |
| `Services/NginxAuthService.cs` | `GetAuthType`, `EnableSso`, `DisableSso` |
| `Components/Pages/Forwards/AuthManage.razor` | Radio group + conditional UI |
| `scripts/setup-google-idp.py` | New script |
