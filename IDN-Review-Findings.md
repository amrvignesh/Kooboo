# IDN/EAI Compatibility — Bug & Enhancement Review

Review of the completed IDN work, July 13, 2026. Ordered by severity.

> **Status update (same day):** All bugs and minor issues below are now FIXED, including
> `Hash.ComputeInt`/`ComputeGuidIgnoreCase`, which now use `ToLowerInvariant()`.
> NOTE: the hash change can alter stored IDs for culture-sensitive characters — a data
> wipe / fresh install is planned, so this is safe. NFC normalization was also added in
> `GetPunycodeAddress`, which is identity for existing NFC/ASCII data.
> A new shared `Kooboo.Lib.Domain.IdnHelper` replaces all per-call `IdnMapping` blocks.
> The SMTPUTF8 gap was fixed in the real send path (`MailKitUtility.Send`): it now sets
> `FormatOptions.International` when a Unicode local part is present and the server
> advertises SMTPUTF8, and fails with a clear log message when it doesn't.
> Redeploy required: rebuild the .NET solution and the Frontend, then republish to OCI.

## Bugs

### 1. SSL status shown against Unicode name — always reports "no SSL" for IDN bindings
`Kooboo.Web/Api/Implementation/Binding.cs`, `ListBySite` (lines 192–197).
`BindingViewModel` converts `FullName` to Unicode first, then `HasSsl(model.FullName)` and `SslService.GetError(model.FullName)` are called with the **Unicode** name. Certificates are stored under the Punycode name, so for IDN bindings the UI will always show SSL as disabled/errored even when a cert exists.
**Fix:** compute `EnableSsl`/`SslError` from the raw (Punycode) `item.FullName` before converting for display.

