# Auth: Per-Site Keycloak SSO Toggle + Google IDP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-site Keycloak SSO toggle to AuthManage dialog and create a script to configure Google as an Identity Provider in Keycloak.

**Architecture:** `NginxAuthService` gets `GetAuthType(domain)`, `EnableSso(domain)`, `DisableSso(domain)` methods that SSH into VPS and read/write nginx config files. `AuthManage.razor` gains a `MudRadioGroup<AuthType>` to switch between None / BasicAuth / KeycloakSSO. A standalone Python script handles Google IDP registration in Keycloak via Admin REST API.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, Renci.SshNet (via SshService), Python 3 (stdlib only), Keycloak Admin REST API

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `src/TunnelManager.Web/Models/AuthType.cs` | Enum: None / BasicAuth / KeycloakSSO |
| Modify | `src/TunnelManager.Web/Services/NginxAuthService.cs` | Add GetAuthType, EnableSso, DisableSso |
| Modify | `src/TunnelManager.Web/Services/NginxService.cs` | Line 172: detect SSO in HasAuth flag |
| Modify | `src/TunnelManager.Web/Components/Pages/Forwards/AuthManage.razor` | Radio group + SSO UI |
| Create | `scripts/setup-google-idp.py` | Register Google IDP in Keycloak |

---

## Task 1: AuthType enum

**Files:**
- Create: `src/TunnelManager.Web/Models/AuthType.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace TunnelManager.Web.Models;

public enum AuthType
{
    None,
    BasicAuth,
    KeycloakSSO
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
cd H:/aps/tunnelling
dotnet build src/TunnelManager.Web/TunnelManager.Web.csproj --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/TunnelManager.Web/Models/AuthType.cs
git commit -m "feat: add AuthType enum (None/BasicAuth/KeycloakSSO)"
```

---

## Task 2: NginxAuthService — GetAuthType

**Files:**
- Modify: `src/TunnelManager.Web/Services/NginxAuthService.cs`

Add `GetAuthType(string domain)` after the existing `HasAuth()` method (after line 47).

- [ ] **Step 1: Add the method**

In `NginxAuthService.cs`, add after the closing brace of `HasAuth()` (after line 47):

```csharp
public AuthType GetAuthType(string domain)
{
    _loggerMM?.Debug("NginxAuthService", "GetAuthType", $"Detecting auth type for domain: {domain}",
        @params: new { domain },
        tags: new[] { "nginx", "auth" });

    var configPath = $"{ConfigDir}/{domain}";
    if (!_sshService.FileExists(configPath))
        return AuthType.None;

    var content = _sshService.ReadFile(configPath);

    if (Regex.IsMatch(content, @"include\s+snippets/sso-auth\.conf"))
        return AuthType.KeycloakSSO;

    if (Regex.IsMatch(content, @"auth_basic"))
        return AuthType.BasicAuth;

    return AuthType.None;
}
```

- [ ] **Step 2: Add `using TunnelManager.Web.Models;` if not present**

Check line 3 of the file — it already has `using TunnelManager.Web.Models;`. No change needed.

- [ ] **Step 3: Build**

```bash
dotnet build src/TunnelManager.Web/TunnelManager.Web.csproj --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/TunnelManager.Web/Services/NginxAuthService.cs
git commit -m "feat: add NginxAuthService.GetAuthType detecting Basic/SSO/None"
```

---

## Task 3: NginxAuthService — EnableSso / DisableSso

**Files:**
- Modify: `src/TunnelManager.Web/Services/NginxAuthService.cs`

Add two methods after `GetAuthType()`. Also update `NginxService.cs:172` so the `HasAuth` flag covers SSO too.

- [ ] **Step 1: Add EnableSso method**

Add after `GetAuthType()`:

