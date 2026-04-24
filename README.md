# ⚡ SemaBuzz for Windows

> **Built for the desktop. Made for the wire.**
> A live-typing chat for Windows — encrypted in transit, no accounts, relay-assisted connection.

---

## What It Is

SemaBuzz is a C# / .NET 9 WPF desktop application that lets two people communicate in real time over a direct encrypted connection. Text is streamed **character-by-character** as it is typed, so the other person sees your thoughts form live on their screen. A visual "filament" indicator pulses with the rhythm of incoming data, making the connection feel tactile and alive.

Messages are encrypted on your device with ECDH P-256 key exchange and AES-256-GCM before leaving it. The relay forwards packets but never holds the keys and cannot read your messages. No account required — just a handle, a port (or a relay token), and a peer.

---

## Features

### Core
- **Live-wire typing** — characters stream keystroke-by-keystroke in real time
- **Buzz alerts** — send an instant ⚡ that shakes the peer's window and pulses the filament
- **Strong encryption** — ephemeral ECDH P-256 key exchange + AES-256-GCM; relay sees only ciphertext
- **Three connection modes** — direct P2P, relay (WebSocket), or `buzz://` URI deep-links
- **STUN NAT discovery** — auto-detects your external IP/port to simplify direct connections
- **Identity profiles** — named handles with optional avatar images, saved locally
- **Color emoji** — full color emoji rendering in chat and the emoji picker (Emoji.Wpf)
- **12 themes** — Obsidian, Neon, Matrix, BloodMoon, Arctic, Sepia, Midnight, Sunset, Rose, Violet, Emerald, Steel

### Pro (Microsoft Store license)
- **Permanent encrypted chat logs** — sessions persisted to disk, AES-encrypted at rest
- **Custom listen port** — pre-configure your default port in settings
- **Pulse & Wave indicator styles** — additional filament animations beyond the free Flicker mode

---

## How Connections Work

Connections are relay-first. The host generates a short `buzz://TOKEN` address and shares it with their peer. Both sides connect to the relay server via WebSocket; the relay pairs them by token and forwards encrypted frames transparently. The relay never sees plaintext — the ECDH handshake and all subsequent traffic is encrypted on-device before transmission. The relay forwards opaque ciphertext only.

Default relay: `wss://relay.semabuzz.me` (configurable in Settings)

### `buzz://` Deep Links
The app registers the `buzz://` URI scheme. A `buzz://TOKEN` link opens the app and pre-populates the dial dialog. Direct `buzz://host:port` URIs are also supported at the protocol level for advanced use.

---

## Encryption

Every session generates a **fresh ephemeral ECDH P-256 key pair**. Public keys are exchanged during the handshake, a shared secret is derived, and all subsequent traffic is encrypted with **AES-256-GCM** — providing both confidentiality and tamper detection. Private keys never leave the device. Peer identity metadata (handle + avatar) is also encrypted before transmission.

Packet integrity is further protected by per-packet sequence numbers to detect replays.

---

## Project Structure

| Project | Role |
|---|---|
| `SemaBuzz.App` | WPF UI — windows, dialogs, theming, indicator |
| `SemaBuzz.Protocol` | Core library — wire protocol, encryption, STUN, relay client |
| `SemaBuzz.Relay` | Self-hostable ASP.NET Core WebSocket relay server |
| `SemaBuzz.Styles` | Shared XAML styles and color resources |
| `SemaBuzz.Tests` | Unit tests |

---

## Tech Stack

| | |
|---|---|
| Language | C# 12 |
| Runtime | .NET 9 |
| UI Framework | WPF (Windows Presentation Foundation) |
| Networking | UDP (P2P), WebSocket (relay) |
| Encryption | ECDH P-256 + AES-256-GCM |
| NAT Traversal | STUN (RFC 5389) |
| Emoji | [Emoji.Wpf](https://github.com/samhocevar/emoji.wpf) 0.3.4 |
| Packaging | MSIX (Microsoft Store) |
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

Or deploy to Railway, Render, or Fly.io — set the `PORT` environment variable and TLS is terminated by the platform.

### Endpoints

| Path | Description |
|---|---|
| `GET /` | Health check — returns `SemaBuzz Relay OK` |
| `WS /relay` | WebSocket endpoint for SemaBuzz clients |

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

### Publishing release binaries locally

Use the included script to build both targets:

```powershell
.\Publish-Relay.ps1
```

Output: `dist/relay/SemaBuzz-Relay-Windows.exe` and `dist/relay/SemaBuzz-Relay-Linux`

---

## Privacy — Relay Design

The relay is a **blind pass-through**. It does not log, read, or store message content. All traffic is encrypted on-device before reaching the relay; the server sees only opaque binary frames. IP addresses are held in memory for the duration of a session only and are never written to disk.

---

## Data & Privacy (App)

All settings and profiles are stored locally in `%APPDATA%\SemaBuzz\`. Nothing is transmitted to any server except the encrypted packets exchanged with your peer (and optionally routed through the relay). The relay server sees only opaque encrypted binary frames.

---

## License

Proprietary — Copyright (c) 2026 Skynr Labs. All rights reserved.

---

[semabuzz.com](https://semabuzz.com) &nbsp;·&nbsp; [Skynr Labs](https://skynrlabs.com)