# Bulk SMS Platform  
## Product Introduction for Commercial Clients

**Document type:** Client overview  
**Version:** 1.0  
**Prepared for:** Prospective business clients  
**Confidentiality:** For authorized client review

---

## 1. Executive summary

**Bulk SMS Platform** is a private, business-ready messaging solution that helps organizations send the same SMS notification to many Sri Lankan mobile numbers in a controlled, trackable way.

It is designed for companies that need:

- Customer notifications and reminders  
- Promotional or campaign messages  
- Operational alerts  
- Internal staff broadcasts  

Unlike typical cloud SMS services, this platform can run **inside your own local network**, using either:

1. **An Android phone with a Dialog (or other local) SIM** as the SMS gateway, or  
2. A configurable **external SMS gateway API** when preferred  

Your message content, recipient lists, and credentials stay under your control.

---

## 2. Who this product is for

Ideal for:

| Client type | Example use |
|-------------|-------------|
| Retail & shops | Offers, collection reminders, event notices |
| Clinics & service centers | Appointment reminders |
| Schools & institutes | Parent / student notices |
| SMEs & field teams | Delivery updates, staff alerts |
| Agencies & call centres | Campaign blasts with review controls |

---

## 3. What the product does

### 3.1 Core Bulk SMS

Authorized users can:

1. Sign in securely  
2. Upload a recipient list (**CSV / Excel**)  
3. Automatically validate Sri Lankan mobile numbers  
4. See valid, invalid, and duplicate counts  
5. Write or refine the SMS message  
6. View character count and estimated SMS segments (English / Sinhala / Tamil aware)  
7. Confirm before sending  
8. Send messages in a controlled sequence  
9. Track success, failure, and progress in real time  
10. Export failed numbers for follow-up  

### 3.2 Local Android SMS Gateway (recommended for private setups)

Your Android phone acts as a local SMS gateway:

```
Office PC (Web App + Server)
        ↓  Local Wi‑Fi
Android phone + Dialog SIM
        ↓  Mobile network
Customer phones
```

**Benefits for clients**

- No mandatory third-party SMS cloud subscription for basic use  
- Works on your local Wi‑Fi  
- Gateway token stays on the server (not in the website)  
- Start / stop gateway from the phone  
- View sent / failed counters and recent activity on the phone  

### 3.3 AI Campaign Assistant (optional)

Users can describe a campaign in **English, Sinhala, or Tamil** and receive suggested SMS drafts for review.

- Suggestions include character and segment estimates  
- Safety checks help flag risky or spam-like content  
- AI **never sends SMS by itself**  
- A human must still review, edit, and confirm before sending  

---

## 4. Key business benefits

| Benefit | Description |
|---------|-------------|
| **Lower operating cost option** | Send via your own SIM/package instead of paying per-message cloud markups (subject to your telecom plan) |
| **Private deployment** | Can run locally; no public website required |
| **Sri Lanka ready** | Built for local number formats (`07…` / `94…`) |
| **Controlled sending** | Sequential sending with configurable delay to protect SIM and network stability |
| **Clear accountability** | Per-number status: Pending / Sending / Sent / Failed |
| **Safer operations** | Login required; gateway credentials never exposed to the browser |
| **Faster campaign writing** | Optional AI drafts in Sinhala / Tamil / English |

---

## 5. Product components

| Component | Role |
|-----------|------|
| **Web application (Angular)** | User interface for login, upload, validation, sending, progress |
| **Business server (ASP.NET)** | Security, validation, batch control, gateway communication |
| **Android SMS Gateway app** | Local HTTP gateway that sends SMS through the phone SIM |
| **Optional AI module** | Draft suggestions only (review required) |

---

## 6. How a typical send works

1. User opens **Bulk SMS** on the office PC  
2. System checks whether the Android gateway is online  
3. User uploads Excel/CSV of numbers  
4. System validates and removes duplicates  
5. User enters the message (or selects an AI suggestion)  
6. User confirms: *“Send to X recipients?”*  
7. Server sends one number at a time to the Android gateway  
8. Phone sends SMS through the Dialog SIM  
9. Results update on screen (Successful / Failed / Pending)  

