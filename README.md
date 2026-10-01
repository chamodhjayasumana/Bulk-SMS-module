# Bulk SMS (Angular + ASP.NET)

Personal Bulk SMS module for sending the same message to many Sri Lankan mobile numbers through a configurable SMS gateway REST API.

## Architecture

```
Angular UI
  → ASP.NET Web API (/api/sms/*)
    → IBulkSmsService
      → ISmsProvider (MockSmsProvider | RestSmsProvider)
        → External SMS Gateway (when Provider=Rest)
```

Credentials (`ApiKey`, `Username`, `Password`, `SenderId`, `ApiUrl`) live **only** in ASP.NET configuration. They are never exposed to Angular.

## Solution layout

| Path | Purpose |
|------|---------|
| `src/BulkSms.Api` | Web API, JWT auth, controllers |
| `src/BulkSms.Application` | Validation, parsing, batching, providers |
| `src/BulkSms.Domain` | Models + options |
| `tests/BulkSms.Tests` | Unit + API tests |
| `client` | Angular 20 UI |
| `samples/recipients-sample.csv` | Sample upload file |

## Quick start

### 1. API

```powershell
cd "e:\sms bulck\src\BulkSms.Api"
dotnet run --launch-profile https
```

Swagger (Development): `https://localhost:7291/swagger`

Default login (dev placeholders — change before any real use):

- Username: `bulksms.admin`
- Password: `ChangeMe123!`

### 2. Angular

```powershell
cd "e:\sms bulck\client"
npm start
```

Open `http://localhost:4200` → sign in → Bulk SMS page.

API base URL for local HTTPS: `client/src/environments/environment.ts` → `https://localhost:7291/api`

### 3. Tests

```powershell
cd "e:\sms bulck"
dotnet test
```

Tests use `MockSmsProvider` and never send real SMS.

## Configuration

Edit `src/BulkSms.Api/appsettings.json` (or IIS / environment overrides). Template: `appsettings.json.template`.

### SMSProvider

| Key | Description |
|-----|-------------|
| `Provider` | `Mock` (default) or `Rest` |
| `ApiUrl` | Gateway endpoint |
| `ApiKey` / `Username` / `Password` / `SenderId` | Gateway credentials |
| `BatchSize` | Recipients per batch |
| `MaxConcurrency` | Max parallel HTTP sends |
| `RequestTimeoutSeconds` | HttpClient timeout |
| `RetryCount` / `RetryDelayMs` | Transient-error retries only |
| `RequestMobileField` / `RequestMessageField` / `RequestSenderField` | Request JSON field names |
| `ResponseMessageIdField` / `ResponseSuccessField` / `ResponseErrorField` | Response JSON field names |

### Switch providers

**Mock (safe for development):**

```json
"SMSProvider": { "Provider": "Mock" }
```

**Real gateway:**

```json
"SMSProvider": {
  "Provider": "Rest",
  "ApiUrl": "https://your-gateway/api/send",
  "ApiKey": "YOUR_KEY",
  "SenderId": "YOUR_SENDER"
}
```

### Provider-specific mapping

Update these in config first. If the gateway body/response shape differs further, edit:

`src/BulkSms.Application/Providers/RestSmsProvider.cs`

Marked sections:

- `BuildRequestPayload`
- `ApplyAuth`
- `ParseResponse`

## API examples

### Login

```http
POST /api/Auth/token
Content-Type: application/json

{ "username": "bulksms.admin", "password": "ChangeMe123!" }
```

### Validate file

```http
POST /api/sms/validate
Authorization: Bearer {token}
Content-Type: multipart/form-data

file: recipients.csv
message: Hello from Bulk SMS
```

### Send bulk

```http
POST /api/sms/send-bulk
Authorization: Bearer {token}
Content-Type: application/json

{
  "message": "Hello from Bulk SMS",
  "recipients": ["94712345678", "94771234567"],
  "confirmed": true
}
```

## Mobile normalization

| Input | Normalized |
|-------|------------|
| `0712345678` | `94712345678` |
| `+94771234567` | `94771234567` |
| `94771234567` | `94771234567` |

Rejects landlines, invalid prefixes, short/long/empty values; removes duplicates before send.

## IIS deployment

1. Publish API:
   ```powershell
   dotnet publish "e:\sms bulck\src\BulkSms.Api\BulkSms.Api.csproj" -c Release -o "C:\inetpub\BulkSmsApi"
   ```
2. Install [.NET 9 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/9.0).
3. Create IIS site / app pool (No Managed Code), point to publish folder.
4. Set production secrets via:
   - `appsettings.Production.json` (not committed), or
   - IIS environment variables / Azure Key Vault / user secrets.
5. Build Angular:
   ```powershell
   cd "e:\sms bulck\client"
   npm run build
   ```
6. Host `client/dist/bulk-sms-web/browser` on IIS (or reverse-proxy). Set `environment.prod.ts` `apiBaseUrl` to your API path (e.g. `https://your-host/api`).
7. Enable HTTPS; restrict CORS origins in `Program.cs` for production.

## Security checklist

- [ ] Change JWT `Secret` and all `AuthUsers` passwords
- [ ] Never commit real SMS gateway credentials
- [ ] Keep `SMSProvider:Provider=Mock` until go-live
- [ ] Confirm Angular env files contain **no** API keys
- [ ] Restrict CORS to your Angular origin in production
- [ ] Use HTTPS only in production
- [ ] Limit `MaxRecipientsPerRequest` / `MaxUploadBytes`
- [ ] Grant `BulkSMS` access only to authorized users
- [ ] Do not log message bodies, OTPs, or passwords
- [ ] Configure provider field mapping before enabling `Rest`

## Remaining provider work

Before production SMS:

1. Obtain gateway API documentation.
2. Set `ApiUrl`, auth, and field-name options.
3. Adjust `RestSmsProvider` if the payload/response differs from the default JSON shape.
4. Run a single-recipient test with a test sender ID.
5. Only then enable bulk sends.
