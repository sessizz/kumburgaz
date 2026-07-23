# Hardcoded Secrets Analysis Results

No vulnerabilities found.

## Recon Notes (for audit trail)

Scanned all publicly accessible code paths in this ASP.NET Core MVC monolith:

- `wwwroot/js/*.js` (site.js, mobile.js, mobile-push.js, document-preview.js, sw.js)
- `wwwroot/lib/**` (third-party libraries — jQuery validation, no custom secrets)
- All `Views/**/*.cshtml` and `Areas/Mobile/Views/**/*.cshtml` (Razor views rendered to the browser, including inline `<script>` blocks)
- Root-level JSON/config files (`appsettings.json`, `dotnet-tools.json`, `Properties/launchSettings.json`) — reviewed only to confirm they are server-side only and not served statically to clients

**Searches performed**:
1. High-confidence regex patterns for AWS, Google, GitHub, Slack, Stripe, SendGrid, OpenAI/Anthropic keys, private-key headers, and credential-embedded connection strings (`user:pass@host`) — no matches in any file.
2. Variable-name pattern search (`api_key`, `secret`, `token`, `password`, `private_key`, `client_secret`, `signing_key`, `connection_string`, `vapid`) across all `.js`/`.cshtml`/`.ts`/`.html` files.

**Notable non-findings** (reviewed and confirmed NOT vulnerable):

- **`wwwroot/js/mobile-push.js` / `Areas/Mobile/Views/Shared/_MobileLayout.cshtml`**: `data-public-key="@PushSender.PublicKey"` — this is the VAPID **public** key for Web Push, intentionally exposed to the browser (`pushManager.subscribe({ applicationServerKey: publicKey })`). Public keys are designed for client-side use; not a secret. The corresponding `Push:PrivateKey` in `appsettings.json` stays server-side only and is never rendered into any view or script.
- **`Views/Reports/LoginCredentials.cshtml`, `LoginCredentialDetail.cshtml`, `Views/Accounts/Edit.cshtml`**: render `@Model.Password` / `@Model.MobilePassword` (the resident's plaintext mobile PIN) into server-rendered HTML. This is dynamic per-request data pulled from the database via a controller action, not a hardcoded secret literal in source code — out of scope for this skill (it is a sensitive-data-exposure/authorization concern instead; already flagged in `sast/architecture.md` under "Notable Areas for Deeper Review" and better suited to the IDOR/missing-auth checks).
- **`appsettings.json`**: contains a local Postgres connection string (`Password=postgres`) and blank `Push:PrivateKey`/`Push:PublicKey` placeholders. This file is server-side configuration only — it is not served as a static asset and is not bundled into any client-side script. Out of scope per this skill's public-accessibility criteria (flagged here only for completeness, not as a finding).
- `__RequestVerificationToken` references in `mobile-push.js` and various `.cshtml` files are ASP.NET Core CSRF anti-forgery tokens (`@Html.AntiForgeryToken()`), not secrets — they are per-session, single-purpose tokens designed to be present in the client-rendered form.

No API keys, access tokens, private keys, passwords, or connection strings were found hardcoded as string literals in any frontend JavaScript, Razor view, or other publicly accessible file.
