# Capstone 2 — OAuth 2.0 Authorization Flows

Demonstrates two OAuth 2.0 / OIDC flows against a self-hosted Keycloak instance,
with no external dependencies:

- **Authorization Code + PKCE** — `WebClient`, a user-facing ASP.NET Core MVC
  app, authenticates a human user and calls the API with the `api.read` scope.
- **Client Credentials** — `MachineClient`, a console app, authenticates itself
  (no user) and calls the API with the `api.admin` scope.

Both call the same `ProtectedApi`, which validates tokens issued by Keycloak
and enforces scope-based authorization per endpoint. `ApiRead` and `ApiAdmin`
are two separate authorization policies, each tied to a different scope, so
the project also proves that scope separation is actually enforced and not
just a happy path (see section 5).

## Architecture

```
                 ┌──────────────┐
  browser  ───▶  │  WebClient   │  Authorization Code + PKCE
                 │ (MVC, :5001) │  (human user, cookie session)
                 └──────┬───────┘
                         │ Bearer token (scope: api.read)
                         ▼
                 ┌──────────────┐      ┌───────────────┐
                 │ ProtectedApi │ ◀──  │   Keycloak     │
                 │   (:5101)    │      │   (:8080)      │
                 └──────▲───────┘      └───────────────┘
                         │ Bearer token (scope: api.admin)
                 ┌──────┴───────┐
                 │ MachineClient│  Client Credentials
                 │  (console)   │  (service account, no user)
                 └──────────────┘
```

## Prerequisites

- Docker + Docker Compose
- .NET SDK, targeting `net10.0` (see the SDK/runtime note in section 6 if
  you're on a snap-installed .NET on Linux, that's what this was built and
  debugged against)

## 1. Start Keycloak

```bash
docker compose up -d
```

This starts Keycloak with a Postgres backing store. In practice, the
`realm-export.json` import turned out to be unreliable (see section 6.4), so
the realm in this project was built manually through the admin console
instead. If you're setting this up from scratch, do the same: create the
realm and its clients/scopes/test user by hand rather than relying on the
import.

The realm in this project is named **`oauth-flows`** (not `capstone2`, the
original name, see section 6.5 for why).

What the realm needs, once set up:

- Realm `oauth-flows`
- Client `webclient-pkce` (public, PKCE-enabled), with **only** the
  `api.read` client scope assigned
- Client `machine-client` (confidential, service account enabled), with
  **only** the `api.admin` client scope assigned
- Client `protected-api` (bearer-only resource client, placeholder)
- Client scopes `api.read` and `api.admin`, each with an **Audience
  mapper** (Included Client Audience: `protected-api`) — without this,
  tokens validate but get rejected on audience (see section 6.7)
- Test user `testuser` / `testpassword`

Keycloak admin console: http://localhost:8080 (admin / admin)

## 2. Run the Protected API

```bash
cd ProtectedApi
dotnet run --urls=https://localhost:5101
```

## 3. Run the WebClient (Authorization Code + PKCE)

```bash
cd WebClient
dotnet run --urls=https://localhost:5001
```

Open https://localhost:5001 in a fresh/incognito browser window, click
through to call the API, sign in as `testuser` / `testpassword`, and you
should see a 200 response from `/api/user/profile`, with `subject` set to
the user's actual Keycloak ID.

## 4. Run the MachineClient (Client Credentials)

```bash
cd MachineClient
dotnet run
```

This requests a token directly with `client_id` / `client_secret` (no
browser, no user) and calls `/api/admin/system-status`, which should also
return 200. The secret is read from `MachineClient/appsettings.json`
(gitignored, see section 7), not hardcoded in source.

## 5. Scope-separation proof

Two quick things worth doing to show the authorization is real, not just a
happy path:

- From the WebClient's token (`api.read` scope), try calling
  `/api/admin/system-status` directly — it should return 403, since that
  token doesn't carry `api.admin`.
- From the MachineClient's token (`api.admin` scope), try calling
  `/api/user/profile` — same result, 403, since machine tokens don't carry
  `api.read`.

This sounds trivial, but it actually caught a real misconfiguration during
development (section 6.9), so it's worth treating as a genuine test, not a
formality.

