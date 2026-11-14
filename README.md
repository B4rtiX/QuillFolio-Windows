# Quillfolio (Windows Edition)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-7C3AED?style=for-the-badge)
![Framework](https://img.shields.io/badge/.NET%208-WPF%20Desktop-512BD4?style=for-the-badge)
![License](https://img.shields.io/badge/License-GPL--3.0-16A34A?style=for-the-badge)
**Quillfolio** is a modern, pressure-sensitive digital notebook and vector-ink workspace crafted for **Windows 10 and Windows 11**. Built natively with **C# / .NET 8 / WPF**, it brings together smooth handwritten note-taking, intelligent geometry tools, and distraction-free organization in a clean, customizable Fluent-inspired studio.
Whether you are taking lecture notes with a graphics tablet, sketching geometric solids and coordinate graphs, or organizing study materials across subjects, Quillfolio keeps everything fast, local, and effortless.
---
## Why Quillfolio?
- **Precision Vector Ink & Stylus Support** — Write naturally with the Ballpoint Pen, Classic Fountain Pen (adjustable calligraphic nib angle), Pencil, and Highlighter. Powered by real-time stroke smoothing (**Catmull-Rom Spline**), hardware stylus pressure, graphic tablet Express Keys, and automatic touch-keyboard invocation.
- **Smart Erasing & Selection** — Switch seamlessly between **Stroke Eraser** and **Point Eraser**, select and move strokes or canvas objects with **Marquee** or **Lasso** selection, or present clearly using the fading **Laser Pointer**.
- **2D Auto-Shapes, 3D Solids & Coordinate Axes** — Draw rough shapes and let **Hold & Snap** turn them into clean circles, ellipses, triangles, rectangles, hexagons, lines, or arrows. Insert editable **3D geometric solids** and interactive **Cartesian coordinate axes** with independent X/Y axis visibility, custom tick steps, and slider resizing.
- **Interactive Study Canvas** — Embed **Sticky Notes** (with **Pin-to-Screen** so key formulas stay visible while scrolling), **Rich Text Boxes** with a contextual formatting ribbon and categorized **Math & Scientific Symbols**, **Dynamic Tables** with custom per-cell colors, and **Images** pasted directly from clipboard (`Ctrl+V`) or disk.
- **Built Around Your Workflow** — Organize notes in expandable hierarchical **Folders & Subfolders** with custom academic icons, drag-and-drop movement, Favorites, and instant search. Personalize your desk with **Light**, **Dark**, **OLED (Pure Black)**, and **Windows System** themes, 4 desk surfaces, 4 paper styles, customizable keyboard shortcuts, fullscreen mode (`F11`), and live **English / Polish** switching.
- **Private & Standalone** — Notes are stored locally in atomic JSON files (`%APPDATA%\Quillfolio`), ready to export anytime to crisp **PNG** images or multi-page **PDF** documents.
---
## System Requirements
| Target | Requirements |
| :--- | :--- |
| **End Users (`Quillfolio.exe`)** | Windows 10 or Windows 11 (64-bit `win-x64`) • No .NET Runtime required (self-contained single-file app) • Minimum `980 × 640 px` display (`1280 × 720 px` or higher recommended) • Mouse/keyboard or graphics tablet/stylus |
| **Developers (From Source)** | Windows 10 or Windows 11 (x64) • [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) • Internet access on first restore (`System.Text.Json 8.0.5`) |
---
## Quick Start
1. Download the latest `Quillfolio.exe` from **[Releases](../../releases)** and run it directly — no installation needed.
2. Or build from source using `build.bat` or the .NET CLI:
   ```powershell
   dotnet restore Quillfolio.sln
   dotnet build Quillfolio.sln -c Release --no-restore -m:1
   dotnet publish Quillfolio.WPF\Quillfolio.WPF.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist --no-restore -m:1
   ```
---
## License
Developed by **B4rtiX** • Licensed under the **GNU General Public License v3.0 (GPL-3.0)**.
