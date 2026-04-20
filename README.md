# ⚡ SemaBuzz for Windows

> **Built for the desktop. Made for the wire.**
> A P2P encrypted live-typing chat for Windows — no accounts, no cloud, no middlemen.

---

## What It Is

SemaBuzz is a C# / .NET 9 WPF desktop application that lets two people communicate in real time over a direct encrypted connection. Text is streamed **character-by-character** as it is typed, so the other person sees your thoughts form live on their screen. A visual "filament" indicator pulses with the rhythm of incoming data, making the connection feel tactile and alive.

Everything is end-to-end encrypted with ECDH P-256 key exchange and AES-256-GCM. No account required — just a handle, a port (or a relay token), and a peer.

---

## Features

### Core
- **Live-wire typing** — characters stream keystroke-by-keystroke in real time
- **Buzz alerts** — send an instant ⚡ that shakes the peer's window and pulses the filament
- **End-to-end encryption** — ephemeral ECDH P-256 key exchange + AES-256-GCM on every session
- **Three connection modes** — direct P2P, relay (WebSocket), or `buzz://` URI deep-links
- **STUN NAT discovery** — auto-detects your external IP/port to simplify direct connections
- **Identity profiles** — named handles with optional avatar images, saved locally
- **12 themes** — Obsidian, Neon, Matrix, BloodMoon, Arctic, Sepia, Midnight, Sunset, Rose, Violet, Emerald, Steel

### Pro (Microsoft Store license)
- **Permanent encrypted chat logs** — sessions persisted to disk, AES-encrypted at rest
- **Custom listen port** — pre-configure your default port in settings
- **Pulse & Wave indicator styles** — additional filament animations beyond the free Flicker mode

---

## How Connections Work

### Direct P2P (default)
One peer **listens** on a UDP port (default 7070). The other peer **dials** their IP:port. STUN is queried automatically to surface your external address when behind NAT.

### Relay Mode
When direct connections aren't possible, both peers connect to a relay server via WebSocket and exchange a shared token. The relay pairs them and forwards encrypted frames transparently — the relay never sees plaintext.

Default relay: `wss://relay.semabuzz.me` (configurable in Settings)

### `buzz://` Deep Links
The app registers the `buzz://` URI scheme. Sharing a `buzz://` link pre-populates the dial dialog so peers can connect with a single click.

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
| `SemaBuzz.Relay` | Self-hostable WebSocket relay server |
| `SemaBuzz.Styles` | Shared XAML styles and color resources |

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
| Packaging | MSIX (Microsoft Store) |
| Min OS | Windows 10 1809+ |

---

## Building

Requirements: Visual Studio 2022 (17.8+) with the **.NET desktop development** workload.

```
git clone https://github.com/SemaBuzz/SemaBuzz-Windows.git
```

Open `SemaBuzz.sln`, set `SemaBuzz.App` as the startup project, and build.

---

## Self-Hosting the Relay

The `SemaBuzz.Relay` project is a standalone .NET 9 WebSocket server. Run it anywhere:

```
cd src/SemaBuzz.Relay
dotnet run
```

Then point clients to your relay URL in **Settings → Relay Server**.

---

## Data & Privacy

All settings and profiles are stored locally in `%APPDATA%\SemaBuzz\`. Nothing is transmitted to any server except the encrypted packets exchanged with your peer (and optionally routed through the relay). The relay server sees only opaque encrypted binary frames.

---

*[SemaBuzz.me](https://semabuzz.me)*