# Security Policy

## Supported Versions

| Version | Supported |
|---|---|
| Latest release on `main` | ✅ |
| Older releases | ❌ — please update |

---

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Please report vulnerabilities privately via GitHub's built-in security advisory system:

1. Go to the [Security tab](https://github.com/SemaBuzz/SemaBuzz/security/advisories) of this repository.
2. Click **"Report a vulnerability"**.
3. Fill in the details — include steps to reproduce, affected component, and potential impact.

You will receive an acknowledgement within **5 business days**. We aim to triage and respond with a remediation plan within **14 days** of receiving a valid report.

---

## Scope

The following are in scope for security reports:

- **SemaBuzz.Protocol** — encryption, key exchange, packet integrity
- **SemaBuzz.Relay** — relay server confidentiality, denial-of-service, abuse of rate limits
- **SemaBuzz.App** — local data exposure, authentication bypass, URI handler injection

The following are **out of scope**:

- Vulnerabilities in third-party dependencies (report those upstream)
- Issues requiring physical access to the device
- Social engineering attacks

---

## Security Design

SemaBuzz is designed with the following guarantees:

- **End-to-end encryption.** All messages are encrypted on-device with ephemeral ECDH P-256 key exchange and AES-256-GCM before transmission. The relay and Skynr Labs cannot read message content.
- **Blind relay.** The relay server is a pass-through. It never reads, logs, or stores message content. IP addresses are held in memory only for the duration of an active session.
- **No accounts.** No credentials are stored on any server.
- **Local storage.** All settings, profiles, and (Pro) chat logs are stored locally in `%APPDATA%\SemaBuzz\`. Pro logs are AES-encrypted at rest.

If you believe any of the above guarantees are violated by a vulnerability, that is a high-priority report.

---

## Disclosure Policy

Once a fix is released, we will publish a GitHub Security Advisory crediting the reporter (unless they request anonymity). We ask that reporters observe a **90-day coordinated disclosure** window from the time of our acknowledgement before publishing independently.