---

## 7. Security & privacy highlights

- Secure login (authorized users only)  
- SMS gateway token stored on the **server / phone**, not in the website  
- Android gateway intended for **local network only** (not public internet)  
- Sensitive credentials are not shown in the UI  
- AI and SMS modules are separated — drafting does not equal sending  
- Logging avoids passwords, API keys, and unnecessary sensitive content  

---

## 8. What is included in a commercial package (suggested)

A standard commercial introduction package can include:

1. Web Bulk SMS application  
2. Backend API server  
3. Android SMS Gateway application  
4. Installation & local network setup guidance  
5. Admin user setup  
6. Sample recipient CSV template  
7. Basic operator training  
8. Optional AI assistant configuration  

*(Exact commercial package, license, and support terms can be customized per client.)*

---

## 9. Client responsibilities

For successful operation, the client should provide:

- A Windows PC for the web system  
- Stable local Wi‑Fi  
- An Android phone dedicated (or primarily used) as the gateway  
- Active Dialog (or supported) SIM with SMS balance / package  
- Authorized staff to review and approve outgoing messages  
- Compliance with Sri Lankan telecom and marketing rules (consent, opt-out, content standards)

---

## 10. Important commercial limitations (please read)

To set correct expectations:

- A normal prepaid/postpaid SIM is **not unlimited**. Carrier and Android limits may apply.  
- Delivery depends on network coverage, SIM balance, and recipient handset status.  
- “Accepted by the phone” does not always mean “delivered to the customer.”  
- The Android phone must remain powered, connected to Wi‑Fi, and running the gateway.  
- Phone IP address may change when Wi‑Fi reconnects (configuration may need update).  
- Sending is intentionally paced (not thousands of simultaneous requests).  
- The system does **not** bypass Dialog, Android, or regulatory restrictions.  
- Clients remain responsible for lawful use, consent, and message content.

---

## 11. Recommended rollout plan for clients

| Stage | Activity |
|-------|----------|
| **Week 1** | Install on local PC + Android phone, send test SMS |
| **Week 1–2** | Pilot with 5–20 recipients |
| **Week 2** | Train operators on validation, confirmation, failed export |
| **Ongoing** | Daily/weekly campaigns within SIM package limits |

---

## 12. Support model (suggested)

| Level | Coverage |
|-------|----------|
| Installation support | Local network setup, first successful test send |
| Training | How to upload lists, validate, send, and review results |
| Maintenance | Updates, configuration help, troubleshooting gateway connectivity |
| Optional | AI configuration, branding, custom reports, future database history |

---

## 13. Next steps for interested clients

1. Short discovery call (use case, volume, languages, local vs API gateway)  
2. Live demo on a local setup  
3. Pilot agreement (small recipient volume)  
4. Commercial proposal (license + setup + support)  
5. Go-live and operator training  

---

## 14. Contact

**Product:** Bulk SMS Platform  
**Vendor / Developer:** *[Your company or your name]*  
**Email:** *[your email]*  
**Phone:** *[your phone]*  
**Location:** Sri Lanka  

---

## Appendix A — Sample recipient file format

```csv
MobileNumber
0712345678
0771234567
0761234567
0781234567
```

Accepted column names include: `MobileNumber`, `Mobile`, `Phone`, `PhoneNumber`, `Telephone`, `Contact`.

Numbers such as `0712345678`, `94712345678`, and `+94712345678` are supported and normalized for sending.

---

## Appendix B — One-page value statement (for email / proposal cover)

> **Bulk SMS Platform** helps Sri Lankan businesses send controlled bulk SMS from their own office network. Upload a list, validate numbers, review the message, and send through a local Android phone with a Dialog SIM — with progress tracking, failure export, optional Sinhala/Tamil AI drafting, and no need for public cloud hosting.

---

*This document is an introduction for commercial discussion. Final pricing, SLA, licensing, and scope will be confirmed in a separate commercial proposal.*