```csharp
public void EnableSso(string domain)
{
    _loggerMM?.Info("NginxAuthService", "EnableSso", $"Enabling Keycloak SSO for domain: {domain}",
        @params: new { domain },
        tags: new[] { "nginx", "auth", "sso", "add" });

    try
    {
        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
            throw new FileNotFoundException($"Config not found: {domain}");

        var content = _sshService.ReadFile(configPath);

        // Remove Basic Auth if present before switching
        content = Regex.Replace(content, @"\s*# Basic HTTP Authentication\s*\n", "");
        content = Regex.Replace(content, @"\s*auth_basic\s+""[^""]*"";\s*\n", "");
        content = Regex.Replace(content, @"\s*auth_basic_user_file\s+[^;]+;\s*\n", "");

        // Add sso-headers inside location / (before proxy_pass)
        content = Regex.Replace(
            content,
            @"(location\s+/\s*\{[^}]*?)(proxy_pass[^;]+;)",
            "$1        include snippets/sso-headers.conf;\n\n        $2",
            RegexOptions.Singleline
        );

        // Add sso-auth.conf include + @sso_redirect before the first location block
        var ssoBlock = @"
    include snippets/sso-auth.conf;
    location @sso_redirect {
        return 302 https://$host/oauth2/sign_in?rd=$scheme://$host$request_uri;
    }

";
        content = Regex.Replace(
            content,
            @"(\s*location\s+/\s*\{)",
            ssoBlock + "$1",
            RegexOptions.Singleline
        );

        _sshService.WriteFile(configPath, content);

        // Remove htpasswd file if it exists
        var htpasswdPath = $"/etc/nginx/.htpasswd_{domain}";
        _sshService.DeleteFile(htpasswdPath);

        TestAndReloadNginx();

        _loggerMM?.Info("NginxAuthService", "EnableSso", $"Keycloak SSO enabled for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "auth", "sso", "add" });
    }
    catch (Exception ex)
    {
        _loggerMM?.Error("NginxAuthService", "EnableSso", $"Failed to enable SSO for domain {domain}: {ex.Message}",
            exception: ex,
            @params: new { domain },
            tags: new[] { "nginx", "auth", "sso", "error" });
        throw;
    }
}
```

- [ ] **Step 2: Add DisableSso method**

Add after `EnableSso()`:

```csharp
public void DisableSso(string domain)
{
    _loggerMM?.Info("NginxAuthService", "DisableSso", $"Disabling Keycloak SSO for domain: {domain}",
        @params: new { domain },
        tags: new[] { "nginx", "auth", "sso", "remove" });

    try
    {
        var configPath = $"{ConfigDir}/{domain}";
        if (!_sshService.FileExists(configPath))
            return;

        var content = _sshService.ReadFile(configPath);

        // Remove include snippets/sso-auth.conf line
        content = Regex.Replace(content, @"\s*include\s+snippets/sso-auth\.conf;\s*\n", "\n");

        // Remove location @sso_redirect { ... } block
        content = Regex.Replace(content, @"\s*location\s+@sso_redirect\s*\{[^}]*\}\s*\n", "\n",
            RegexOptions.Singleline);

        // Remove include snippets/sso-headers.conf line
        content = Regex.Replace(content, @"\s*include\s+snippets/sso-headers\.conf;\s*\n", "\n");

        _sshService.WriteFile(configPath, content);
        TestAndReloadNginx();

        _loggerMM?.Info("NginxAuthService", "DisableSso", $"Keycloak SSO disabled for domain: {domain}",
            @params: new { domain },
            tags: new[] { "nginx", "auth", "sso", "remove" });
    }
    catch (Exception ex)
    {
        _loggerMM?.Error("NginxAuthService", "DisableSso", $"Failed to disable SSO for domain {domain}: {ex.Message}",
            exception: ex,
            @params: new { domain },
            tags: new[] { "nginx", "auth", "sso", "error" });
        throw;
    }
}
```

- [ ] **Step 3: Update NginxService.cs line 172 to detect SSO**

Open `src/TunnelManager.Web/Services/NginxService.cs`. Find line 172:
```csharp
config.HasAuth = Regex.IsMatch(content, @"auth_basic");
```

Replace with:
```csharp
config.HasAuth = Regex.IsMatch(content, @"auth_basic") || Regex.IsMatch(content, @"include\s+snippets/sso-auth\.conf");
```

- [ ] **Step 4: Build**

```bash
dotnet build src/TunnelManager.Web/TunnelManager.Web.csproj --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/TunnelManager.Web/Services/NginxAuthService.cs \
        src/TunnelManager.Web/Services/NginxService.cs
git commit -m "feat: add EnableSso/DisableSso to NginxAuthService, update HasAuth to detect SSO"
```

---

## Task 4: AuthManage.razor — radio group + SSO UI

**Files:**
- Modify: `src/TunnelManager.Web/Components/Pages/Forwards/AuthManage.razor`

Replace the entire file content with the version below. Key changes:
- `_hasAuth: bool` → `_currentAuthType: AuthType` + `_selectedAuthType: AuthType`
- `MudRadioGroup` at the top with three options
- Conditional content section per auth type
- `ApplyAuthType()` handles transitions between types

- [ ] **Step 1: Replace AuthManage.razor**

