# ⚡ SemaBuzz for Windows

![License](https://img.shields.io/badge/license-AGPL--3.0-blue?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D6?style=flat-square&logo=windows&logoColor=white)
![Encryption](https://img.shields.io/badge/encryption-AES--256--GCM%20%2B%20ECDH--P256-22c55e?style=flat-square&logo=letsencrypt&logoColor=white)
![WebSocket](https://img.shields.io/badge/relay-WebSocket-f97316?style=flat-square)

> A private, encrypted 1-to-1 messaging app for Windows.  
> No accounts. No cloud storage. Just a wire between two people.

---

## What Is SemaBuzz?

SemaBuzz is a free Windows desktop app that lets two people communicate in real time over an end-to-end encrypted connection. Text streams **character-by-character** as you type — the other person sees your thoughts form live on their screen. A visual filament indicator pulses with the rhythm of incoming keystrokes, making the connection feel alive.

Every session generates fresh encryption keys on both devices using **ECDH P-256**. All traffic is encrypted with **AES-256-GCM** before it leaves your machine. The relay server — which you or your peer must self-host — forwards only opaque bytes it can never read. When the session ends, nothing is retained anywhere.

---

## 💬 Why SemaBuzz?

I grew up on PowWow, ICQ, and MSN Messenger — there was something magical about that era of direct, personal chat that modern apps have completely lost. I built SemaBuzz to bring that feeling back.

You'll find a lot of the classics: URL Walk, a shared whiteboard, file transfer, and rich status changes with custom status messages — things PowWow and ICQ fans will recognize instantly.

Then we added our own spin: every session generates fresh encryption keys via token-based E2E chat, with a private relay that literally cannot read your messages. And the live-typing preview — that flowing filament line showing characters as they're typed — is our love letter to what made those old programs feel so alive.

Fully open-source (AGPL-3.0). Would love to hear from anyone else who misses that era. 👾

---

## ✨ Features

- ⌨️ **Live-wire typing** — keystrokes stream in real time; watch the other person compose character by character
- 💡 **BuzzIndicator** — a filament that pulses and glows with incoming activity, even when preview is off
- ⚡ **Buzz alerts** — send an instant pulse to get your peer's attention without typing
- 📁 **File transfer** — send files via the relay; SHA-256 verified on receipt
- 🎨 **Shared whiteboard** — real-time collaborative drawing board alongside chat
- 🌐 **Walk Web** — push a URL to your peer as a clickable card instantly
- 😄 **Emoji picker** — 100+ emoji across 7 categories with full colour rendering
- 👤 **Profiles** — named handles with optional avatars, saved locally and switched at any time
- 🖌️ **Themes** — choose from a library of dark and light themes with live preview
- 🟢 **Status** — set Available, Away, or Busy with an optional custom message
- 🔔 **System tray** — minimize to tray; live sessions continue in the background
- 📌 **Always on Top** — keep SemaBuzz floating above other windows
- 🔒 **Strong encryption** — ephemeral ECDH P-256 + AES-256-GCM; the relay never holds keys

---

## ⚙️ How It Works

1. One person clicks **CREATE A BUZZ** — a unique 6-character code appears.
2. They share that code with the other person by any means (text, email, etc.).
3. The other person enters the code and clicks **JOIN**.
4. Both sides are connected and encrypted. Close the app and the session is gone forever.

The relay server pairs the two clients by code and forwards their encrypted frames. It never sees plaintext.

---

## 📋 Requirements

- Windows 10 version 1809 (build 17763) or later
- A self-hosted [SemaBuzz Relay](https://github.com/skynrlabs/SemaBuzz-Relay) — you or your peer must run one

---

## 🔧 Building from Source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download) with the **Windows desktop development** workload.

```
git clone https://github.com/skynrlabs/SemaBuzz.git
cd SemaBuzz
dotnet build SemaBuzz.sln -c Debug
```

Output lands in `build/Debug/`. Set `SemaBuzz.App` as the startup project in Visual Studio to run with F5.

---

## 📂 Project Structure

| Project | Role |
|---|---|
| `SemaBuzz.App` | WPF UI — windows, dialogs, theming, indicator |
| `SemaBuzz.Styles` | Shared XAML styles and colour resources |
| [`SemaBuzz.Protocol`](https://github.com/skynrlabs/SemaBuzz-Protocol) | Wire protocol, encryption, relay client (NuGet package) |

---

## 🛠️ Tech Stack

| | |
|---|---|
| Language | C# 12 |
| Runtime | .NET 9 |
| UI Framework | WPF |
| Networking | WebSocket (relay) |
| Encryption | ECDH P-256 + AES-256-GCM |
| Min OS | Windows 10 1809 (build 17763) |

---

## 🖥️ Self-Hosting the Relay

SemaBuzz requires a relay server. Download a self-contained binary from the [SemaBuzz Relay releases](https://github.com/skynrlabs/SemaBuzz-Relay/releases/latest):

| Platform | File |
|---|---|
| Linux x64 | `SemaBuzz.Relay-linux-x64.tar.gz` |
| Linux ARM64 | `SemaBuzz.Relay-linux-arm64.tar.gz` |
| Windows x64 | `SemaBuzz.Relay-win-x64.zip` |

Run it:

```bash
# Linux
./SemaBuzz.Relay

# Windows
.\SemaBuzz.Relay.exe
```

Default port is **7171**. Override with the `PORT` environment variable. Enter `ws://your-server:7171/relay` in SemaBuzz's Settings → Preferences.

To verify the relay is running, open `http://your-server:7171` in a browser — you should see a health response. "WebSocket upgrade required" is also a valid response and means the relay is up.

---

## 🔐 Privacy

The relay is a blind pass-through. It does not log, read, or store message content. All traffic is encrypted on-device before it reaches the relay.

The app stores only your preferences and profiles locally in `%APPDATA%\SemaBuzz\`. Nothing is transmitted to any server except the encrypted packets exchanged with your peer via the relay.

--

## 💬 Community

Join the [SemaBuzz Discord](https://discord.gg/rJMQ2cfN) to ask questions, share what you're building, and follow development.

## 🤝 Contributing

PRs and issues are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

The repo uses a two-branch model:

| Branch | Purpose |
|---|---|
| `main` | Stable, release-ready |
| `dev` | Integration target — all PRs merge here first |

## 📄 License

SemaBuzz for Windows is open-source software licensed under the **GNU Affero General Public License v3.0**. Copyright © 2026 Skynr Labs.

You are free to use, modify, and distribute this software under the terms of the AGPL-3.0. Any modified version distributed or run as a network service must also be released under the AGPL-3.0. See [LICENSE](LICENSE) for full terms.

The [SemaBuzz Protocol](https://github.com/skynrlabs/SemaBuzz-Protocol) is AGPL-3.0 licensed.  
The [SemaBuzz Relay](https://github.com/skynrlabs/SemaBuzz-Relay) is MIT licensed.

---

[semabuzz.me](https://semabuzz.me) &nbsp;·&nbsp; [GitHub Sponsors](https://github.com/sponsors/skynrlabs)



