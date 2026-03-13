# PullToOBS

![PullToOBS icon](PullToOBS/PullToOBS.png)

A Dalamud plugin that automatically controls OBS recording during FFXIV encounters. Captures a replay buffer clip before pulls and full encounter recordings via OBS WebSocket v5 and IINACT.

## Features

* **Automatic Recording**
  * Starts OBS recording when combat begins (detected via IINACT IPC)
  * Stops recording after a 5-second grace period when combat ends
  * Re-entering combat cancels the pending stop, producing one continuous recording

* **Replay Buffer Integration**
  * Automatically starts the OBS replay buffer on connect
  * Saves the replay buffer 5 seconds into the encounter to capture the prepull
  * Result: two files per encounter -- a replay buffer clip (prepull) and a full recording

* **Visual Status Indicator**
  * Always-visible on-screen indicator showing OBS state
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
  * Persistent configuration

## Requirements

* [OBS Studio](https://obsproject.com/) with WebSocket v5 enabled (Settings > WebSocket Server)
* [IINACT](https://github.com/marzent/IINACT) Dalamud plugin (provides combat event data)
* Replay Buffer configured in OBS (Settings > Output > Replay Buffer) for prepull capture

## Installation

PullToOBS is not yet available in the standard Dalamud plugin repository and must be installed from my third party repository.

To install it, add the following URL in Dalamud settings (`/xlsettings` > Experimental > Custom Plugin Repositories):

```
https://raw.githubusercontent.com/Miu-B/PullToOBS/master/repo.json
```

Then open the Plugin Installer (`/xlplugins`) and search for **PullToOBS**.

## How To Use

### Getting Started

1. Install and enable IINACT in the Dalamud plugin installer
2. Enable OBS WebSocket v5 (OBS > Tools > WebSocket Server Settings)
3. Configure Replay Buffer in OBS (Settings > Output > Replay Buffer)
4. Open PullToOBS config with `/pulltoobs` or `/pto`
5. Enter your OBS WebSocket URL and password, then click Connect
6. The indicator will appear on screen showing connection status
7. Enter combat -- recording starts automatically

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

All settings are saved automatically:

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