```razor
@using TunnelManager.Web.Models
@using TunnelManager.Web.Components.Shared
@using Logger_MM.Agent
@inject NginxAuthService NginxAuthService
@inject IDialogService DialogService
@inject ISnackbar Snackbar
@inject LoggerMMAgent? LoggerMM
@inject AuthenticationStateProvider AuthStateProvider

<MudDialog>
    <DialogContent>
        <MudText Typo="Typo.h6" Class="mb-4">Auth for @Domain</MudText>

        <MudRadioGroup @bind-Value="_selectedAuthType" Class="mb-4">
            <MudRadio Value="AuthType.None" Color="Color.Default">Без защиты</MudRadio>
            <MudRadio Value="AuthType.BasicAuth" Color="Color.Primary">Basic Auth</MudRadio>
            <MudRadio Value="AuthType.KeycloakSSO" Color="Color.Secondary">Keycloak SSO</MudRadio>
        </MudRadioGroup>

        @if (_selectedAuthType == AuthType.BasicAuth)
        {
            @if (_currentAuthType == AuthType.BasicAuth && _users.Count > 0)
            {
                <MudAlert Severity="Severity.Info" Class="mb-3">
                    Активен Basic Auth. Пользователи: @string.Join(", ", _users)
                </MudAlert>
            }
            <MudTextField @bind-Value="_username" Label="Username" Variant="Variant.Outlined" Class="mb-2" />
            <MudTextField @bind-Value="_password" Label="Password" Variant="Variant.Outlined"
                          InputType="InputType.Password" Class="mb-2" />
            <MudTextField @bind-Value="_realm" Label="Realm" Variant="Variant.Outlined"
                          HelperText="Default: Restricted Access" Class="mb-2" />
            <MudButton Color="Color.Primary" Variant="Variant.Filled" OnClick="ApplyAuthType">
                @(_currentAuthType == AuthType.BasicAuth ? "Обновить пароль" : "Включить Basic Auth")
            </MudButton>
        }
        else if (_selectedAuthType == AuthType.KeycloakSSO)
        {
            @if (_currentAuthType == AuthType.KeycloakSSO)
            {
                <MudAlert Severity="Severity.Success" Class="mb-3">
                    Keycloak SSO активен. Вход через dno.auth.denys.fast
                </MudAlert>
            }
            else
            {
                <MudAlert Severity="Severity.Info" Class="mb-3">
                    oauth2-proxy уже настроен на сервере. Нажми "Включить" — nginx начнёт требовать SSO-вход.
                </MudAlert>
            }
            <MudButton Color="Color.Secondary" Variant="Variant.Filled" OnClick="ApplyAuthType"
                       Disabled="_currentAuthType == AuthType.KeycloakSSO">
                @(_currentAuthType == AuthType.KeycloakSSO ? "SSO активен" : "Включить SSO")
            </MudButton>
        }
        else
        {
            @if (_currentAuthType != AuthType.None)
            {
                <MudAlert Severity="Severity.Warning" Class="mb-3">
                    Сайт станет доступен без авторизации.
                </MudAlert>
                <MudButton Color="Color.Error" Variant="Variant.Filled" OnClick="ApplyAuthType">
                    Убрать защиту
                </MudButton>
            }
            else
            {
                <MudAlert Severity="Severity.Default" Class="mb-3">
                    Сайт открыт без авторизации.
                </MudAlert>
            }
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Закрыть</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] MudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public string Domain { get; set; } = string.Empty;

    private AuthType _currentAuthType = AuthType.None;
    private AuthType _selectedAuthType = AuthType.None;
    private List<string> _users = new();
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _realm = "Restricted Access";

    protected override void OnInitialized()
    {
        LoadAuthStatus();
    }

    private void LoadAuthStatus()
    {
        try
        {
            _currentAuthType = NginxAuthService.GetAuthType(Domain);
            _selectedAuthType = _currentAuthType;
            if (_currentAuthType == AuthType.BasicAuth)
                _users = NginxAuthService.GetAuthUsers(Domain);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Ошибка загрузки статуса: {ex.Message}", Severity.Error);
        }
    }

    private async Task ApplyAuthType()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var user = authState.User.Identity?.Name ?? "unknown";

        try
        {
            switch (_selectedAuthType)
            {
                case AuthType.None:
                    if (_currentAuthType == AuthType.BasicAuth)
                        NginxAuthService.RemoveAuth(Domain);
                    else if (_currentAuthType == AuthType.KeycloakSSO)
                        NginxAuthService.DisableSso(Domain);
                    Snackbar.Add("Защита убрана", Severity.Success);
                    break;

                case AuthType.BasicAuth:
                    if (string.IsNullOrWhiteSpace(_username) || string.IsNullOrWhiteSpace(_password))
                    {
                        Snackbar.Add("Username и password обязательны", Severity.Warning);
                        return;
                    }
                    if (_currentAuthType == AuthType.KeycloakSSO)
                        NginxAuthService.DisableSso(Domain);
                    if (_currentAuthType == AuthType.BasicAuth)
                        NginxAuthService.UpdateAuth(Domain, _username, _password);
                    else
                        NginxAuthService.AddAuth(Domain, _username, _password, _realm);
                    Snackbar.Add(_currentAuthType == AuthType.BasicAuth ? "Пароль обновлён" : "Basic Auth включён", Severity.Success);
                    break;

                case AuthType.KeycloakSSO:
                    if (_currentAuthType == AuthType.BasicAuth)
                        NginxAuthService.RemoveAuth(Domain);
                    NginxAuthService.EnableSso(Domain);
                    Snackbar.Add("Keycloak SSO включён", Severity.Success);
                    break;
            }

            LoggerMM?.UserAction("AuthManage", "ApplyAuthType",
                $"Auth changed for {Domain}: {_currentAuthType} → {_selectedAuthType}",
                user: user,
                @params: new { domain = Domain, from = _currentAuthType.ToString(), to = _selectedAuthType.ToString() },
                tags: new[] { "auth", "change", "user-action" });

            _username = string.Empty;
            _password = string.Empty;
            LoadAuthStatus();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Ошибка: {ex.Message}", Severity.Error);
            LoggerMM?.Error("AuthManage", "ApplyAuthType",
                $"Failed to change auth for {Domain}: {ex.Message}",
                user: user,
                exception: ex,
                @params: new { domain = Domain },
                tags: new[] { "auth", "error" });
        }
    }

    private void Cancel() => MudDialog.Close();
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/TunnelManager.Web/TunnelManager.Web.csproj --no-restore -q
```

