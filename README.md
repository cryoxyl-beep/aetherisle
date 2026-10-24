<div align="center">

# 🌘 Aetherisle

### A native Dynamic Island for Windows 11 — living on your desktop, not in a browser.

**C# · .NET 10 · WPF · zero Electron · zero web views**

[![License: MIT](https://img.shields.io/badge/License-MIT-0A84FF.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011-0078D4)](https://github.com)
[![UI](https://img.shields.io/badge/UI-WPF-9B4F96)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![Tests](https://img.shields.io/badge/tests-94%2F94-30D158)](#testing)

*A pure-black capsule that floats on your screen — with real album art, a live audio
visualizer, a full media player, volume/brightness/battery glanceables, and Bluetooth
earbud status. Built like a system component, not a webpage.*

</div>

---

## ✨ What it does

| | Feature | How |
|---|---|---|
| 🎵 | **Live media island** | Real SMTC data: title, artist, artwork, transport controls, progress bar with elapsed / remaining — from Spotify, Apple Music, browsers and any app that publishes a media session |
| 🎨 | **Album art, allow-listed** | Artwork shows for Spotify, Apple Music and web players only — one obvious file to edit ([MediaAllowList.cs](src/GrumphySwarm.Core/Media/MediaAllowList.cs)) |
| 📊 | **Real audio visualizer** | WASAPI loopback capture + custom FFT — the bars dance with *actual* system audio (radial in the pill, linear in the player) and go flat in silence. No fake animations |
| 🔊 | **Volume glance** | Icon → live % while you change it → back to icon. Muted = red icon. All event-driven Core Audio |
| 🔆 | **Brightness glance** | WMI event-driven; DDC/CI fallback for external monitors |
| 🔋 | **System battery** | Charge %, charging bolt, green/amber/red — between volume and clock |
| 🎧 | **Earbud battery capsule** | Middle-click anywhere or double-tap the island — Airdopes 138 & Zeb Duke allow-listed, four battery channels polled (AEP × device/container/service + GATT) |
| 🖱 | **Free repositioning** | **Shift + drag** the island anywhere — in collapsed or expanded mode; position persists across restarts |
| 👻 | **Shift = ghost mode** | Hold Shift to make the island fully click-through — hover stops expanding it, and clicks reach the browser tab underneath |
| 📌 | **Pinned state** | Pin the expanded panel; click outside to dismiss |
| 📅 | **Nook tab** | Media card · mini-calendar (real dates) · pet slot reserved |
| 🤖 | **Agents tab** | Local agent profiles (create/edit/delete, JSON-persisted) — the runtime dock for what's next |

## 🚀 Quick start

```powershell
git clone https://github.com/cryoxyl-beep/aetherisle.git
cd aetherisle
dotnet build GrumphySwarm.slnx -c Release
src\GrumphySwarm.App\bin\Release\net10.0-windows10.0.26100\GrumphySwarm.exe
```

<details>
<summary><b>📦 Self-contained publish</b> (no .NET install needed — ~182 MB)</summary>

```powershell
dotnet publish src/GrumphySwarm.App/GrumphySwarm.App.csproj `
  -c Release -r win-x64 --self-contained true -o publish/GrumphySwarm
```
</details>

## 🎮 Controls

| Gesture | Action |
|---|---|
| **Hover** the island | Expands (media player if something is playing, Nook/Agents otherwise) |
| **Click** the pill | Jump straight into the media player |
| **Double-click** / **middle-click anywhere** | Earbud battery capsule |
| **Shift + left-drag on the island** | Move it anywhere — works collapsed or expanded |
| **Hold Shift** | Ghost mode: island is click-through and won't expand (perfect for reaching browser tabs underneath) |
| **Pin** button | Keep the panel open; click outside to unpin |
| Tray icon → Exit | Clean shutdown |

## 🏗 Architecture

```
GrumphySwarm.slnx
├── src/GrumphySwarm.Core/          # pure, UI-free logic — fully unit-tested
│   ├── Audio/SpectrumAnalyzer.cs   # hand-rolled FFT + playback clock
│   ├── Media/MediaAllowList.cs     # ✏️ your allow-lists live here
│   ├── Layout/                     # capsule geometry + DPI placement math
│   ├── Monitors/                   # contracts, transient status, presenters
│   └── State/ Agents/ Settings/ Time/
├── src/GrumphySwarm.App/           # WPF host (net10.0-windows10.0.26100)
│   ├── Controls/NotchIsland.*      # 4-layer island + deterministic animator
│   ├── Services/Audio/             # Core Audio volume + WASAPI loopback FFT
│   ├── Services/Bluetooth/         # 4-channel earbud battery
│   ├── Services/Brightness/ Media/ Power/
│   └── ViewModels/ Views/ Themes/ Interop/
└── tests/GrumphySwarm.Tests/       # 94 xUnit tests
```

**Native window behavior:** `WS_EX_NOACTIVATE` (never steals keyboard focus), `WS_EX_TOOLWINDOW` (invisible to Alt+Tab), silhouette-exact `WM_NCHITTEST` click-through, per-monitor-v2 DPI, single instance, tray menu, background file logging with 7-day rotation.

## ⚠️ Honest limitations

- **Earbud battery %**: Airdopes 138 exposes no battery through any public Windows channel (AEP device/container/service and GATT Battery Service were all probed live — Windows Settings reads a vendor channel that isn't exposed to apps). The capsule shows "Connected" honestly; if the device or a Windows build ever reports it, the green ring lights up with zero code changes. The **Zeb Duke** may behave differently — try it.
- **Browser artwork**: browsers don't tell the OS which site is playing, and Aetherisle never scrapes windows — browser sessions are trusted as your web player (music.apple.com). Remove browser tokens in the allow-list to disable.
- SMTC positions are event-based; the progress bar interpolates by wall clock between reports.
- No multi-monitor hosting yet; the island lives on the primary display.

## 🧪 Testing

```powershell
dotnet test GrumphySwarm.slnx    # 94/94 — state machine, FFT math, silhouette,
                                 # allow-lists, playback clock, persistence
```

## 🗺 Roadmap

- [ ] Desktop pets 🐾 (the reserved slot is waiting)
- [ ] Agent runtime + live status feed into the Agents tab
- [ ] Settings window (delays, modules, allow-list editor)
- [ ] Composition-powered shadows & multi-monitor islands

## 📄 License

[MIT](LICENSE) — © 2026 cryoxyl-beep. Ship it, fork it, make it yours.

> The executable keeps its original working name `GrumphySwarm.exe` — everything else here is Aetherisle.
