# ⚡ SemaBuzz for Windows

![License](https://img.shields.io/badge/license-Proprietary-red?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-12-239120?style=flat-square&logo=csharp&logoColor=white)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D6?style=flat-square&logo=windows&logoColor=white)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D6?style=flat-square&logo=windows&logoColor=white)
![Encryption](https://img.shields.io/badge/encryption-AES--256--GCM%20%2B%20ECDH--P256-22c55e?style=flat-square&logo=letsencrypt&logoColor=white)
![WebSocket](https://img.shields.io/badge/relay-WebSocket-f97316?style=flat-square)
![Docker](https://img.shields.io/badge/relay-Docker-2496ED?style=flat-square&logo=docker&logoColor=white)

> **Built for the desktop. Made for the wire.**
> A live-typing chat for Windows — encrypted in transit, no accounts, relay-assisted connection.

---

## What It Is

SemaBuzz is a C# / .NET 9 WPF desktop application that lets two people communicate in real time over a direct encrypted connection. Text is streamed **character-by-character** as it is typed, so the other person sees your thoughts form live on their screen. A visual "filament" indicator pulses with the rhythm of incoming data, making the connection feel tactile and alive.

Messages are encrypted on your device with ECDH P-256 key exchange and AES-256-GCM before leaving it. The relay forwards packets but never holds the keys and cannot read your messages. No account required — just a handle and a relay token.

---

## Features

### Free
- **Live-wire typing** — characters stream keystroke-by-keystroke in real time
- **Buzz alerts** — send an instant ⚡ that shakes the peer's window and pulses the filament
- **Strong encryption** — ephemeral ECDH P-256 key exchange + AES-256-GCM; relay sees only ciphertext
- **Relay connection** — connect via the default relay; no port-forwarding needed
- **STUN NAT discovery** — auto-detects your external IP/port for direct connections
- **Identity profiles** — named handles with optional avatar images, saved locally
- **100+ emoji** — full colour emoji rendering in chat and built-in picker (7 categories)
- **Obsidian theme** — the default dark amber look
- **Flicker indicator** — the live-typing filament animation

### Pro (one-time license — $9.99)
- **URL Walk** — push a live URL to your peer mid-conversation; appears as a clickable card
- **Shared whiteboard** — open a real-time drawing board alongside chat; 6 colours, 3 stroke sizes, synced CLEAR; strokes encrypted over the wire
- **15 Pro themes** — Neon, Matrix, Blood Moon, Arctic, Sepia, Midnight, Sunset, Rose, Violet, Emerald, Steel, Forest, Chrome, Muted Terminal, Retro '95
- **Pulse & Wave indicator styles** — additional filament animations
- **Custom relay URI** — override the default relay in Settings
- **Custom default port** — pre-configure your listen port

Purchase at [semabuzz.gumroad.com/l/dgeyxz](https://semabuzz.gumroad.com/l/dgeyxz). License key is emailed instantly and activates offline.

---

## How Connections Work

The host generates a short token and shares it with their peer. Both sides connect to the relay server via WebSocket; the relay pairs them by token and forwards encrypted frames transparently. The relay never sees plaintext — the ECDH handshake and all subsequent traffic is encrypted on-device before transmission.

Default relay: `wss://relay.semabuzz.me` (configurable in Settings for Pro users)

---

## Encryption

Every session generates a **fresh ephemeral ECDH P-256 key pair**. Public keys are exchanged during the handshake, a shared secret is derived, and all subsequent traffic is encrypted with **AES-256-GCM** — providing both confidentiality and tamper detection. Private keys never leave the device. Peer identity metadata (handle + avatar) is also encrypted before transmission.

Per-packet sequence numbers guard against replays.

---

## Project Structure

| Project | Role |
|---|---|
| `SemaBuzz.App` | WPF UI — windows, dialogs, theming, indicator |
| `SemaBuzz.Protocol` | Core library — wire protocol, encryption, STUN, relay client |
| `SemaBuzz.Relay` | Self-hostable ASP.NET Core WebSocket relay server |
| `SemaBuzz.Styles` | Shared XAML styles and colour resources |
| `SemaBuzz.Tests` | Unit and integration tests |

---

## Tech Stack

| | |
|---|---|
| Language | C# 12 |
| Runtime | .NET 9 |
| UI Framework | WPF (Windows Presentation Foundation) |
| Networking | WebSocket (relay), UDP (direct P2P) |
| Encryption | ECDH P-256 + AES-256-GCM |
| NAT Traversal | STUN (RFC 5389) |
| Emoji | [Emoji.Wpf](https://github.com/samhocevar/emoji.wpf) 0.3.4 |
| Packaging | Single-file `.exe` (self-contained) |
| Min OS | Windows 10 1809 (build 17763)+ |

---

## Building

Requirements: Visual Studio 2022 (17.8+) or the .NET 9 SDK with the **.NET desktop development** workload.

```
git clone https://github.com/semabuzz/SemaBuzz.git
cd SemaBuzz
dotnet build SemaBuzz.sln -c Debug
```

Output lands in `build/Debug/net9.0-windows10.0.17763.0/`. Set `SemaBuzz.App` as the startup project in Visual Studio to run with F5.

---

## Self-Hosting the Relay

### Pre-built binaries

Download a self-contained single-file binary from the [latest release](https://github.com/semabuzz/SemaBuzz/releases/latest):

| Platform | File |
|---|---|
| Windows x64 | `SemaBuzz-Relay-Windows.exe` |
| Linux x64 | `SemaBuzz-Relay-Linux` |

No runtime required. Just run:

```powershell
# Windows
.\SemaBuzz-Relay-Windows.exe [--port 7171]

# Linux
chmod +x SemaBuzz-Relay-Linux
./SemaBuzz-Relay-Linux [--port 7171]
```

The default port is **7171** and can be overridden with `--port` or the `PORT` environment variable.

### Build from source

```
cd src/SemaBuzz.Relay
dotnet run
```

### Docker

```dockerfile
docker build -t semabuzz-relay .
docker run -p 7171:7171 semabuzz-relay
```

Or deploy to Railway, Render, or Fly.io — set the `PORT` environment variable; TLS is terminated by the platform.

### Endpoints

| Path | Description |
|---|---|
| `GET /` | Health check — returns `SemaBuzz Relay OK` |
| `WS /relay` | WebSocket endpoint for SemaBuzz clients |

### Rate limits & defaults

| Setting | Value |
|---|---|
| Default port | 7171 |
| WebSocket keep-alive | 30 s |
| Room TTL (idle) | 10 min |
| Max rooms (global) | 500 |
| Max connections per IP | 5 |

### Stopping

```powershell
# Ctrl+C in the terminal (clean shutdown)
# Windows background:
Stop-Process -Name "SemaBuzz-Relay-Windows"
# Linux background:
pkill SemaBuzz-Relay-Linux
# Docker:
docker stop <container-name>
```

---

## Privacy — Relay Design

The relay is a **blind pass-through**. It does not log, read, or store message content. All traffic is encrypted on-device before reaching the relay; the server sees only opaque binary frames. IP addresses are held in memory for the duration of a session only and are never written to disk.

---

## Data & Privacy (App)

All settings and profiles are stored locally in `%APPDATA%\SemaBuzz\`. Nothing is transmitted to any server except the encrypted packets exchanged with your peer (routed through the relay). The relay server sees only opaque encrypted binary frames.

---

## Contributing

The repo uses a two-branch model:

| Branch | Purpose |
|---|---|
| `main` | Stable, release-ready. Tagged on every release. |
| `dev` | Integration target — all PRs merge here first. |

For features and fixes, branch off `dev` (`feature/my-thing` or `fix/my-thing`), open a PR back to `dev`. Releases are cut by merging `dev` → `main` and tagging:

- App releases: `vX.Y.Z`
- Relay releases: `vX.Y.Z-relay`

---

## License

SemaBuzz is **proprietary software**. Copyright © 2026 Skynr Labs. All rights reserved.

You may download and use the application for personal or internal business use. You may not copy, modify, redistribute, sublicense, or sell the software or its source code. See [LICENSE](LICENSE) for full terms.

---

[semabuzz.com](https://semabuzz.com) &nbsp;·&nbsp; [Skynr Labs](https://skynrlabs.com)