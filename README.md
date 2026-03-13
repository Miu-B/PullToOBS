# PullToOBS

![PullToOBS icon](PullToOBS/PullToOBS.png)

If you're anything like me, you've had that moment — the pull goes perfectly, you finally clear, and then you realise OBS was sitting there doing absolutely nothing because you forgot to press Record. Again.

I already had [rec-cue](https://github.com/Miu-B/rec-cue) to show me an in-game indicator when recording was active, which was great for noticing the problem... but not so great at *preventing* it. So I built PullToOBS: a Dalamud plugin that talks to OBS over WebSocket v5, starts recording the moment you enter combat, and saves a replay buffer clip of the prepull for good measure. No more "I forgot to record" sadness.

## Features

* **Automatic Recording** -- the whole point
  * Starts OBS recording when combat begins (detected via Dalamud)
  * Stops recording after a 5-second grace period when combat ends
  * If you re-enter combat during that grace period, the pending stop is cancelled and you get one continuous recording instead of two fragments

* **Replay Buffer Integration** -- never miss the prepull
  * Automatically starts the OBS replay buffer when the plugin connects
  * Saves the replay buffer 5 seconds into the encounter, capturing everything that happened before the pull
  * You'll end up with two files per encounter: a replay buffer clip (prepull) and a full recording

* **Visual Status Indicator** -- know what OBS is doing at a glance
  * Always-visible on-screen dot showing the current OBS state
  * **Red pulsing dot**: Recording in progress
  * **Orange dot**: Replay buffer active (connected, not recording)
  * **Green dot**: Connected to OBS
  * **Grey dot**: Not connected
  * Draggable when the config window is open
  * Adjustable scale (0.5x - 2.0x)

* **Simple Configuration**
  * OBS WebSocket URL and password
  * Optional auto-connect on plugin start
  * Indicator position, scale, and visibility settings
  * All settings are saved automatically

## Companion Tool

Since PullToOBS produces two files per encounter (a replay buffer clip and the full recording), you'll probably want to stitch them together afterwards. [limitcut](https://github.com/Miu-B/limitcut) does exactly that -- it finds where the two recordings overlap using audio cross-correlation and combines them into a single MP4, no manual trimming required.

## Requirements

* [OBS Studio](https://obsproject.com/) with WebSocket v5 enabled (Settings > WebSocket Server)
* Replay Buffer configured in OBS (Settings > Output > Replay Buffer) for prepull capture

## Installation

PullToOBS isn't in the standard Dalamud plugin repository yet, so you'll need to add it as a third-party repo.

Add this URL in Dalamud settings (`/xlsettings` > Experimental > Custom Plugin Repositories):

```
https://raw.githubusercontent.com/Miu-B/PullToOBS/master/repo.json
```

After that, open the Plugin Installer (`/xlplugins`), search for **PullToOBS**, and install it.

## How To Use

### Getting Started

1. Enable OBS WebSocket v5 (OBS > Tools > WebSocket Server Settings)
2. Set up a Replay Buffer in OBS (Settings > Output > Replay Buffer) -- this is what captures the prepull
3. Open PullToOBS config with `/pulltoobs` or `/pto`
4. Enter your OBS WebSocket URL and password, then click Connect
5. The indicator shows up on screen -- you're good to go
6. Enter combat and recording starts automatically

### Commands

* `/pulltoobs` or `/pto` - Toggle the configuration window
* `/pulltoobs show` or `/pto show` - Show the indicator
* `/pulltoobs hide` or `/pto hide` - Hide the indicator

### Adjusting Indicator

1. Open the configuration window with `/pulltoobs`
2. While the configuration window is open, drag the indicator to your desired position
3. Use the "Indicator Scale" slider to adjust size
4. Position is saved automatically when you finish dragging

## Configuration

All settings are saved automatically, so you can just set things up once and forget about it (forgetting is what we're good at, after all):

* **WebSocket URL** - OBS WebSocket server address (default: `ws://localhost:4455`)
* **Password** - OBS WebSocket server password
* **Auto-connect on start** - Automatically connect to OBS when the plugin loads
* **Indicator Scale** - Scale multiplier for the indicator (0.5x to 2.0x)
* **Hide Indicator** - Toggle indicator visibility

## Development

### Building

```bash
dotnet build --configuration Release
```

### Running Tests

```bash
dotnet test
```

### Loading in Dalamud (Dev)

1. Launch the game and use `/xlsettings` to open Dalamud settings
2. Go to `Experimental` and add the full path to `PullToOBS.dll` to Dev Plugin Locations
3. Use `/xlplugins` to open the Plugin Installer
4. Go to `Dev Tools > Installed Dev Plugins` and enable PullToOBS
5. Use `/pulltoobs` to open the configuration window

## License

AGPL-3.0-or-later

## Credits

Based on [SamplePlugin](https://github.com/goatcorp/SamplePlugin) template by goatcorp
