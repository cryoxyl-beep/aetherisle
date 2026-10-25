<div align="center">

# 🌘 Aetherisle

### A native Dynamic Island for Windows 11 — living on your desktop, not in a browser.

**C# · .NET 10 · WPF · zero Electron · zero web views**

[![License: MIT](https://img.shields.io/badge/License-MIT-0A84FF.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011-0078D4)](https://github.com)
[![UI](https://img.shields.io/badge/UI-WPF-9B4F96)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![Tests](https://img.shields.io/badge/tests-96%2F96-30D158)](#testing)
[![Release](https://img.shields.io/badge/download-v0.3.3-FF2D55)](https://github.com/cryoxyl-beep/aetherisle/releases/latest)

### ⬇️ [**Download Aetherisle.exe**](https://github.com/cryoxyl-beep/aetherisle/releases/latest/download/Aetherisle.exe) — double-click and run. No install, no .NET, no admin.

*(or grab the [full build as a zip](https://github.com/cryoxyl-beep/aetherisle/releases/latest/download/Aetherisle-win-x64.zip))*

> **🚧 Status: Active Development.** Aetherisle ships early and improves fast.
> Current builds are stable for daily use, but this is a young project — if you
> hit a bug, [open an issue](https://github.com/cryoxyl-beep/aetherisle/issues);
> fixes and improvements land in
> [upcoming releases](https://github.com/cryoxyl-beep/aetherisle/releases).

*A pure-black capsule that floats on your screen — with real album art, a live audio
visualizer, a full media player, volume/brightness/battery glanceables, and Bluetooth
earbud status. Built like a system component, not a webpage.*

</div>

---

## 🎯 The problems Aetherisle solves

Windows has no Dynamic Island — and the usual workarounds all hurt. Aetherisle exists to fix each of those pains natively:

| The problem | How Aetherisle solves it |
|---|---|
| **Media controls live in buried windows.** Changing a track means alt-tabbing and hunting for Spotify / Apple Music / the right browser tab. | The island surfaces the *actual* system media session — title, artist, artwork, live progress bar and transport buttons — from any app publishing one, without stealing focus from what you're doing. |
| **Now-playing widgets are fake or ugly.** Placeholder art, static icons, visualizers that just wiggle on a timer. | Real SMTC metadata, real album artwork (allow-listed to your player apps), and a genuine WASAPI-loopback FFT visualizer reacting to the audio your PC is producing — flat in silence, alive when sound plays. |
| **Overlays block what's underneath.** A top-of-screen bar eats the clicks meant for the browser tab behind it, and hover-expand drives you mad. | Pixel-exact silhouette hit-testing: only the island itself is interactive. Hold **Shift** and the island goes full ghost — click-through, no hover-expand — so the tab beneath stays perfectly usable. |
| **Repositioning overlays is janky** — config files, registry keys, or "grab the invisible resize handle". | Hold **Shift + left-drag** anywhere on the island: Windows' own native drag moves it, collapsed or expanded, and the position survives restarts. |
| **Volume / brightness / battery feedback is scattered** across flyouts, taskbar corners and the Settings app. | One glance in the pill: volume icon → live % while adjusting → back (red when muted), brightness flashes, system battery with charge state — event-driven, no polling lag. |
| **Bluetooth earbud battery is invisible.** Windows shows it in Settings, but no lightweight overlay surfaces it. | Middle-click anywhere (or double-tap the island) for the battery capsule — the same Hands-Free-Profile battery source Windows Settings reads, with 3D device art and the % inside the ring. |
| **"Desktop widget" usually means Electron.** Hundreds of MB of Chromium for a clock. | Pure native WPF on .NET 10 — one process, GPU-composited UI, no browser, no web view, no JS runtime. |

---

## 📸 Previews — feature by feature

**1 — The island (collapsed pill).** The everyday state: allow-listed album art on the
left, volume icon (live % while adjusting, red when muted), system battery with charge
state, 12-hour clock, and the radial audio visualizer reacting to real system sound.
Drag it anywhere with **Shift + left-drag**.

![1 — The island pill](docs/previews/1-island-pill.png)

**2 — The media player (hover / click while playing).** Real SMTC data: artwork, title,
artist · app, live waveform, progress bar with elapsed / remaining, and prev / play-pause
/ next transport that only enables what the session supports.

![2 — The media player](docs/previews/2-media-player.png)

**3 — The earbud battery capsule (middle-click or double-tap).** The same
Hands-Free-Profile battery Windows Settings reads — 3D device art, connection state and
the percentage right inside the ring. Allow-listed to your own devices.

![3 — Earbud battery capsule](docs/previews/3-earbud-battery.png)

---

## ✨ Feature tour

| | Feature | How |
|---|---|---|
| 🎵 | **Live media island** | Real SMTC data: title, artist, artwork, transport controls, progress bar with elapsed / remaining — from Spotify, Apple Music, browsers and any app that publishes a media session |
| 🎨 | **Album art, allow-listed** | Artwork shows for Spotify, Apple Music and web players only — one obvious file to edit ([MediaAllowList.cs](src/GrumphySwarm.Core/Media/MediaAllowList.cs)) |
| 📊 | **Real audio visualizer** | WASAPI loopback capture + custom FFT — the bars dance with *actual* system audio (radial in the pill, linear in the player) and go flat in silence. No fake animations |
| 🔊 | **Volume glance** | Icon → live % while you change it → back to icon. Muted = red icon. All event-driven Core Audio |
| 🔆 | **Brightness glance** | WMI event-driven; DDC/CI fallback for external monitors |
| 🔋 | **System battery** | Charge %, charging bolt, green/amber/red — between volume and clock |
| 📈 | **Live system stats** | New Stats tab: CPU, GPU, disk, RAM with bars, network ↓/↑, uptime and top apps by CPU — real performance counters, 2-second refresh, unavailable hardware hidden honestly |
| 🎧 | **Earbud battery capsule** | Middle-click anywhere or double-tap the island — Airdopes 138 & Zeb Duke allow-listed, four battery channels polled (AEP × device/container/service + GATT) |
| 🖱 | **Free repositioning** | **Ctrl + left-drag** the island anywhere via Windows native drag — collapsed or expanded; position persists across restarts |
| 👻 | **Shift = full ghost mode** | The whole island becomes click-through: no hover-expand, every click and scroll lands on the app beneath — no need to move the island |
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
| **Click** the pill (or the media card / artwork) | Open the media player — always an explicit action; hover never hijacks the view |
| **Double-click** / **middle-click anywhere** | Earbud battery capsule |
| **Hold Shift + scroll** over the island | Open the media player while the island stays click-through |
| **Ctrl + left-drag on the island** | Move it anywhere — Windows' own native drag, works collapsed or expanded |
| **Hold Shift** | Ghost mode: no hover-expand, and clicks around the island reach the tab beneath; on the island, left-drag moves it |
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
