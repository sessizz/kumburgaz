# XSS Analysis Results: Kumburgaz

## Executive Summary
- Sink sites analyzed: 1
- Vulnerable: 1
- Likely Vulnerable: 0
- Not Vulnerable: 0
- Needs Manual Review: 0

## Findings

### [VULNERABLE] Block name breaks out of inline `onsubmit` JS string (delete confirm)
- **File**: `Views/Blocks/Index.cshtml` (lines 57-58)
- **Endpoint / function / component**: `GET /Blocks` (`BlocksController.Index`) view, per-row delete `<form>`; value written via `BlocksController.Create`/`Edit` (`POST /Blocks/Create`, `POST /Blocks/Edit`)
- **XSS type**: Stored
- **Issue**: `@item.Name` (the `Block.Name` field) is interpolated directly inside a single-quoted JavaScript string literal within the `onsubmit` HTML attribute:
  ```cshtml
  <form asp-action="Delete" asp-route-id="@item.Id" method="post"
        onsubmit="return confirm('\'@item.Name\' bloğu silinsin mi?');">
  ```
  Razor's default attribute encoding HTML-encodes `'` to `&#39;`. The browser's HTML parser decodes `&#39;` back to a literal `'` *before* handing the attribute value to the JS engine as the `onsubmit` handler source. This means the encoding that would normally prevent breakout is neutralized for the JS-string context: an attacker-controlled `'` in `item.Name` closes the JS string early, and everything else needed to complete a payload (parentheses, semicolons, slashes) is not HTML-encoded at all and passes through untouched. Any `Name` value is rendered for every user who opens `/Blocks`, so this is a persistent (stored) XSS affecting all viewers of the page, not just the author.
- **Taint trace**:
  1. Source: `POST /Blocks/Create` or `POST /Blocks/Edit` form field `Name`, bound to `Block model` in `BlocksController.Create(Block model)` / `Edit(Block model)` (`Controllers/BlocksController.cs`).
  2. Validation: `Block.Name` in `Models/DomainModels.cs` line 273-274 has only `[Required, MaxLength(60)]` — no `[RegularExpression]`, no character allowlist. Client-side `maxlength="60"` on the Create modal input (`Views/Blocks/Index.cshtml` line 83) is a UI hint only, trivially bypassed by posting directly to `/Blocks/Create`.
  3. Storage: `model.Name = model.Name.Trim();` then `db.Blocks.Add(model); await db.SaveChangesAsync();` — stored verbatim (only trimmed) in the `Blocks` table.
  4. Sink: `Views/Blocks/Index.cshtml` line 44-58, `@foreach (var item in Model)` renders `@item.Name` raw inside the `onsubmit` JS string literal for the delete-confirm form. Also rendered plainly at line 47 (`<td class="font-semibold">@item.Name</td>`), which is safely HTML-encoded there — the vulnerability is specific to the JS-string-in-attribute context at line 58.
- **Impact**: Any user with access to create/rename Blocks (gated by `[ModuleAuthorize(AppModules.Daireler)]` on `BlocksController` — the "Daireler" module permission, not necessarily a full super-admin role) can store a JS payload as a block name. When any other user who has view access to `/Blocks` (also gated by the same module) loads the page, the injected script executes in their session. This enables session/cookie theft, CSRF-token exfiltration (note `@Html.AntiForgeryToken()` is rendered in the same page), UI redress, or using the victim's authenticated session to perform actions (e.g., silently submitting the delete forms, or other admin actions) — a horizontal/lateral escalation within the "Daireler" module's user base, and potentially attacking higher-privileged users (e.g., a full admin) who also has access to this module.
- **Remediation**:
  - Do not interpolate raw model data into inline JS string literals in HTML attributes. Replace the inline `onsubmit="confirm('...')"` pattern with unobtrusive JS: give the form/button a `data-block-name="@item.Name"` attribute (Razor will HTML-encode this correctly for a plain attribute context) and read it via `event.target.dataset.blockName` in an external/`<script>` event listener, then pass it through `confirm()` from JS — never rebuild a JS string via server-side templating.
  - Alternatively, if inline handlers must be kept, JavaScript-escape the value server-side (e.g., `System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(item.Name)`) before embedding it inside the JS string, in addition to (not instead of) HTML-attribute encoding.
  - Consider adding server-side input restriction on `Block.Name` (e.g., disallow HTML/JS meta-characters) as defense in depth, though the primary fix must be at the output/sink layer since any free-text field could end up in a similar context.
