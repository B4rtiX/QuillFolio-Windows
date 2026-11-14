namespace Quillfolio.Core.Models;

public enum ToolType
{
    None = -1,        // Brak aktywnego narzędzia / Pan-only browse mode
    Pen = 0,          // Długopis / Ballpoint
    Pencil = 1,
    Highlighter = 2,
    Eraser = 3,
    Select = 4,
    Laser = 5,
    FountainPen = 6,  // Pióro klasyczne / Classic Fountain Pen
    Cursor = 7        // Kursor / Move & Drag objects
}

public enum EraserMode
{
    Stroke = 0,
    Point = 1
}

public enum SelectionMode
{
    Marquee = 0,
    Lasso = 1
}

public enum PaperStyle
{
    Blank = 0,
    Ruled = 1,
    Grid = 2,
    Dotted = 3
}

public enum PaperColorTheme
{
    White = 0,
    Cream = 1,
    LegalYellow = 2,
    DarkSlate = 3,
    Chalkboard = 4
}

public enum AppThemeMode
{
    Light = 0,
    Dark = 1,
    Oled = 2,
    System = 3
}