### 2. SMTPUTF8 work lives in dead code — real send path is MailKit
`Kooboo.Mail/Smtp/Client/SmtpClient.cs` is not instantiated anywhere in the solution (the only `new SmtpClient(...)` is MailKit's, in `MailKitUtility.Send`). All outbound mail — including the Brevo relay — goes through `Delivery.Send → MailKitUtility.Send`, which does **not** set `FormatOptions.International` or check the server's SMTPUTF8 capability explicitly. MailKit punycodes IDN domains itself, but a Unicode **local part** (விக்னேஷ்@…) depends on MailKit's implicit SMTPUTF8 handling and will throw/fail if the relay doesn't advertise it.
**Fix:** either delete the custom client and add explicit SMTPUTF8/International handling in `MailKitUtility.Send` (with a clear bounce message when the next hop lacks SMTPUTF8), or actually wire the custom client in. Also note `BuildCommands`/`PipeLineCommands` in the custom client never append the SMTPUTF8 parameter, so if that path is ever revived it is non-compliant.

### 3. No Unicode normalization (NFC) before hashing addresses
`Kooboo.Mail/Models/EmailAddress.cs` `ToId` → `GetPunycodeAddress` → `Hash.ComputeInt`. The local part is hashed as-is. Tamil/Hindi text can arrive in NFC or NFD form (visually identical, different code points), so mail to `விக்னேஷ்@தீ.இந்தியா` composed on a device that emits NFD will hash differently and get "mailbox not found". This is the same class of bug as the Punycode-local-part issue already fixed by hand.
**Fix:** apply `address.Normalize(NormalizationForm.FormC)` inside `ToId`/`GetPunycodeAddress` (and once at mailbox creation in `EmailAddressApi.Post`).

### 4. SMTP AUTH LOGIN decodes credentials as ASCII
`Kooboo.Mail/Smtp/SmtpSession.cs` lines 544, 551: `Encoding.ASCII.GetString(Convert.FromBase64String(...))`. A user authenticating with an EAI mailbox name (e.g. from Thunderbird/Outlook using விக்னேஷ்@தீ.இந்தியா as username) gets mangled to `?` characters and can never log in. The client side (`SmtpClient.Login`) already uses UTF-8 — the server side doesn't.
**Fix:** use `Encoding.UTF8.GetString(...)`.

### 5. Frontend still blocks Unicode in several inputs
`Frontend/src/utils/validate.ts` — these rules were not updated and reject Unicode before the (now-fixed) backend is ever reached:
- `wildcardEmailRule` (line 75) — can't create Unicode wildcard/alias addresses.
- `subDomainRule` (line 173) — can't bind a Unicode subdomain.
- `DomainRule` (line 178) and `domainSearchRule` (line 100) — can't enter/search Unicode domains.
- `hostRecordRule` (line 183) — can't add a Unicode host in DNS records, even though `AddDns` on the server now punycodes it.
- `notAllowMultilevelDomain` (line 109).

### 6. `BindingApi.IsUniqueName` compares raw input against stored Punycode
`Binding.cs` line 355+. `Post` stores bindings in Punycode, but the uniqueness check compares the un-normalized (Unicode) name against stored values — an IDN binding will pass the "unique" check and then collide (or duplicates get created).
**Fix:** `GetAscii` the name before comparing.

### 7. `DomainApi.Create` doesn't normalize before parsing
`Domain.cs` line 267: `DomainHelper.GetRootDomain(domainname)` (in the Kooboo.Data nuget package) is called with the raw input. Elsewhere (e.g. `AddressUtility.IsValidEmailDomain`) you punycode **before** calling `DomainHelper.Parse`, which implies the helper itself doesn't handle Unicode. A Unicode domain entered here will either fail or be stored un-normalized, breaking later Punycode-keyed lookups (`GlobalDb.Domains.Get`, `IDGenerator.GetDomainId`, binding matching).
**Fix:** `GetAscii(domainname)` at the top of `Create`, mirroring the other endpoints.

## Minor / consistency

- `DomainApi.DomainSiteBindings` (line 201): `SubDomain` is returned raw (Punycode) — every other endpoint converts to Unicode for display.
- `SmtpConnector.cs` line 268 writes server responses with `Encoding.ASCII`; any Unicode echoed in a response becomes `?`. RFC 6531 expects UTF-8 responses after SMTPUTF8 is negotiated.
- `EmailAddressApi.SetDefaultSender` / `SetAuthorizationCode`: `orgdb.Email.Find(address)` result is dereferenced without a null check — a form-mismatch lookup failure (see bug 3) becomes an unhandled 500.
- `Hash.ComputeInt` uses culture-sensitive `.ToLower()`; for identifiers prefer `ToLowerInvariant()` (Turkish-I class of bugs, now reachable with Unicode addresses).
- Per-call `new IdnMapping()` in `DomainService.IsValidDomain`/`Parse` (hot paths — host header parsing). `AddressUtility` already uses a shared static instance; do the same.
- Repeated `try { GetAscii } catch {}` blocks in `Binding.cs`/`Domain.cs` — worth extracting into one helper (e.g. `AddressUtility.GetPunycodeDomainSafe`) so behavior stays consistent.

## Enhancements

1. **Graceful downgrade / clear bounce when next hop lacks SMTPUTF8** — if only the domain is Unicode, punycode it and send; if the local part is Unicode, return a clear NDR ("recipient server does not support internationalized email") instead of a generic failure.
2. **IDNA2008 awareness** — .NET `IdnMapping` implements IDNA2003; characters like `ß` and `ς` map differently than modern registries expect. Low risk for Tamil/Chinese TLDs, worth a note in docs.
3. **Address-book normalization** — contacts (`AddBook`) store raw strings; store both Unicode display and Punycode forms so autocomplete matches whichever the user types.
4. **Tests** — add unit tests covering: NFC/NFD equivalence, Punycode vs Unicode lookup equivalence for mailbox/domain/binding, SSL lookup for IDN bindings, and EHLO SMTPUTF8 negotiation. The `VerifyApp` scratch checks would convert nearly as-is.
