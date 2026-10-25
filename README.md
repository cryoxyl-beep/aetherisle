<div align="center">

# 🏝️ Aetherisle

**A native Dynamic Island for Windows 11 — notch UI, real media, live stats, earbud battery, weather.**

Pure C# / .NET 10 / WPF. No Electron. No web views.

[![License: MIT](https://img.shields.io/badge/License-MIT-0A84FF.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011-0078D4)](https://github.com)
[![UI](https://img.shields.io/badge/UI-WPF-9B4F96)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![Tests](https://img.shields.io/badge/tests-132%2F132-30D158)](#-testing)
[![Release](https://img.shields.io/badge/download-v0.6.0-FF2D55)](https://github.com/cryoxyl-beep/aetherisle/releases/latest)
[![Beta](https://img.shields.io/badge/status-beta-8B5CF6)](https://github.com/cryoxyl-beep/aetherisle/releases)

### ⬇️ Direct download

| File | What it is |
|---|---|
| [**Aetherisle.exe**](https://github.com/cryoxyl-beep/aetherisle/releases/latest/download/Aetherisle.exe) | Single-file, self-contained (~185 MB) — **just run it, no install, no .NET, no admin** |
| [**Aetherisle-win-x64.zip**](https://github.com/cryoxyl-beep/aetherisle/releases/latest/download/Aetherisle-win-x64.zip) | Zipped copy of the same exe |

</div>

> [!IMPORTANT]
> 🟣 **Aetherisle is in BETA.** Each update ships new features and bug fixes — expect rapid
> iteration and the occasional rough edge. Found a bug? [**Open an issue**](https://github.com/cryoxyl-beep/aetherisle/issues).
> Want to help build it? [**Pull requests are welcome**](https://github.com/cryoxyl-beep/aetherisle/pulls).

---

## ✨ What is this?

Aetherisle lives in the strip of screen Windows never uses — the top-center notch zone —
and turns it into a living island:

- **Collapsed pill** — album art · volume (live %, red when muted) · system battery · 12-hour clock · a radial audio visualizer dancing to whatever is playing.
- **Expanded Nook** — now-playing with real source-app badge, transport controls and progress · date & events · **live weather with animated Meteocons Lottie icons**.
- **Media control center** — full player view for Apple Music / Spotify / any SMTC session.
- **Stats** — CPU / GPU / RAM ring gauges, disk, network throughput, uptime, top apps.
- **Earbud battery capsule** — real battery % for allow-listed buds (Airdopes 138, Zeb Duke).

## 📸 Previews — feature by feature

**1 — The island (collapsed pill).** The everyday state: allow-listed album art on the
left, volume icon (live % while adjusting, red when muted), system battery with charge
state, 12-hour clock, and the radial audio visualizer reacting to real system sound.
Drag it anywhere with **Shift + left-drag**; hide it instantly with **Ctrl + Shift**.

![1 — The island pill](docs/previews/1-island-pill.png)

**2 — The media player (hover / click while playing).** Real SMTC data: artwork, title,
artist · app, live waveform, progress bar with elapsed / remaining, and prev / play-pause
/ next transport that only enables what the session supports.

![2 — The media player](docs/previews/2-media-player.png)

**3 — Earbud battery capsule (middle-click or double-tap).** Real HFP/GATT battery
percentages inside the ring; the 3D art ships embedded in the exe.

![3 — Earbud battery capsule](docs/previews/3-earbud-battery.png)

## 🎯 The problems Aetherisle solves

| The problem | How Aetherisle solves it |
|---|---|
| **Overlays block what's underneath.** A top-of-screen bar eats the clicks meant for the browser tab behind it, and hover-expand drives you mad. | Pixel-exact silhouette hit-testing: only the island itself is interactive. Hold **Shift** and it goes full ghost — true OS-level click-through, no hover-expand. |
| **Repositioning overlays is janky.** Config files, registry keys, invisible resize handles. | **Shift + left-drag** anywhere on the island — even while ghosted. Position persists across restarts and respects monitor bounds and DPI. |
| **Now-playing widgets are fake.** They show a static cover or break when you switch apps. | Real SMTC data: artwork, title, artist, transport and timeline from Apple Music, Spotify and any media session Windows knows about. |
| **Bluetooth battery is invisible.** Windows buries it (when the device even reports it). | Four battery channels (AEP × device/container/service + HFP PnP property + GATT) — if Windows can see the %, the island shows it. Middle-click or double-tap. |
| **Widget apps are heavy.** Electron shells idling at half a gigabyte. | One native WPF process. Event-driven services. No browsers, no webviews. |

## 🔥 Features

| | Feature | Details |
|---|---|---|
| 🎵 | **Now playing, honestly** | Real SMTC session data — title, artist, transport, timeline — from Apple Music, Spotify, browsers, anything Windows sees |
| 🎨 | **Brand-logo source badge** | Large real Apple Music / Spotify logo pinned to the artwork corner; other apps get their actual executable icon |
| ⏯ | **Custom transport glyphs** | Bare filled play / pause / next / previous icons (no button chrome) in the Nook mini-player and the full media view |
| 🌤 | **Live weather + Meteocons Lottie** | Real geo-located conditions (open-meteo, IP geolocation with provider fallback), today's high/low, day/night icons — the **official animated Meteocons Lottie files** playing natively |
| 👻 | **Shift = full ghost mode** | True OS-level click-through (`WS_EX_TRANSPARENT`): no hover-expand, clicks and scrolls land on the app beneath |
| 🖱 | **Shift + drag = move** | Reposition anywhere — persists across restarts, DPI-aware, monitor-clamped |
| 🙈 | **Ctrl + Shift = instant hide** | Island vanishes while held, returns the instant either key is released |
| 📈 | **Live system stats** | CPU / GPU / RAM ring gauges with real percentages, disk space, network ↓/↑, uptime, top apps by CPU — performance counters, 2 s refresh |
| 🎧 | **Earbud battery capsule** | Middle-click or double-tap — Airdopes 138 & Zeb Duke allow-listed, four battery channels, honest "not reported" when the device stays silent |
| 📌 | **Pinned state** | Pin the expanded panel; click outside to dismiss |
| 🛡 | **Self-healing window** | Watchdog strips rogue window ownership and recovers from minimize/hide/cloak/off-monitor — the island never stays gone |
| 🤖 | **Agents tab** | Local agent profiles (create/edit/delete, JSON-persisted) — the dock for what's next |

## 🚀 Quick start

1. Download **[Aetherisle.exe](https://github.com/cryoxyl-beep/aetherisle/releases/latest/download/Aetherisle.exe)**
2. Run it — it lives in the tray; the island appears top-center on your primary display
3. Hover it. Play music. Middle-click it. Hold Shift. That's the tour.

```powershell
# from source
git clone https://github.com/cryoxyl-beep/aetherisle.git
cd aetherisle
dotnet build GrumphySwarm.slnx -c Release
src\GrumphySwarm.App\bin\Release\net10.0-windows10.0.26100\GrumphySwarm.exe
```

<details>
<summary><b>📦 Self-contained publish</b> (no .NET install needed — ~185 MB)</summary>

```powershell
dotnet publish src/GrumphySwarm.App/GrumphySwarm.App.csproj `
  -c Release -r win-x64 --self-contained true -o publish/GrumphySwarm
```
</details>

## 🎮 Controls

| Gesture | Action |
|---|---|
| **Hover** the island | Expands the Nook (media · date · weather) |
| **Click** the media card / artwork | Open the full media control center |
| **Click** a nav icon | Nook · Agents · Stats — the battery pill opens Stats |
| **Double-click** / **middle-click anywhere** | Earbud battery capsule |
| **Hold Shift** | Ghost mode — full click-through, no hover-expand |
| **Shift + left-drag** on the island | Move it anywhere, even while ghosted |
| **Hold Ctrl + Shift** | Hide completely until either key is released |
| **Shift + scroll** over the island | Open the media control center while staying click-through |
| **Pin** (lock icon) | Keep the panel open; click outside to unpin |
| Tray icon → Exit | Clean shutdown |

## 🧪 Testing

132 xUnit tests cover the pure logic: the interaction state machine, silhouette
geometry and layout math, media allow-lists, playback clock interpolation, weather
code → label/icon mapping, stats formatting, transient overlays, settings
persistence and agent profile storage.

```powershell
dotnet test GrumphySwarm.slnx
```

## 🏗 Architecture

- **GrumphySwarm.Core** — pure, testable logic: state machine, layout math, presenters, allow-lists, FFT.
- **GrumphySwarm.App** — WPF host: native interop (`WS_EX_NOACTIVATE`/`TOOLWINDOW`/`TOPMOST`, per-pixel hit-testing, LL hooks for gestures), event-driven monitors (Core Audio, SMTC, WMI brightness, Bluetooth AEP/HFP/GATT, performance counters), and the island control itself.
- **No fake data.** Anything a device or driver won't report is shown as absent — never invented.

## 🗺 Roadmap

- Real calendar/event provider for the Nook events column
- Settings surface (the gear is waiting)
- Desktop pet for the reserved Nook slot
- Agent runtime for the Agents tab

## 🤝 Contributing

Bug reports → [issues](https://github.com/cryoxyl-beep/aetherisle/issues) · code → [pull requests](https://github.com/cryoxyl-beep/aetherisle/pulls).
Keep it native (WPF/.NET), keep the no-fake-data rule, and run the tests.

## 📄 License

[MIT](LICENSE) — weather icons by [Meteocons](https://bas.dev/work/meteocons) (MIT, Bas Milius).

<div align="center">

**[⬇ Download the latest release](https://github.com/cryoxyl-beep/aetherisle/releases/latest)**

</div>
