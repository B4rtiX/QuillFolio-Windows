using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Quillfolio.Core.Services;

namespace Quillfolio.Core.Models;

public class FloatingTextBox
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("text")]
    public string Text { get; set; } = "Notes...";

    [JsonPropertyName("x")]
    public double X { get; set; } = 150.0;

    [JsonPropertyName("y")]
    public double Y { get; set; } = 150.0;

    [JsonPropertyName("width")]
    public double Width { get; set; } = 220.0;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 140.0;

    [JsonPropertyName("rotation")]
    public double Rotation { get; set; } = 0.0;

    [JsonPropertyName("isStickyNote")]
    public bool IsStickyNote { get; set; } = false;

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#FEF08A"; // Pastel Yellow Sticky Note

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 15.0;

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("isBold")]
    public bool IsBold { get; set; } = false;

    [JsonPropertyName("isItalic")]
    public bool IsItalic { get; set; } = false;

    [JsonPropertyName("isUnderline")]
    public bool IsUnderline { get; set; } = false;

    [JsonPropertyName("isStrikethrough")]
    public bool IsStrikethrough { get; set; } = false;

    [JsonPropertyName("textColor")]
    public string TextColor { get; set; } = "#111827";

    [JsonPropertyName("textAlignment")]
    public string TextAlignment { get; set; } = "Left";

    [JsonPropertyName("isPinnedToScreen")]
    public bool IsPinnedToScreen { get; set; }

    [JsonPropertyName("pinnedScreenX")]
    public double PinnedScreenX { get; set; } = 32;

    [JsonPropertyName("pinnedScreenY")]
    public double PinnedScreenY { get; set; } = 32;
}

public class CanvasImage
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("x")]
    public double X { get; set; } = 120.0;

    [JsonPropertyName("y")]
    public double Y { get; set; } = 120.0;

    [JsonPropertyName("width")]
    public double Width { get; set; } = 320.0;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 240.0;

    [JsonPropertyName("imageBase64")]
    public string ImageBase64 { get; set; } = "";

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1.0;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Image";
}

public class Page
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Page 1";

    [JsonPropertyName("paperStyle")]
    public PaperStyle PaperStyle { get; set; } = PaperStyle.Grid;

    [JsonPropertyName("paperColor")]
    public PaperColorTheme PaperColor { get; set; } = PaperColorTheme.White;

    [JsonPropertyName("width")]
    public double Width { get; set; } = 2400.0;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 3200.0;

    [JsonPropertyName("strokes")]
    public List<DrawingStroke> Strokes { get; set; } = new();

    [JsonPropertyName("tables")]
    public List<DynamicTable> Tables { get; set; } = new();

    [JsonPropertyName("textBoxes")]
    public List<FloatingTextBox> TextBoxes { get; set; } = new();

    [JsonPropertyName("images")]
    public List<CanvasImage> Images { get; set; } = new();
}

public class Note
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Untitled Note";

    [JsonPropertyName("pages")]
    public List<Page> Pages { get; set; } = new() { new Page() };

    [JsonPropertyName("activePageIndex")]
    public int ActivePageIndex { get; set; } = 0;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("isFavorite")]
    public bool IsFavorite { get; set; } = false;

    [JsonIgnore]
    public int PageCount => Pages?.Count ?? 0;

    [JsonIgnore]
    public string PageCountDisplay => $"{PageCount} {LocalizationService.Instance.Get("PageShortSuffix")}";

    [JsonIgnore]
    public string UpdatedAtText => UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    [JsonIgnore]
    public string UpdatedAtDisplay => UpdatedAtText;

    [JsonIgnore]
    public Page CurrentPage
    {
        get
        {
            if (Pages == null || Pages.Count == 0)
            {
                Pages = new List<Page> { new Page() };
            }
            if (ActivePageIndex < 0 || ActivePageIndex >= Pages.Count)
            {
                ActivePageIndex = 0;
            }
            return Pages[ActivePageIndex];
        }
    }
}

public class Subcategory
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Subfolder";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "Subfolder";

    [JsonPropertyName("notes")]
    public List<Note> Notes { get; set; } = new();

    [JsonIgnore]
    public int NotesCount => Notes?.Count ?? 0;
}

public class Category
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "General";

    [JsonPropertyName("colorHex")]
    public string ColorHex { get; set; } = "#7C3AED";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "Folder";

    [JsonPropertyName("subcategories")]
    public List<Subcategory> Subcategories { get; set; } = new();

    [JsonIgnore]
    public bool IsExpanded { get; set; } = true;

    [JsonIgnore]
    public int TotalNotesCount => Subcategories?.Sum(s => s.Notes?.Count ?? 0) ?? 0;
}
