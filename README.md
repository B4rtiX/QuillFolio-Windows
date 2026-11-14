# Quillfolio (Windows Edition)

Modern, pressure-sensitive digital notebook and vector ink workspace built for **Windows 10 & Windows 11** using **C# / .NET 8 / WPF**.

> Developed by **B4rtiX** • Licensed under **GPL-3.0**

---

## Key Features

### Vector Ink & Stylus Precision
- **Tools**: Cursor (move objects), Ballpoint Pen, Classic Fountain Pen (caligraphic nib angle), Pencil, Highlighter, Stroke & Point Eraser, Marquee & Lasso Selection, and Laser Pointer.
- **Smooth Vector Rendering**: Pressure-sensitive ink paths with configurable **Catmull-Rom Spline** smoothing (`Core/Geometry/SplineSmoother.cs`) and real-time stroke interpolation.
- **Stylus & Express Keys Support**: Full hardware stylus pressure, pen-tablet shortcut keys, and automatic on-screen touch keyboard invocation (`TouchKeyboardHelper.cs`) for pen/touch input.

### Smart Geometry, 3D Solids & Scientific Notes
- **Hold & Snap Auto-Shape (`ShapeRecognizer.cs`)**: Recognizes circles, ellipses, triangles, rectangles, hexagons, straight lines, and single/double arrows.
- **2D Shapes & 3D Geometric Solids (`Geometry3DHelper.cs`)**: Insert and resize editable cubes, rectangular prisms, triangular/square pyramids, cylinders, cones, spheres, and hexagonal prisms directly onto the page.
- **Coordinate Axes**: Interactive Cartesian axes with independent X/Y axis visibility toggles, customizable tick intervals, bounds sliders, and size editing.

### Rich Canvas Objects & Overlays
- **Sticky Notes & Pin-to-Screen**: Floating sticky notes can be pinned to screen coordinates so they remain visible in place while panning or scrolling the page.
- **Rich Text Boxes**: Focus-only floating formatting ribbon with font family/size, bold/italic/underline/strikethrough, alignment, bulleted lists, color swatches, HSV Color Wheel, and a categorized **Math & Scientific Symbols** picker.
- **Dynamic Tables**: Editable spreadsheet-like tables with row/column insertion/removal, custom cell background/text colors, context menu controls, and theme-matched borders.
- **Images**: Paste directly from Clipboard (`Ctrl+V`) or load PNG/JPG/BMP/GIF/WEBP files, drag, and resize cleanly on the canvas.

### Workspace Organization & Customization
- **Hierarchical Folders & Favorites**: Expandable folders & subfolders with custom academic/scientific icons (`FolderIconCatalog.cs`), drag-and-drop note movement, right-click context menus, instant search, and a Favorites tab.
- **Themes**: Switch seamlessly between **Light (Nordic Studio)**, **Dark (Studio Dark)**, **OLED (Pure Black)**, and **Windows System** themes, plus customizable desk surfaces (`Nordic Frost`, `Warm Sand`, `Slate Mist`, `Deep Studio`).
- **Paper Styles**: Blank, Ruled, Grid, Dotted with configurable line spacing and paper colors (`White`, `Cream`, `Legal Yellow`, `Dark Slate`, `Chalkboard`).
- **Bilingual UI (PL / EN)**: Instant live switching between **Polish** and **English** across all toolbars, dialogs, overlays, tooltips, and the Home screen.
- **Custom Keyboard Shortcuts & Fullscreen (`F11`)**: Rebindable hotkeys saved in user settings.
- **Export**: High-DPI **PNG** export and multi-page vector **PDF** export via `PdfSharpCore`.

---

## Project Structure

```text
Quillfolio-Windows/
├── .github/workflows/build.yml         # CI workflow for Windows Release build & publish
├── Quillfolio.sln                      # .NET 8 solution
├── Quillfolio.Core/                    # Cross-layer domain models, geometry, localization & storage
│   ├── Geometry/                       # Geometry3DHelper, ShapeRecognizer, SplineSmoother
│   ├── Models/                         # NoteModels, DrawingStroke, DynamicTable, ShapeModels, etc.
│   └── Services/                       # DataStore (atomic JSON persistence), LocalizationService
├── Quillfolio.WPF/                     # WPF UI, vector ink canvas, dialogs & exporters
│   ├── Assets/                         # Application icon (.ico / .png)
│   ├── Controls/                       # InkCanvasControl, ImageOverlay, StickyNoteOverlay, TableOverlay
│   ├── Services/                       # FolderIconCatalog, Loc, PdfExporter, TouchKeyboardHelper, Animations & Themes
│   ├── Styles/                         # FluentStyles.xaml (Light/Dark/OLED resource dictionaries)
│   └── Views/                          # MainWindow, SettingsWindow, AxesConfigWindow, ColorPicker, IconPicker, ConfirmDialog
├── build.bat                           # One-click single-file Release build script
├── run.bat                             # Quick dotnet run script
└── LICENSE                             # GNU GPL v3.0
```

---

## Requirements & Build

- **OS**: Windows 10 / Windows 11 (x64)
- **SDK**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Option 1: Run in Development Mode
```bat
run.bat
```
or:
```powershell
dotnet run --project Quillfolio.WPF
```

### Option 2: Build Standalone Single-File Executable
Run `build.bat` or execute manually:
```powershell
dotnet restore Quillfolio.sln
dotnet build Quillfolio.sln -c Release --no-restore -m:1
dotnet publish Quillfolio.WPF\Quillfolio.WPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist --no-restore -m:1
```
Output executable: `.\dist\Quillfolio.exe`

User notes and preferences are stored locally in:
```text
%APPDATA%\Quillfolio\quillfolio_data.json
%APPDATA%\Quillfolio\quillfolio_settings.json
```

---

## License

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)** — see the [LICENSE](LICENSE) file for details.
