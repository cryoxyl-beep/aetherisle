# Celestia — Dynamic Island for Windows 11

**Celestia** is a native Dynamic Island for Windows 11, built with **C# / .NET 10 / WPF** —
no Electron, no web view, no second UI framework. A pure-black capsule floats at the
top of your screen with live album art, a real audio-spectrum visualizer, system
volume/brightness/battery status, a full media player, and Bluetooth earbud battery
monitoring.

> The app assembly is `GrumphySwarm.exe` (the project's original working name);
> everything else is Celestia.

---

## What's in this milestone

### Dynamic Island silhouette (capsule, no flares)
- The island is one capsule geometry (all four corners rounded) built from
  unit-tested math (`NotchSilhouetteMath`), shared by fill, content clip,
  shadow layer and precise `WM_NCHITTEST` hit-testing.
- Four content layers morph inside one silhouette: **pill → media player /
  Nook–Agents panel → earbud battery capsule**, with the single-owner
  transition controller (no competing animations, deterministic interrupts).

### Collapsed pill (reference layout)
- **Allow-listed album art** (left) · volume glyph · transient volume/brightness
  status (center) · **live audio visualizer bars** (right) · clock.
- The visualizer is REAL signal processing: WASAPI loopback capture + FFT
  (`SpectrumAnalyzer`), so bars dance with any system audio and decay to flat
  in silence. No fake animation.

### Media player expanded view (reference layout)
Hovering/clicking the pill during playback expands into the player: artwork
(rounded), title, artist · app, live visualizer, progress bar with elapsed /
remaining times (real SMTC timeline + 1 s interpolation), and prev /
play-pause / next / output controls — enabled only when the session supports
them. No session → the Nook/Agents panel shows instead.

### Allow-lists (edit in `src/GrumphySwarm.Core/Media/MediaAllowList.cs`)
- **Album art**: Spotify, Apple Music/iTunes, and browsers as web players
  (music.apple.com). Browsers don't expose the tab URL to the OS and the app
  never scrapes windows — so a browser session is trusted as your web player.
  Every other app shows text only.
- **Earbuds**: `airdopes 138`, `zeb duke`.

### Earbud battery capsule
- **Middle mouse button anywhere**, or **double-tap the island** → capsule with
  earbuds icon, device name, and a battery ring.
- Data comes from the same Windows Bluetooth AEP properties the Settings app
  uses (`IsConnected`, `AepBattery_Level`), push-based via DeviceWatcher with a
  property-list fallback chain for different Windows builds.
- Honest hardware caveat: whether a battery percentage exists depends on the
  earbuds. **Airdopes 138 is detected and shows "Connected" but does not report
  a battery level over standard Windows properties** — the ring stays empty
  rather than showing an invented number. Devices that do report it show the
  green ring.

### Everything from earlier milestones still works
Volume + brightness transients (Core Audio / WMI event-driven), Nook
(media card · calendar · pet slot) and Agents (local profiles, JSON-persisted,
editor window) tabs, pinning, tray menu, single instance, file logging.

## Project structure

```
GrumphySwarm.slnx
├── src/GrumphySwarm.Core/
│   ├── Audio/SpectrumAnalyzer.cs      # FFT + playback clock (pure, tested)
│   ├── Media/MediaAllowList.cs        # the user's allow-lists (edit here)
│   ├── Layout/                        # capsule math + window placement
│   ├── Monitors/                      # contracts + transient controller
│   └── State/ Agents/ Settings/ Time/ Messaging/
├── src/GrumphySwarm.App/
│   ├── Controls/NotchIsland.*         # 4-layer island + transition controller
│   ├── Controls/AudioVisualizerControl.cs · BatteryRingControl.cs
│   ├── Rendering/NotchSilhouetteBuilder.cs
│   ├── Services/Audio/                # volume (Core Audio) + loopback FFT
│   ├── Services/Brightness/ · Bluetooth/ · Media/
│   └── ViewModels/ Views/ Themes/
├── tests/GrumphySwarm.Tests/          # 94 xUnit tests
└── publish/GrumphySwarm/              # self-contained output
```

## Run / test / publish

```powershell
dotnet build GrumphySwarm.slnx -c Debug
src\GrumphySwarm.App\bin\Debug\net10.0-windows10.0.26100\GrumphySwarm.exe

dotnet test GrumphySwarm.slnx            # 94 tests

dotnet publish src/GrumphySwarm.App/GrumphySwarm.App.csproj `
  -c Release -r win-x64 --self-contained true -o publish/GrumphySwarm
# → publish\GrumphySwarm\GrumphySwarm.exe (~182 MB, no runtime install, no admin)
```

### Controlling the island

- **Drag** the island anywhere on the screen — the position persists across restarts.
- **Click** the pill during playback → media player view.
- **Double-click** the island (or **middle-click anywhere**) → earbud battery capsule.
- **Hover** → pill expands; leaving collapses it; pin it from the Nook/Agents panel.

## Verified live on this machine (2026-10-24)

- Capsule silhouette screenshots (pill, player, battery) — clean edges, flush top.
- Spotify / Apple Music playing → real artwork in pill AND player, real timeline,
  transport buttons working, visualizer dancing to actual output.
- Collapsed center group: volume icon → live % during changes → back; red muted
  icon; system battery % with charge state; clock; radial visualizer.
- Island dragging with persisted position.
- Middle-click **and** double-tap → battery capsule ("Airdopes 138 · Connected").
- 94/94 tests in Debug and Release, published exe verified.

## Manual checks worth doing

1. Play music in Chrome at music.apple.com — art should appear (web allow-list).
2. Play something NOT in the allow-list (e.g. VLC) — text only, no artwork.
3. Listen for the visualizer reacting to ANY system audio (YouTube, games…).
4. Connect the Zeb Duke — it should appear in the battery capsule too.
5. Progress bar advances every second while playing and resyncs on track seek.
6. Pinning, tray menu, second-launch activation, Alt+Tab exclusion.

## Known limitations / deferred

- Airdopes 138 firmware does not expose battery over standard Windows
  properties → capsule shows "Connected" only (never a fabricated %).
- Browser tab URLs are invisible to the OS; the web-player allow-list trusts
  the browser generally (documented in `MediaAllowList`).
- SMTC timeline is event-based; position advances by wall-clock between events
  (resyncs on every report). Seeking is not exposed yet.
- Media expanded view replaces Nook/Agents while a session is active; pin the
  panel before playing (or stop playback) to reach the tabs.
- Agent runtime, desktop pets, settings window: future milestones.
