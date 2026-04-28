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

A C# (.NET) interactive console application that automates the entire process of creating a *Bad Update* USB drive. No manual file hunting required. Runs on **Windows and Linux**.

**Author:** [Pdawg](https://github.com/Pdawg-bytes/BadBuilder)

### Features

- **Disk detection** — lists all connected removable drives with size and type information
- **FAT32 formatting**
  - *Windows:* uses `format.com` for drives < 32 GB; custom low-level FAT32 formatter via Windows IOCTL for larger drives
  - *Linux:* uses `mkfs.vfat` (from `dosfstools`) — no size limit
- **Automatic file download** — fetches the latest releases from GitHub for:
  - [Xbox360BadUpdate](https://github.com/grimdoomer/Xbox360BadUpdate)
  - [FreeMyXe](https://github.com/FreeMyXe/FreeMyXe)
  - [XeUnshackle](https://github.com/Byrom90/XeUnshackle)
- **File extraction** — unpacks all downloaded archives automatically
- **Payload selection** — choose between FreeMyXe or XeUnshackle as the default boot target
- **Homebrew support** — add any homebrew app (e.g. Aurora) by pointing to its root folder; BadBuilder finds the entry `.xex`, copies all files to the USB, and patches the `.xex` in-place using XexTool
- **Rich terminal UI** — Spectre.Console with colour-coded progress bars and prompts

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
│   │   ├── ArchiveHelper.cs           # ZIP/7z extraction
│   │   ├── DiskHelper.cs              # Drive enumeration & format calls (Windows + Linux)
│   │   ├── DownloadHelper.cs          # GitHub release fetching (Octokit), HTTP download
│   │   ├── FileSystemHelper.cs        # Directory mirroring
│   │   └── PatchHelper.cs             # XexTool invocation (Wine on Linux)
│   ├── Models/
│   │   └── DiskInfo.cs                # Drive metadata model (includes DevicePath for Linux)
│   └── Utilities/
│       ├── ActionQueue.cs             # Sequential async task queue
│       └── Constants.cs               # Working/download/extract directory paths
└── BadBuilder.Formatter/
    ├── DiskFormatter.cs               # Custom FAT32 formatter (large drives, Windows IOCTL)
    ├── FAT32BootSector.cs             # FAT32 boot sector structure
    ├── FAT32FsInfoSector.cs           # FAT32 FS info sector structure
    └── Utilities.cs                   # P/Invoke helpers
```

### How to use — Windows

1. Launch the BadBuilder executable in a terminal window.
2. Select the target USB drive from the list.
3. Confirm formatting — **all data on the selected drive will be erased**.
4. BadBuilder downloads all required files automatically.
5. Choose your default payload: **FreeMyXe** or **XeUnshackle**.
6. BadBuilder extracts and copies all exploit files to the USB.
7. *(Optional)* Add a homebrew application — provide the root folder path (e.g. `D:\Aurora 0.7b.2 - Release Package`); BadBuilder finds and patches the entry `.xex`.
8. The USB drive is ready. Insert into your Xbox 360 and follow the Bad Update exploit steps.

### How to use — Linux

#### Prerequisites

Install the required system packages:

```bash
# LMDE 7 / Debian 13 (Trixie)
# Step 1: Add Microsoft's package feed (not in Debian repos by default)
wget https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Step 2: Install .NET SDK and other dependencies
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0 dosfstools wine
```

```bash
# Debian 12 (Bookworm) / LMDE 6
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0 dosfstools wine
```

```bash
# Arch/Manjaro
sudo pacman -S dotnet-sdk dosfstools wine
```

```bash
# Fedora
sudo dnf install dotnet-sdk-8.0 dosfstools wine
```

> **Why the extra step on Debian/LMDE?** Microsoft's .NET SDK is not in the official Debian repositories. The `packages-microsoft-prod.deb` package adds Microsoft's APT feed and signing key. Running `apt install dotnet-sdk-8.0` without this step will produce "Unable to locate package".

- **`dosfstools`** provides `mkfs.vfat` for FAT32 formatting.
- **`wine`** is required only if you want to add and patch homebrew apps (XexTool is a Windows binary). Core USB creation works without Wine.

> **Note:** Formatting a block device requires elevated privileges. Run BadBuilder with `sudo` (or ensure your user is in the `disk` group).

#### Build and run

```bash
cd BadBuilder-main
dotnet build BadBuilder/BadBuilder.csproj
sudo dotnet run --project BadBuilder/BadBuilder.csproj
```

Or publish a self-contained binary:

```bash
dotnet publish BadBuilder/BadBuilder.csproj \
  -r linux-x64 \
  -c Release \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish

sudo ./publish/BadBuilder
```

#### How Linux disk selection works

BadBuilder uses `lsblk` to enumerate removable block devices. Only removable drives (`rm=1`) are shown to reduce the risk of selecting the wrong disk.

Example listing:
```
/dev/sdb1 (14.32 GB) - Removable    [mounted at /media/user/USB]
/dev/sdc  (29.80 GB) - Removable    [unmounted /dev/sdc]
```

After formatting, BadBuilder waits for the OS to remount the drive under its new `BADUPDATE` label and detects the new mount point automatically. If auto-mount doesn't occur (e.g. on a headless system), you will be prompted to either mount it manually or enter the path.

#### Adding homebrew on Linux (Aurora example)

```bash
# When prompted for a homebrew folder, enter the full path:
/home/you/WBs360/Aurora\ 0.7b.2\ -\ Release\ Package
```

BadBuilder will detect `Aurora.xex`, copy the full directory to the USB, and patch the `.xex` using `wine XexTool.exe`. The original files are never modified.

### Dependencies

- [Spectre.Console](https://spectreconsole.net/) — terminal UI
- [Octokit](https://github.com/octokit/octokit.net) — GitHub API client
- [SharpCompress](https://github.com/adamhathcock/sharpcompress) — cross-platform archive extraction
- [CsWin32](https://github.com/microsoft/CsWin32) — Windows P/Invoke source generation (used in `BadBuilder.Formatter`, Windows only)

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

| Platform | Requirements |
|---|---|
| Windows | .NET 8 SDK or later |
| Linux | .NET 8 SDK, `dosfstools` (`mkfs.vfat`), `wine` (optional, for homebrew `.xex` patching) |

Run BadBuilder on Linux with `sudo` to allow block device access for formatting.

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