Quick way to test the MachineClient side without touching code, get a token
directly from Keycloak and call the wrong endpoint with it:

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/realms/oauth-flows/protocol/openid-connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=machine-client" \
  -d "client_secret=<your-client-secret>" \
  -d "scope=api.admin" \
  | python3 -c "import sys, json; print(json.load(sys.stdin)['access_token'])")

curl -k -s -o /dev/null -w "Status: %{http_code}\n" \
  -H "Authorization: Bearer $TOKEN" \
  https://localhost:5101/api/user/profile
```

Expected: `Status: 403`.

## 6. Debugging history

This section exists because the real value of this project, beyond "it
works," is everything that *didn't* work on the first, second, or fifth
try, and why. Each entry is a real issue hit while building this, in
roughly chronological order. If you're using this repo to study OAuth/OIDC
or just to understand how these failures present in practice, this is the
part worth reading closely.

### 6.1 .NET SDK/runtime version mismatch

**Symptom:** build or run failures referencing a framework version (e.g.
`8.0.0`) not matching the installed runtime.

**Cause:** the projects originally targeted `net8.0`, but the installed
.NET (via snap on Linux) only had the `10.0.x` runtime available.

**Fix:** retargeted all three `.csproj` files from `net8.0` to `net10.0`.

**Lesson:** `TargetFramework` has to match an actually-installed runtime,
not just a version you intended to use. Check what's installed
(`dotnet --list-runtimes`) before assuming a target framework will work.

### 6.2 Removing "shared framework" package references broke the build (CS0234)

**Symptom:** `CS0234` errors, namespaces like
`Microsoft.AspNetCore.Authentication.JwtBearer` not found.

**Cause:** an incorrect assumption that `JwtBearer` and `OpenIdConnect`
authentication packages ship as part of the ASP.NET Core shared framework
(via `Sdk.Web`) and don't need an explicit `PackageReference`. They don't,
they're separate NuGet packages.

**Fix:** restored the explicit `PackageReference` entries.

**Lesson:** don't assume what's "built in" to a shared framework without
checking; when in doubt, keep the explicit package reference.

### 6.3 dotnet CLI itself broken after a snap packaging sync issue

**Symptom:** the `dotnet` CLI failing to run at all, SDK `10.0.113`
expecting runtime `10.0.13`, only `10.0.12` installed. `sudo snap refresh
dotnet` reported "no available updates," confirming the snap itself was in
a broken, out-of-sync state rather than just missing an update.

**Fix:** `sudo snap remove dotnet && sudo snap install dotnet --classic`,
which landed on `10.0.3`. Confirmed this was still compatible with
`TargetFramework net10.0` (only the major version has to match).

**Lesson:** snap-distributed .NET can get its SDK and runtime components
out of sync with each other in a way `snap refresh` won't fix. A full
remove/reinstall is sometimes the only way out.

### 6.4 `invalid_scope` on first login attempt

**Symptom:** Keycloak rejecting the requested scopes (`openid profile
api.read`) entirely on login.

**Cause:** turned out to be much deeper than a scope typo. The
`realm-export.json` import wasn't reliably applying `defaultClientScopes`
during import, and on closer inspection, the realm was missing Keycloak's
own standard scopes (`profile`, `email`, `roles`) entirely, meaning the
import had left the realm in a broken, incomplete state.

**Fix:** abandoned the JSON import approach. Deleted the realm and rebuilt
it entirely by hand through the admin console: clients, client scopes, and
test user, all created manually.

**Lesson:** `--import-realm` / realm JSON import is convenient when it
works, but failures are silent and the resulting realm can look mostly
fine while missing critical pieces. If something that should obviously
work doesn't, check the realm's actual contents in the admin console
rather than trusting the import succeeded.

### 6.5 Realm name drift (`capstone2` → `oauth-flows`)

**Symptom:** `{"error":"Realm does not exist"}` when a client tries to
reach a realm by name.

**Cause:** the realm was accidentally renamed during manual setup, twice,
across two separate sessions of working on this project (Realm settings →
General → Realm ID is easy to edit by accident while clicking through
other settings).

**Fix:** the first time, renamed it back to `capstone2`. The second time it
drifted again, the realm name was left as `oauth-flows` and every project
config (`WebClient/appsettings.json`, `ProtectedApi/appsettings.json`,
`MachineClient/Program.cs`) was updated to match it instead of continuing
to fight the rename.

**Lesson:** pick one source of truth for a config value like a realm name
and don't let it drift between "what the docs say" and "what's actually in
Keycloak." When they disagree, it's nearly always faster to update the
docs/config to match reality than to keep forcing reality back to match
old docs.

### 6.6 HTTP 500 — SSL `UntrustedRoot`

**Symptom:** `AuthenticationException: The remote certificate is invalid
because of errors in the certificate chain: UntrustedRoot` when WebClient
(and later MachineClient) called ProtectedApi over HTTPS.

**Cause:** ProtectedApi runs on ASP.NET Core's self-signed local dev
certificate. `dotnet dev-certs https --trust` is meant to register that
cert with the OS trust store, but doesn't do so reliably on Linux.

**Fix:** added a dev-only certificate bypass
(`ServerCertificateCustomValidationCallback =
HttpClientHandler.DangerousAcceptAnyServerCertificateValidator`) on the
named `HttpClient` used to call ProtectedApi, in both WebClient and
MachineClient. Explicitly commented in both places as dev-only, never
acceptable in production, where a real CA-issued certificate would make
this unnecessary.

**Lesson:** "trust this dev cert" tooling is a convenience that doesn't
hold on every OS. A scoped, clearly-commented bypass on a specific
`HttpClient` is safer than disabling TLS validation more broadly, and the
comment matters so nobody mistakes it for acceptable in a real deployment.

### 6.7 401 Unauthorized after the SSL fix

**Symptom:** SSL handshake succeeding, but the API call returning 401.

**Cause:** the JSON realm import (section 6.4) had included an Audience
protocol mapper on the relevant client scopes, but the manually recreated
clients didn't have one. Without it, tokens don't carry the `aud` claim
ProtectedApi expects, so audience validation fails.

**Fix:** added an Audience mapper (Included Client Audience:
`protected-api`) to the `api.read` and `api.admin` client scopes.

**Lesson:** when rebuilding something manually that was previously defined
declaratively (like a realm export), it's easy to miss a piece that isn't
obviously visible in the main client settings screens, protocol mappers
live a layer deeper than most of the settings people check first.

### 6.8 Persistent 401 even after the audience fix

**Symptom:** still 401, even in a fresh incognito window, even after the
audience mapper was added.

**Diagnosis:** enabled `Debug`-level logging on
`Microsoft.AspNetCore.Authentication` in `ProtectedApi/appsettings.json`,
which surfaced the real error: `IDX10204: Unable to validate issuer,
ValidIssuer is null`.

**Cause:** ProtectedApi's OpenID Connect configuration manager had cached
a *failed* metadata fetch from earlier, before the realm existed properly
(back during section 6.4/6.5). ASP.NET Core fetches and caches OIDC
discovery metadata (the `.well-known/openid-configuration` document) once
at startup, with a background refresh interval, not on every request. A
cached failure doesn't automatically retry just because the underlying
realm got fixed.

**Fix:** fully restarted the ProtectedApi process, forcing a fresh
metadata fetch against the now-correctly-configured realm.

**Lesson, and this one repeats later (section 6.11):** this setup does not
hot-reload. A running process keeps whatever OIDC metadata (and, as it
turns out, whatever code) it loaded at startup. Any change on the
Keycloak side, or any code change in the API itself, requires a full stop
and restart to actually take effect, not just a retry from the client
side.

### 6.9 False negative on the scope-separation proof

**Symptom:** while running the proof in section 5, a MachineClient token
(scope `api.admin`) got a 200 on `/api/user/profile`, which requires
`api.read`. Scope enforcement looked broken.

**Diagnosis:** the `ScopeHandler` authorization logic itself was correct
(checks the token's `scope` claim for the required value). The bug was in
Keycloak client configuration, not code: both `api.read` and `api.admin`
client scopes had been assigned as **Default** scopes on both clients.
A "Default" client scope in Keycloak is included in every token issued to
that client automatically, regardless of what's requested in the `scope`
parameter at login/token time. So `machine-client`'s token carried
`api.admin api.read` even though only `api.admin` was requested.

**Fix:** in each client's **Client scopes** tab, removed the scope that
client shouldn't have, `api.read` removed from `machine-client`,
`api.admin` removed from `webclient-pkce`, so each client can only ever
get the one scope it's meant to have.

**Lesson:** "Default" vs "Optional" client scope assignment in Keycloak is
a meaningful security boundary, not just a convenience setting. A scope
set as Default on a client will show up in every token for that client no
matter what the `scope` request parameter says. This is exactly the kind
of subtle misconfiguration a scope-separation test is designed to catch,
and did.

### 6.10 `"subject":"unknown"` in the API response

**Symptom:** the Authorization Code + PKCE flow worked end-to-end (200
response), but the `subject` field always read `"unknown"` instead of the
actual user ID.

**Cause:** the code looked up the claim via `User.FindFirst("sub")`.
ASP.NET Core's JWT Bearer handler, by default, remaps several standard JWT
claim types on the way in, including renaming `sub` to the long
`ClaimTypes.NameIdentifier` URI
(`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`).
So the claim was present in the token and in the resulting
`ClaimsPrincipal`, just not under the short name the code was looking for.

**Fix:** set `options.MapInboundClaims = false;` in the `AddJwtBearer`
configuration in `ProtectedApi/Program.cs`, which stops the remapping
entirely, so claims keep their original names as issued by Keycloak
(`sub` stays `sub`). This was preferred over a defensive fallback check in
the controller, since it fixes the behavior at the source for every claim
the API might read in the future, not just this one.

**Lesson:** when a claim seems to be "missing" despite clearly being in
the decoded token, check for claim type remapping before assuming the
token itself is malformed, ASP.NET Core's JWT handler does this
transformation silently by default.

### 6.11 401 again, immediately after the claims fix

**Symptom:** right after adding `MapInboundClaims = false` and saving the
file, the next test still returned 401.

**Cause:** same root lesson as section 6.8, the already-running
`ProtectedApi` process was still serving the old build. Saving a `.cs`
file doesn't hot-reload a running `dotnet run` process in this setup.

**Fix:** stopped the process (`Ctrl+C`) and started it again, which
compiled and loaded the fix. Confirmed working immediately after, with
`subject` showing the real Keycloak user ID.

**Lesson, stated plainly since it came up twice:** after any change to
`Program.cs` or `appsettings.json` in a project like this, always fully
stop and restart the process before re-testing. Don't assume a change
took effect just because the file was saved.

## 7. Secrets and configuration

`MachineClient`'s client secret is read from `MachineClient/appsettings.json`
at runtime, which is **gitignored** and never committed. A
`MachineClient/appsettings.example.json` template (with a placeholder
value) is committed instead, so anyone cloning this repo knows the shape
of the file they need to create locally.

## 8. Write-up: when each flow applies

**Authorization Code + PKCE** — use when a human user is present and needs
to grant consent (or at least authenticate) via a browser redirect. PKCE
(`code_challenge` / `code_verifier`) replaces the need for a client
secret, which is why it's the correct choice for public clients that can't
keep a secret confidential, a browser-based SPA, a mobile app, or (as
here) a server-rendered web app acting on behalf of a logged-in user.

**Client Credentials** — use when there's no human user at all: a backend
service, a scheduled job, or a machine-to-machine integration
authenticating as itself. The client secret here is held by the
confidential client (the service), never exposed to a browser, which is
the trust boundary that makes this flow safe only for server-side/trusted
clients.

**Security trade-offs:**

- PKCE protects the Authorization Code flow from interception/replay on a
  public client, but still relies on the redirect URI and browser session
  being trustworthy, an attacker who controls the device can still hijack
  tokens post-issuance.
- Client Credentials has no user context at all, so authorization must
  come entirely from what the service account itself is permitted to do
  (its scopes/roles), this is why scoping `machine-client` tightly to only
  `api.admin` matters (and why the Default-scope leak in section 6.9 was
  worth catching): a compromised service credential is as dangerous as its
  assigned scope, nothing more and nothing less.
- Both flows benefit from short-lived access tokens (5 minutes here) and
  from validating `aud` (audience) in addition to the signature, so a
  token issued for one resource server can't be replayed against another.

## 9. Possible next steps

Ideas for extending this project further:

- Add a role- or group-based authorization layer on top of scopes, to
  show RBAC and scope-based authorization working together.
- Add a refresh-token flow to WebClient, currently it only handles the
  initial token issuance.
- Containerize `WebClient`, `ProtectedApi`, and `MachineClient` themselves
  (not just Keycloak) so the whole stack comes up with one
  `docker compose up`.
- Add automated tests for the scope-separation proof (section 5) instead
  of running it manually via curl.