Expected: `Build succeeded.`

- [ ] **Step 3: Deploy and manually verify**

```bash
# In H:/aps/tunnelling
./deploy  # or however the project is deployed
```

Open TunnelManager UI → Forwards → выбери любой форвард → кнопка Auth.  
Должен появиться RadioGroup с тремя вариантами. У сайтов с SSO (aiserver, loggermm, opencodephone) должен быть выбран "Keycloak SSO".

- [ ] **Step 4: Commit**

```bash
git add src/TunnelManager.Web/Components/Pages/Forwards/AuthManage.razor
git commit -m "feat: add Keycloak SSO toggle to AuthManage dialog"
```

---

## Task 5: setup-google-idp.py script

**Files:**
- Create: `scripts/setup-google-idp.py`

The script follows the exact same pattern as `scripts/create-keycloak-clients.py` (stdlib only, no pip).

- [ ] **Step 1: Create the script**

```python
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
        return resp.status, resp.read().decode(), dict(resp.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(), dict(e.headers)


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
    status, body, _ = req("GET", f"/admin/realms/{REALM}/identity-provider/instances/google", token)
    if status == 200:
        # Update existing
        status, body, _ = req("PUT", f"/admin/realms/{REALM}/identity-provider/instances/google", token, idp_body)
        if status == 204:
            print("  UPDATED  google IDP")
        else:
            print(f"  ERROR updating: HTTP {status}  {body[:300]}", file=sys.stderr)
            sys.exit(1)
    elif status == 404:
        # Create new
        status, body, _ = req("POST", f"/admin/realms/{REALM}/identity-provider/instances", token, idp_body)
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
```

- [ ] **Step 2: Verify the script syntax**

```bash
python3 -c "import ast; ast.parse(open('scripts/setup-google-idp.py').read()); print('OK')"
```

Expected: `OK`

- [ ] **Step 3: Dry-run check (no KC_PASS = early exit)**

```bash
python3 scripts/setup-google-idp.py --client-id test --client-secret test
```

Expected: `ERROR: set KC_PASS env var`

- [ ] **Step 4: Commit**

```bash
git add scripts/setup-google-idp.py
git commit -m "feat: add setup-google-idp.py script for Keycloak Google IDP"
```

---

## Self-Review

**Spec coverage:**
- ✅ Per-site auth type toggle (None/Basic/SSO) → Tasks 1–4
- ✅ Configurable in UI (AuthManage dialog) → Task 4
- ✅ Google IDP in Keycloak → Task 5
- ✅ Transitions between auth types (Basic→SSO, SSO→Basic, etc.) → Task 3+4
- ✅ HasAuth flag in ForwardConfig correctly reflects SSO → Task 3 Step 3

**Placeholder scan:** Clean — all steps have code.

**Type consistency:**
- `AuthType.None/BasicAuth/KeycloakSSO` — used consistently in Tasks 1, 2, 3, 4
- `GetAuthType()` returns `AuthType` — used in `LoadAuthStatus()` in Task 4
- `EnableSso(domain)` / `DisableSso(domain)` — defined Task 3, used Task 4
- `RemoveAuth(domain)` — existing method, used Task 4 for None transition ✅
