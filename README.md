# Photo Sorter

A cross-platform desktop application for rapidly culling and sorting raw photo files (e.g. DNG) into categorized folders. Built with [Avalonia UI](https://avaloniaui.net/) and .NET 10.

![Platform](https://img.shields.io/badge/platform-macOS-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-512bd4)
![Avalonia](https://img.shields.io/badge/Avalonia-12.0-8b5cf6)

## Features

- **Fast photo viewing** — loads DNG files with a two-pass decode pipeline (quick preview → full resolution) and aggressive LRU caching
- **One-key sorting** — categorize photos into **Good**, **Very Good**, or **Sorted Out** folders with arrow keys
- **Zoom, pan & rotate** — mouse wheel/pinch zoom, drag-to-pan, Q/E to rotate 90°
- **EXIF auto-rotation** — portrait photos display correctly automatically
- **Configurable folders** — rename sort destinations and set your library root in the built-in settings panel
- **Working folder switcher** — quickly browse the library root or any sort destination from the bottom bar

## Keyboard Shortcuts

| Key | Action |
|---|---|
| `←` / `→` / `↑` / `↓` | Navigate photos |
| `Shift` + `←` | Sort out (reject) |
| `Shift` + `→` | Move to **Good** |
| `Shift` + `↑` | Move to **Very Good** |
| `Q` | Rotate left (counter-clockwise) |
| `E` | Rotate right (clockwise) |
| `W` / `S` | Zoom in / out |
| `Space` | Reset zoom |
| `H` | Toggle help overlay |
| `Esc` | Quit |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (preview)
- macOS (currently the only build target with the bundled `.app` scripts)

### Run from Source

```bash
git clone https://github.com/niconiconii/PhotoSelector.git
cd PhotoSelector
dotnet run --project PhotoSorterAvalonia
```

### Build a macOS App Bundle

```bash
./create-mac-app.sh            # builds and creates "Photo Sorter.app" in the repo root
./create-mac-app.sh --install  # builds, creates the bundle, and copies it to /Applications
```

### Install to /Applications (from an existing bundle)

```bash
./install-photo-sorter.sh
```

### Uninstall

Double-click **Uninstall Photo Sorter.command** or run:

```bash
rm -rf "/Applications/Photo Sorter.app"
```

## Configuration

User settings are stored in `~/Library/Application Support/PhotoSorter/photo_sorter_app.json` (macOS) and can also be edited from the in-app Settings panel:

| Setting | Default | Description |
|---|---|---|
| RootFolder | *User's Pictures folder* | Library root where photos are scanned |
| GoodFolderName | `good` | Subfolder name for "good" selections |
| VeryGoodFolderName | `verygood` | Subfolder name for "very good" selections |
| SortedOutFolderName | `sortedout` | Subfolder name for rejected photos |

Internal tuning constants (cache sizes, zoom limits, etc.) live in `PhotoSorterAvalonia/AppConfig.cs`.

## Tech Stack

- **UI Framework:** Avalonia UI 12 (Fluent theme)
- **Runtime:** .NET 10, self-contained single-file publish
- **Image decoding:** Custom two-pass pipeline with orientation-aware EXIF rotation

## Project Structure

```
PhotoSorterAvalonia/
├── Program.cs                  # Entry point
├── App.axaml(.cs)              # Application bootstrap
├── AppConfig.cs                # Compile-time constants & helpers
├── AppSettings.cs              # Persisted user settings (JSON)
├── SessionSettings.cs          # Per-session transient state
├── StatisticsManager.cs        # Sort statistics tracking
├── ImageDecoder.cs             # Two-pass image decode pipeline
├── BitmapOrientationHelper.cs  # EXIF orientation handling
├── MainWindow.axaml(.cs)       # Main window & layout
├── MainWindow.ImageLoading.cs  # Photo loading & caching logic
├── MainWindow.PhotoCommands.cs # Sort/delete command handlers
├── MainWindow.PointerInput.cs  # Mouse/trackpad interaction
├── MainWindow.ZoomPan.cs       # Zoom & pan transforms
├── SettingsWindow.axaml(.cs)   # Settings dialog
└── PhotoSorterAvalonia.csproj  # Project file
```

## License

This project is licensed under the [MIT License](LICENSE).