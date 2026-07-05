# Gold Rush Mod Menu

A BepInEx plugin for **Gold Rush: The Game** that adds an in-game overlay menu for modifying resources, the gold market price, and random gold nugget spawning — with full stealth support for leaderboard sessions.

---

## Features

| Category | What it does |
|---|---|
| **Cash** | Set, trickle, or quick-add money at a configurable $/s rate |
| **Gold Inventory** | Set, trickle, or quick-add gold oz at a configurable oz/s rate |
| **Diamonds** | Set or add gems |
| **Gold Stock Price** | Lock the market price, run stock-style declines at adjustable speed |
| **Nugget Limit** | Raise the random gold nugget cap beyond the game default of 60 |
| **Stealth Mode** | All patches auto-disable during leaderboard sessions; menu hides itself |
| **Trickle System** | Resources rise gradually instead of jumping, for believable growth |
| **Hotkeys** | Add resources without opening the menu |
| **Persistent Config** | All settings saved to disk and restored on next launch |
| **Collapsible UI** | Each section collapses; scrollable window works at any resolution |

---

## Requirements

- **Gold Rush: The Game** (Steam, Windows)
- **Windows 10 or 11 (64-bit)**
- **.NET / Visual C++ runtime** — already installed on most Windows machines

---

## Installation (Pre-built — recommended)

1. **Download** the latest `GoldRushModMenu_vX.X.X.zip` from the [Releases](../../releases) page.

2. **Find your game folder.**
   - Open Steam → right-click *Gold Rush: The Game* → *Manage* → *Browse local files*
   - The folder will look something like:
     ```
     C:\Program Files (x86)\Steam\steamapps\common\Gold Rush The Game\
     ```
     or on a secondary drive:
     ```
     D:\SteamLibrary\steamapps\common\Gold Rush The Game\
     ```

3. **Extract the ZIP** directly into that game folder, keeping the folder structure intact.  
   After extracting you should see these new files alongside `GoldMiningSimulator.exe`:

   ```
   GoldMiningSimulator.exe          <- already here
   winhttp.dll                      <- added by mod
   doorstop_config.ini              <- added by mod
   BepInEx\
     core\       (BepInEx runtime)  <- added by mod
     plugins\
       GoldRushModMenu.dll          <- added by mod
   ```

4. **Launch the game normally** through Steam. A BepInEx console window will briefly appear — this is normal.

5. **Press F10** in-game to open the mod menu.

---

## Hotkeys

| Key | Action |
|---|---|
| `F10` | Open / close mod menu |
| `Numpad 4` | +$50,000 cash (trickle) |
| `Numpad 5` | +$250,000 cash (trickle) |
| `Numpad 6` | +500 oz gold (trickle) |
| `Numpad 7` | +2,500 oz gold (trickle) |

Hotkeys are disabled automatically during leaderboard sessions.

---

## Trickle System

Instead of resources jumping instantly (which looks suspicious in recordings or spectator view), the trickle system adds money and gold gradually at a rate you control:

- Default cash rate: **$5,000 / second**
- Default gold rate: **50 oz / second**

You can change these rates in the mod menu under each section and they are saved to the config file.

Use **"Trickle"** to gradually reach a typed target value, or the quick-add buttons for a relative increase.  
Use **"Set Now"** if you need an instant change.

---

## Leaderboard / Stealth Mode

When the game detects a leaderboard/ranked session:

- All Harmony patches are **silently disabled** — the game runs completely vanilla
- The mod menu **hides itself** and cannot be opened
- Hotkeys are **disabled**
- A red warning banner appears if the menu was open when leaderboard mode started

Your settings are preserved and re-activate when the ranked session ends.

---

## Persistent Settings

Your configuration is saved automatically to:

```
<YourGameFolder>\BepInEx\config\com.goldrushmod.modmenu.cfg
```

This file is plain text and can be edited in any text editor. Settings that persist:

- Nugget limit
- Locked gold price and whether the override was active
- Decline speed
- Trickle rates for cash and gold

---

## Gold Nugget Limit

The game normally caps random gold nugget finds at **60 per claim**. This section lets you raise that limit.

Extra nuggets beyond 60 cycle through the game's top-tier reward range (indices 55–59 in the internal table), so the oz amounts are realistic and consistent with what the game itself would award.

| Control | Effect |
|---|---|
| **Set Limit** | Apply the typed cap (saved to config) |
| **x2 / x5 / x10 / Unlimited** | Quick presets (120 / 300 / 600 / 9999) |
| **Force Nugget** | Instantly award the next nugget without waiting for mud |
| **Reset Counter** | Reset spawned count to 0, restarting the cycle |

Natural spawning (via digging mud) still requires the nugget option to be enabled in the game's Settings menu.

---

## Building from Source

### Prerequisites

- [.NET SDK 6+](https://dotnet.microsoft.com/download)
- Gold Rush: The Game installed
- BepInEx 5.4.x installed in the game folder (see Installation above)

### Steps

1. Clone this repository:
   ```
   git clone https://github.com/YOUR_USERNAME/gold-rush-mod-menu.git
   cd gold-rush-mod-menu/src
   ```

2. Tell the project where your game is installed.  
   **Option A — environment variable (PowerShell):**
   ```powershell
   $env:GOLD_RUSH_PATH = "C:\Program Files (x86)\Steam\steamapps\common\Gold Rush The Game"
   ```
   **Option B — create a `local.props` file** in the `src\` folder:
   ```xml
   <Project>
     <PropertyGroup>
       <GoldRushPath>C:\Program Files (x86)\Steam\steamapps\common\Gold Rush The Game</GoldRushPath>
     </PropertyGroup>
   </Project>
   ```
   *(This file is in `.gitignore` so it will never be committed.)*

3. Build:
   ```
   dotnet build -c Release
   ```
   The compiled `GoldRushModMenu.dll` is automatically copied to your game's `BepInEx\plugins\` folder.

---

## Uninstall

Delete **`winhttp.dll`** from your Gold Rush game folder.  
BepInEx will no longer hook into the game and everything returns to vanilla.  
You can also delete the `BepInEx\` folder entirely to remove all traces.

---

## Troubleshooting

**Menu does not appear (F10 does nothing)**
- Make sure `winhttp.dll` is in the same folder as `GoldMiningSimulator.exe`
- Check `BepInEx\LogOutput.log` for error messages

**Nuggets not spawning beyond 60**
- Make sure "Gold Nuggets" is enabled in the game's Settings → Gameplay menu
- Natural spawning requires digging mud; use "Force Nugget" to test immediately

**Trickle seems stuck**
- Click "Stop" next to the active trickle, or load a save — the wallet must be ready

---

## Version History

| Version | Changes |
|---|---|
| v1.6.0 | Stealth/leaderboard mode, trickle system, believable gold growth |
| v1.5.0 | Persistent config, collapsible sections, scroll view, hotkeys, decline speed |
| v1.4.0 | Random gold nugget limit, force spawn, reset counter |
| v1.3.0 | Cursor unlock, stock-market price decline, pause/resume |
| v1.2.0 | Gold stock price lock and presets |
| v1.1.0 | Cash, gold, and diamonds editing |
| v1.0.0 | Initial release |

---

## Disclaimer

This mod is for **single-player use only**. The stealth system automatically disables all patches during leaderboard/ranked sessions, but use of any mod in competitive contexts is solely at your own risk. The authors take no responsibility for account penalties.