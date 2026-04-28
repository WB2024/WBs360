# WBs360 — Xbox 360 Software Homebrew Toolkit

A collection of tools, exploit code, and homebrew applications for running unsigned/homebrew software on an unmodified (no soldering required) Xbox 360 console running dashboard firmware **17559**.

---

## Overview

This repository bundles four components that together form a complete end-to-end Xbox 360 software homebrew pipeline:

| Component | Description |
|---|---|
| [`Xbox360BadUpdate-master`](#xbox360badupdate-master) | Original *Bad Update* hypervisor exploit |
| [`ABadAvatar-master`](#abadavatar-master) | Avatar-based variant of the Bad Update exploit |
| [`BadBuilder-main`](#badbuilder-main) | Automated USB drive setup tool (C#) |
| [`Aurora 0.7b.2 - Release Package`](#aurora-07b2---release-package) | Aurora homebrew dashboard (sample payload) |

---

## How It All Fits Together

```
┌──────────────────────────────────────────────────────────┐
│  1. BadBuilder prepares a USB drive                       │
│     - Downloads exploit files from GitHub releases        │
│     - Formats the drive as FAT32                          │
│     - Copies exploit + payload (FreeMyXe / XeUnshackle)  │
│     - Optionally copies homebrew apps (e.g. Aurora)       │
└────────────────────────┬─────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────┐
│  2. Bad Update exploit runs on Xbox 360                   │
│     Trigger via one of:                                   │
│       - Tony Hawk's American Wasteland (save exploit)     │
│       - Rock Band Blitz (save exploit)                    │
│       - Avatar system (ABadAvatar variant)                │
│                                                           │
│     4-stage PowerPC ROP chain:                            │
│       Stage 1 → initial stack pivot                       │
│       Stage 2 → load modules, map flash                   │
│       Stage 3 → race the hypervisor, win code exec        │
│       Stage 4 → patch hypervisor, hand off to payload     │
└────────────────────────┬─────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────┐
│  3. Payload runs (FreeMyXe or XeUnshackle)                │
│     Hypervisor is now patched to allow unsigned code       │
└────────────────────────┬─────────────────────────────────┘
                         │
                         ▼
┌──────────────────────────────────────────────────────────┐
│  4. Homebrew runs (e.g. Aurora dashboard)                 │
└──────────────────────────────────────────────────────────┘
```

> **Important:** This exploit is **NOT persistent**. The console returns to a stock state on every power cycle. The exploit must be re-run each boot.

---

## Xbox360BadUpdate-master

The original *Bad Update* exploit authored by [grimdoomer](https://github.com/grimdoomer/Xbox360BadUpdate).

### What it is

A non-persistent, software-only hypervisor exploit for the Xbox 360 targeting dashboard version **17559**. It exploits a race condition in the system update process to overwrite hypervisor code, allowing unsigned executables (`.xex`) to run without any hardware modification.

- **Success rate:** ~30% per attempt
- **Max time to trigger:** ~20 minutes
- **Persistence:** None — must be re-run every boot

### Trigger games

| Game | Notes |
|---|---|
| Tony Hawk's American Wasteland | NTSC/PAL/RF — save game stack overflow |
| Rock Band Blitz | Trial or full — save game stack overflow |

### Exploit stages

The exploit is assembled from PowerPC assembly (`.asm`) source files using `XePatcher` (a custom GNU assembler wrapper). A Windows batch script (`build_exploit.bat`) orchestrates compilation.

```
build_exploit.bat <THAW|RBB> [RETAIL_BUILD|DEBUG_BUILD]
```

| Stage | File | Purpose |
|---|---|---|
| Stage 1 | `Stage1/<Game>/BadUpdateExploit.asm` | Initial ROP chain triggered by the save game overflow. Pivots the stack and copies exploit data into a large heap allocation. |
| Stage 2 | `Stage2/BadUpdateExploit-2ndStage.asm` | Sets LED status, maps the flash filesystem, loads `bootanim.xex` to discover kernel addresses, and builds the cipher-text overwrite loop. |
| Stage 3 | `Stage3/BadUpdateExploit-3rdStage.asm` | Runs the race condition — monitors the hypervisor's cipher text for changes, detects when a block overwrites hypervisor code, and writes the shellcode entry point into the syscall table. Derived from `BadUpdatePoc.cpp`. |
| Stage 4 | `Stage4/BadUpdateExploit-4thStage.asm` | Hypervisor-level shellcode. Fixes trashed HV data, sets LED to orange, relocates cache lines, and launches the unsigned payload XEX. |

### Common shared files

Located in `Common/`:

| File | Purpose |
|---|---|
| `BuildConfig.asm` | Selects retail/debug kernel config and game target. Performs sanity checks. |
| `KernelConfig_Retail_17559.asm` | Kernel function addresses for retail dashboard 17559 |
| `KernelConfig_Debug.asm` | Kernel function addresses for debug/devkit builds |
| `Gadgets.asm` | ROP gadget addresses used across all stages |
| `GetPayloadCipherText.asm` / `_Macros.asm` | Logic for reading the hypervisor's encrypted block cipher text |
| `MemcpyCipherText.asm` | Overwrites hypervisor segments using the cipher text loop |
| `BadUpdateExploit_Data.asm` / `_Data_h.asm` | Shared data segment layout and header |

---

## ABadAvatar-master

An extension of the Bad Update exploit, authored by [shutterbug2000](https://github.com/shutterbug2000), that adds the Xbox 360 **Avatar system** as a third trigger vector.

### What it adds

Rather than requiring a specific game disc, this variant exploits a stack overflow via a crafted **Avatar item** (stored in `update_data.bin` / `xke_update.bin`). The Avatar data gap name overflow triggers Stage 0, which performs a stack pivot and hands off to the standard Bad Update chain.

The Avatar profile must be installed on the console beforehand (HDD or formatted USB with the offline 17559 system update). **Do not log into the exploit profile on LIVE.**

### Trigger vectors

| Vector | Notes |
|---|---|
| Avatar system | New vector added by this variant. Triggered from the profile select screen. |
| Tony Hawk's American Wasteland | Retained from original |
| Rock Band Blitz | Retained from original |

### Additional stage: Stage 0

`Stage1/Avatar/Stage0.asm` — a minimal, largely static stub that:
1. Displays anti-scam text
2. Performs the initial stack pivot into Stage 1

Stage 0 is compressed and embedded in the Avatar item at offset `0x2200`. It is considered static and should not normally need modification.

### Key differences from Xbox360BadUpdate-master

- Stage 3 must be built at base address `0x90110000` (not `0x98030000`).
- Stage 3 output must be padded to `0x10000` bytes with `4E 80 00 20` (`blr` instructions) so the bootanim module can be properly unloaded.
- Includes pre-built binary artifacts: `update_data.bin`, `xke_update.bin`.
- Exploit can be identified while running: move the cursor on the profile select screen — 2 LEDs on the RoL will occasionally swap.

---

## BadBuilder-main

A C# (.NET) interactive console application that automates the entire process of creating a *Bad Update* USB drive. No manual file hunting required.

**Author:** [Pdawg](https://github.com/Pdawg-bytes/BadBuilder)

### Features

- **Disk detection** — lists all connected drives with size and type information
- **FAT32 formatting** — uses Windows `format.com` for drives < 32 GB; uses a custom low-level FAT32 formatter (`BadBuilder.Formatter`) via Windows P/Invoke (`IOCTL`) for larger drives
- **Automatic file download** — fetches the latest releases from GitHub for:
  - [Xbox360BadUpdate](https://github.com/grimdoomer/Xbox360BadUpdate)
  - [FreeMyXe](https://github.com/FreeMyXe/FreeMyXe)
  - [XeUnshackle](https://github.com/Byrom90/XeUnshackle)
- **File extraction** — unpacks all downloaded archives automatically
- **Payload selection** — choose between FreeMyXe or XeUnshackle as the default boot target
- **Homebrew support** — add any homebrew app (e.g. Aurora) by pointing to its root folder; BadBuilder finds the entry `.xex`, copies all files to the USB, and patches the `.xex` in-place using XexTool
- **Rich terminal UI** — Spectre.Console with colour-coded progress bars and prompts

> **Note:** Formatting is Windows-only. On other operating systems BadBuilder will prompt you to format manually.

### Project structure

```
BadBuilder-main/
├── BadBuilder/
│   ├── Program.cs                     # Entry point, main workflow
│   ├── ConsoleExperiences/
│   │   ├── DiskExperience.cs          # Drive selection & format prompts
│   │   ├── DownloadExperience.cs      # Download progress UI
│   │   ├── ExtractExperience.cs       # Archive extraction UI
│   │   └── HomebrewExperience.cs      # Homebrew app management UI
│   ├── Helpers/
│   │   ├── ArchiveHelper.cs           # ZIP extraction
│   │   ├── DiskHelper.cs              # Drive enumeration & format calls
│   │   ├── DownloadHelper.cs          # GitHub release fetching (Octokit), HTTP download
│   │   ├── FileSystemHelper.cs        # Directory mirroring
│   │   └── PatchHelper.cs             # XexTool invocation for .xex patching
│   ├── Models/
│   │   └── DiskInfo.cs                # Drive metadata model
│   └── Utilities/
│       ├── ActionQueue.cs             # Sequential async task queue
│       └── Constants.cs               # Working/download/extract directory paths
└── BadBuilder.Formatter/
    ├── DiskFormatter.cs               # Custom FAT32 formatter (large drives, Windows IOCTL)
    ├── FAT32BootSector.cs             # FAT32 boot sector structure
    ├── FAT32FsInfoSector.cs           # FAT32 FS info sector structure
    └── Utilities.cs                   # P/Invoke helpers
```

### How to use

1. Launch the BadBuilder executable in a terminal window.
2. Select the target USB drive from the list.
3. Confirm formatting (all data on the drive will be erased).
4. BadBuilder downloads all required files automatically.
5. Choose your default payload: **FreeMyXe** or **XeUnshackle**.
6. BadBuilder extracts and copies all exploit files to the USB.
7. *(Optional)* Add a homebrew application — provide the root folder path (e.g. `D:\Aurora 0.7b.2 - Release Package`); BadBuilder finds and patches the entry `.xex`.
8. The USB drive is ready. Insert into your Xbox 360 and follow the Bad Update exploit steps.

### Dependencies

- [Spectre.Console](https://spectreconsole.net/) — terminal UI
- [Octokit](https://github.com/octokit/octokit.net) — GitHub API client
- [CsWin32](https://github.com/microsoft/CsWin32) — Windows P/Invoke source generation (used in `BadBuilder.Formatter`)

---

## Aurora 0.7b.2 - Release Package

The [Aurora](http://phoenix.xboxunity.net/) homebrew dashboard for Xbox 360, version **0.7b.2**, included here as a ready-to-use sample payload/homebrew application.

### What it is

Aurora is a full replacement dashboard for the Xbox 360 that runs as an unsigned XEX under the Bad Update exploit (or any other CFW/hypervisor patch). It provides game library management, cover art, online features (via the Phoenix Network), FTP access, plugin support, and a customisable skin system.

### Contents

```
Aurora 0.7b.2 - Release Package/
├── Aurora.xex          # Main executable (entry point)
├── live.json           # Phoenix network configuration
├── nxeart              # Network art data
├── Data/
│   ├── Logs/           # Runtime log output
│   └── Thumbnails/     # Cached game art
├── Media/
│   ├── Assets/         # UI images and resources
│   ├── Effects/        # Compiled shader effects (SimpleShaders.fxobj)
│   ├── Fonts/          # XPR/XTT font files
│   ├── Layouts/        # Dashboard layout definitions (.cfljson)
│   ├── Locales/        # Localisation strings
│   └── Scripts/        # LUA scripts for UI behaviour
├── Plugins/
│   ├── FtpDll.xex      # FTP server plugin
│   ├── Nova.xex        # Nova plugin (game launching, title management)
│   ├── HudScene/       # HUD overlay assets
│   ├── Log/            # Plugin log output
│   └── WebRoot/        # Web interface assets
├── Skins/
│   └── Default.xzp     # Default skin package
└── User/
    ├── Backgrounds/    # User background images
    ├── Icons/          # User icons
    ├── Import/         # Import staging area
    ├── Scripts/        # User LUA scripts
    └── Trainers/       # Game trainer files
```

### Using Aurora with BadBuilder

When running BadBuilder's homebrew setup step, point it at the root of this folder:

```
/path/to/Aurora 0.7b.2 - Release Package
```

BadBuilder will detect `Aurora.xex` as the entry point, patch it with XexTool, and copy the full directory tree to the USB drive.

---

## Requirements

### To run the exploit

- Xbox 360 console (all models, including Winchester) running **dashboard 17559**
- USB stick formatted as FAT32
- One of the supported trigger games (disc or installed):
  - Tony Hawk's American Wasteland (Xbox 360 version, NTSC/PAL/RF)
  - Rock Band Blitz (trial or full)
  - *(ABadAvatar variant only)* Avatar update data installed on console

### To build the exploit from source

- Windows (for the build batch scripts)
- `XePatcher.exe` (custom GNU assembler for PPC, from the Tools.zip release)

### To build BadBuilder from source

- .NET 8 SDK or later
- Windows (for the formatter; other platforms can build but formatting will be manual)

---

## Disclaimer

This toolkit is intended for **educational and personal research purposes** only. You should only use this on hardware you own. Connecting a hacked Xbox 360 to Xbox LIVE may result in a console ban. Always disconnect from the internet before running the exploit.

---

## Credits

| Person / Team | Contribution |
|---|---|
| [grimdoomer](https://github.com/grimdoomer) | Original Bad Update exploit (Xbox360BadUpdate) |
| [shutterbug2000](https://github.com/shutterbug2000) | Avatar-based trigger variant (ABadAvatar) |
| [Pdawg](https://github.com/Pdawg-bytes) | BadBuilder automated USB setup tool |
| [InvoxiPlayGames](https://github.com/FreeMyXe) | FreeMyXe payload |
| [Byrom90](https://github.com/Byrom90) | XeUnshackle payload |
| [Swizzy](https://github.com/Swizzy) | Simple 360 NAND Flasher |
| Team XeDEV | XeXMenu |
| Team Aurora / Phoenix | Aurora dashboard |