- **Dynamic Test**:
  ```
  1. Log in as a user with the "Daireler" module permission (able to access /Blocks and create/edit blocks).
  2. Navigate to /Blocks, click "Yeni Blok", and in the Name field submit (via browser devtools or a raw POST to bypass the maxlength=60 client-side limit if needed):
       ');alert(document.cookie);//
     (Or a shorter payload that still fits under 60 chars: ');alert(1);//)
  3. Submit the Create form (POST /Blocks/Create).
  4. Reload /Blocks (or have any other module user open it) and click the "Sil" (delete) button for the newly created block's row.
  5. Observe: instead of a native browser confirm() dialog with the expected text, an alert(document.cookie) (or alert(1)) fires — confirming the block name broke out of the confirm() string and executed as JavaScript.
  ```

## Confirmed-Safe Patterns (not flagged as vulnerable)

- `Areas/Mobile/Views/Panel/Index.cshtml:33` — the only other `Html.Raw(...)` call in the codebase (`Html.Raw(sb.ToString())`); every interpolated value going into the `StringBuilder` (`label`, `it.Name`) is passed through `System.Net.WebUtility.HtmlEncode(...)` first — safe.
- `Views/Audit/Index.cshtml:6,195` — local `Js(string value) => JavaScriptEncoder.Default.Encode(value)` helper used to safely embed `row.RestoreConfirmText` into a `confirm('@Js(...)')` JS string literal — safe, and notably this is the correct pattern that `Views/Blocks/Index.cshtml` should have used instead.
- `wwwroot/js/site.js` (global unit search dropdown, lines 66-127) — builds `results.innerHTML` from AJAX JSON response fields (`item.label`, `item.ownerName`, `item.tenantName`, `item.subtitle`, `item.unitNo`), but every field is passed through a local `escapeHtml()` function before concatenation — safe.
- `wwwroot/js/document-preview.js`, `wwwroot/js/mobile.js`, `wwwroot/js/mobile-push.js` — use only `textContent`, `createElement`, `replaceChildren`, `setAttribute` with static attribute names — no HTML-injection sinks.
- `Views/Home/Index.cshtml:68` — `href="@alert.Url"` renders a URL built server-side from internal dashboard-alert data (not raw end-user free text), no `javascript:`-scheme validation present; flagged for awareness only, not an active sink today.
- Numerous `onclick="document.getElementById('modal-@Model.Id').showModal()"` / `onclick="openLightbox('@Url.Action(...)')"` patterns across `Views/Ledger/*`, `Views/CashBank/_DetailParts/*`, `Areas/Mobile/Views/Gider/*` — all interpolated values are numeric/GUID entity IDs or `Url.Action(...)`-generated route URLs, not free-text user input — safe.
- All other data-bound output across `Views/*`, `Areas/Mobile/Views/*`, `Areas/Identity/Pages/*` uses plain Razor `@expression` interpolation (auto HTML-encoded) — no other `Html.Raw`, `MvcHtmlString`, or `new HtmlString(...)` calls exist in the codebase.
- No `eval(`, `new Function(`, `document.write(`, `insertAdjacentHTML(`, or dynamic `<script>` tag construction found in any first-party file.
- No DOM-based source→sink flow (`location.hash`, `location.search`, `document.referrer`, `document.cookie`, `window.name`, `postMessage`) found in first-party JS; matches in `wwwroot/lib/*` are unmodified vendored jQuery/Bootstrap library internals, out of scope.
