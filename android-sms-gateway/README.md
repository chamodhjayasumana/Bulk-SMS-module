# Android SMS Gateway (Phase A)

Local-only HTTP SMS gateway that uses the phone’s Dialog SIM via Android `SmsManager`.

**Do not expose this app to the public internet.**

## Endpoints

### Health

```http
GET /api/gateway/status
Authorization: Bearer <TOKEN>
```

### Send SMS

```http
POST /api/sms/send
Authorization: Bearer <TOKEN>
Content-Type: application/json

{
  "requestId": "TEST-001",
  "phoneNumber": "94771234567",
  "message": "Test SMS"
}
```

Duplicate `requestId` values are rejected for resend (idempotent replay of previous result).

## Defaults

| Item | Value |
|------|--------|
| Port | `8080` |
| Min Android | API 26 |
| Auth | Bearer token (EncryptedSharedPreferences) |
| Provider mode on ASP.NET | Keep `Mock` until Phase B |

## Build

Open this folder in **Android Studio**, sync Gradle, run on a physical phone with a Dialog SIM.
