using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Quillfolio.Core.Models;

public enum ShortcutAction
{
    ToolCursor,
    ToolPen,
    ToolFountainPen,
    ToolPencil,
    ToolHighlighter,
    ToolEraser,
    ToolSelect,
    ToolLaser,
    ToolNone,
    ToggleFullscreen,
    ToggleSidebar,
    ToggleBottomBar,
    OpenHome,
    OpenSettings,
    NewNote,
    AddPage,
    PrevPage,
    NextPage,
    Undo,
    Redo,
    CopySelection,
    Paste,
    DuplicateSelection,
    SelectAll,
    DeleteSelected,
    ZoomIn,
    ZoomOut,
    ZoomReset,
    BrushSmaller,
    BrushLarger
}

public class ShortcutBinding
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("ctrl")]
    public bool Ctrl { get; set; }

    [JsonPropertyName("shift")]
    public bool Shift { get; set; }

    [JsonPropertyName("alt")]
    public bool Alt { get; set; }

    public ShortcutBinding() { }

    public ShortcutBinding(string key, bool ctrl = false, bool shift = false, bool alt = false)
    {
        Key = NormalizeKeyName(key);
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
    }

    public ShortcutBinding Clone() => new(Key, Ctrl, Shift, Alt);

    public bool Matches(string rawKeyName, bool ctrl, bool shift, bool alt)
    {
        if (string.IsNullOrWhiteSpace(Key)) return false;
        string normalized = NormalizeKeyName(rawKeyName);
        if (!string.Equals(Key, normalized, StringComparison.OrdinalIgnoreCase)) return false;
        return Ctrl == ctrl && Shift == shift && Alt == alt;
    }

    public bool SameCombo(ShortcutBinding other) =>
        other != null &&
        string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase) &&
        Ctrl == other.Ctrl &&
        Shift == other.Shift &&
        Alt == other.Alt;

    public string ToDisplayString()
    {
        if (string.IsNullOrWhiteSpace(Key)) return "—";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        parts.Add(FormatKeyDisplay(Key));
        return string.Join(" + ", parts);
    }

    public static string NormalizeKeyName(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return "";
        return keyName.Trim() switch
        {
            "Add" => "OemPlus",
            "Subtract" => "OemMinus",
            "NumPad0" => "D0",
            "Next" => "PageDown",
            "Prior" => "PageUp",
            "Return" => "Enter",
            _ => keyName.Trim()
        };
    }

    public static string FormatKeyDisplay(string keyName)
    {
        string norm = NormalizeKeyName(keyName);
        if (norm.Length == 2 && norm[0] == 'D' && char.IsDigit(norm[1]))
            return norm[1].ToString();

        return norm switch
        {
            "OemPlus" => "+",
            "OemMinus" => "-",
            "OemOpenBrackets" => "[",
            "OemCloseBrackets" => "]",
            "OemComma" => ",",
            "OemPeriod" => ".",
            "OemQuestion" => "/",
            "OemSemicolon" => ";",
            "OemQuotes" => "'",
            "OemPipe" => "\\",
            "OemTilde" => "`",
            "PageUp" => "PgUp",
            "PageDown" => "PgDn",
            "Delete" => "Del",
            "Space" => "Space",
            _ => norm
        };
    }

    public static Dictionary<string, ShortcutBinding> CreateDefaults() => new()
    {
        { ShortcutAction.ToolCursor.ToString(), new("C") },
        { ShortcutAction.ToolPen.ToString(), new("P") },
        { ShortcutAction.ToolFountainPen.ToString(), new("F") },
        { ShortcutAction.ToolPencil.ToString(), new("B") },
        { ShortcutAction.ToolHighlighter.ToString(), new("H") },
        { ShortcutAction.ToolEraser.ToString(), new("E") },
        { ShortcutAction.ToolSelect.ToString(), new("S") },
        { ShortcutAction.ToolLaser.ToString(), new("L") },
        { ShortcutAction.ToolNone.ToString(), new("D0") },

        { ShortcutAction.ToggleFullscreen.ToString(), new("F11") },
        { ShortcutAction.ToggleSidebar.ToString(), new("B", ctrl: true) },
        { ShortcutAction.ToggleBottomBar.ToString(), new("F9") },
        { ShortcutAction.OpenHome.ToString(), new("H", ctrl: true) },
        { ShortcutAction.OpenSettings.ToString(), new("OemComma", ctrl: true) },

        { ShortcutAction.NewNote.ToString(), new("N", ctrl: true) },
        { ShortcutAction.AddPage.ToString(), new("N", ctrl: true, shift: true) },
        { ShortcutAction.PrevPage.ToString(), new("PageUp") },
        { ShortcutAction.NextPage.ToString(), new("PageDown") },

        { ShortcutAction.Undo.ToString(), new("Z", ctrl: true) },
        { ShortcutAction.Redo.ToString(), new("Y", ctrl: true) },
        { ShortcutAction.CopySelection.ToString(), new("C", ctrl: true) },
        { ShortcutAction.Paste.ToString(), new("V", ctrl: true) },
        { ShortcutAction.DuplicateSelection.ToString(), new("D", ctrl: true) },
        { ShortcutAction.SelectAll.ToString(), new("A", ctrl: true) },
        { ShortcutAction.DeleteSelected.ToString(), new("Delete") },

        { ShortcutAction.ZoomIn.ToString(), new("OemPlus", ctrl: true) },
        { ShortcutAction.ZoomOut.ToString(), new("OemMinus", ctrl: true) },
        { ShortcutAction.ZoomReset.ToString(), new("D0", ctrl: true) },
        { ShortcutAction.BrushSmaller.ToString(), new("OemOpenBrackets") },
        { ShortcutAction.BrushLarger.ToString(), new("OemCloseBrackets") }
    };

    public static bool AllowsWhenTextFocused(ShortcutAction action) => action is
        ShortcutAction.ToggleFullscreen or
        ShortcutAction.OpenSettings or
        ShortcutAction.ToggleSidebar or
        ShortcutAction.ToggleBottomBar;
}
