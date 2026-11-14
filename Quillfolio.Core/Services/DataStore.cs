/*
 ╭───────────────────────────────────────────────────────────────────╮
 │   ██████╗     ██╗  ██╗    ██████╗   ████████╗  ██╗  ██╗  ██╗      │
 │   ██╔══██╗    ██║  ██║    ██╔══██╗  ╚══██╔══╝  ██║  ╚██╗██╔╝      │
 │   ██████╔╝    ███████║    ██████╔╝     ██║     ██║   ╚███╔╝       │
 │   ██╔══██╗    ╚════██║    ██╔══██╗     ██║     ██║   ██╔██╗       │
 │   ██████╔╝         ██║    ██║  ██║     ██║     ██║  ██╔╝ ██╗      │
 │   ╚═════╝          ╚═╝    ╚═╝  ╚═╝     ╚═╝     ╚═╝  ╚═╝  ╚═╝      │
 │                                                                   │
 │   Architect & Lead Developer: B4rtiX                              │
 │   Quillfolio (Windows .NET Edition)                               │
 ╰───────────────────────────────────────────────────────────────────╯
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Quillfolio.Core.Models;

namespace Quillfolio.Core.Services;

public class AppSettings
{
    [JsonPropertyName("language")]
    public AppLanguage Language { get; set; } = AppLanguage.Polish;

    [JsonPropertyName("stylusPressureFactor")]
    public double StylusPressureFactor { get; set; } = 1.0;

    [JsonPropertyName("isShapeSnapEnabled")]
    public bool IsShapeSnapEnabled { get; set; } = true;

    [JsonPropertyName("shapeHoldDelayMs")]
    public int ShapeHoldDelayMs { get; set; } = 450;

    [JsonPropertyName("selectedColorHex")]
    public string SelectedColorHex { get; set; } = "#1E1E1E";

    [JsonPropertyName("selectedThickness")]
    public double SelectedThickness { get; set; } = 3.5;

    [JsonPropertyName("isSmoothingEnabled")]
    public bool IsSmoothingEnabled { get; set; } = true;

    [JsonPropertyName("guidelineSpacing")]
    public double GuidelineSpacing { get; set; } = 28.0;

    [JsonPropertyName("eraserMode")]
    public EraserMode EraserMode { get; set; } = EraserMode.Stroke;

    [JsonPropertyName("selectionMode")]
    public SelectionMode SelectionMode { get; set; } = SelectionMode.Marquee;

    [JsonPropertyName("fountainNibAngleDegrees")]
    public double FountainNibAngleDegrees { get; set; } = 38.0;

    [JsonPropertyName("laserFadeMs")]
    public int LaserFadeMs { get; set; } = 1100;

    [JsonPropertyName("eraserSizeMultiplier")]
    public double EraserSizeMultiplier { get; set; } = 2.2;

    [JsonPropertyName("autoSelectInsertedShapes")]
    public bool AutoSelectInsertedShapes { get; set; } = true;

    [JsonPropertyName("deskTheme")]
    public string DeskTheme { get; set; } = "NordicFrost";

    [JsonPropertyName("appTheme")]
    public AppThemeMode AppTheme { get; set; } = AppThemeMode.Light;

    [JsonPropertyName("defaultPageWidth")]
    public double DefaultPageWidth { get; set; } = 2400.0;

    [JsonPropertyName("defaultPageHeight")]
    public double DefaultPageHeight { get; set; } = 3200.0;

    [JsonPropertyName("defaultStartupTool")]
    public ToolType DefaultStartupTool { get; set; } = ToolType.Pen;

    [JsonPropertyName("shortcuts")]
    public Dictionary<string, ShortcutBinding> Shortcuts { get; set; } = ShortcutBinding.CreateDefaults();
}

public class DataStore
{
    public static string? CustomDataFolderOverride { get; set; }

    private static readonly Lazy<DataStore> _instance = new(() => new DataStore());
    public static DataStore Instance => _instance.Value;

    private static readonly JsonSerializerOptions _compactJsonOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions _settingsJsonOptions = new() { WriteIndented = true };

    private const int MaxUndoHistory = 50;

    public List<Category> Categories { get; set; } = new();
    public Note? ActiveNote { get; set; }
    public ToolType SelectedTool { get; set; } = ToolType.Pen;
    public ToolType DefaultStartupTool { get; set; } = ToolType.Pen;
    public EraserMode SelectedEraserMode { get; set; } = EraserMode.Stroke;
    public SelectionMode SelectedSelectionMode { get; set; } = SelectionMode.Marquee;
    public string SelectedColorHex { get; set; } = "#1E1E1E";
    public double SelectedThickness { get; set; } = 3.5;
    public bool IsShapeSnapEnabled { get; set; } = true;
    public bool IsSmoothingEnabled { get; set; } = true;
    public double GuidelineSpacing { get; set; } = 28.0;
    public double StylusPressureFactor { get; set; } = 1.0;
    public int ShapeHoldDelayMs { get; set; } = 450;
    public double FountainNibAngleDegrees { get; set; } = 38.0;
    public int LaserFadeMs { get; set; } = 1100;
    public double EraserSizeMultiplier { get; set; } = 2.2;
    public bool AutoSelectInsertedShapes { get; set; } = true;
    public string DeskTheme { get; set; } = "NordicFrost";
    public AppThemeMode AppTheme { get; set; } = AppThemeMode.Light;
    public double DefaultPageWidth { get; set; } = 2400.0;
    public double DefaultPageHeight { get; set; } = 3200.0;
    public double ZoomLevel { get; set; } = 1.0; // 0.2 to 4.0

    public Dictionary<string, ShortcutBinding> Shortcuts { get; set; } = ShortcutBinding.CreateDefaults();

    private readonly object _fileWriteLock = new();
    private SynchronizationContext? _uiSyncContext;
    private Timer? _saveDebounceTimer;
    private volatile bool _hasPendingSave;
    private Task _lastWriteTask = Task.CompletedTask;

    // Undo / Redo History (Store serialized snapshots of current Page)
    private readonly List<string> _undoHistory = new();
    private readonly Stack<string> _redoStack = new();

    public bool CanUndo => _undoHistory.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event Action? StateChanged;
    public event Action? UndoStateChanged;
    public event Action? CanvasContentChanged;

    public DataStore()
    {
        _uiSyncContext = SynchronizationContext.Current;
        LoadSettings();
        EnsureShortcutDefaults();
        SelectedTool = DefaultStartupTool;
        LoadFromDisk();
        if (Categories.Count == 0 || ActiveNote == null)
        {
            SeedDefaultData();
        }
    }

    public void EnsureShortcutDefaults()
    {
        var defaults = ShortcutBinding.CreateDefaults();
        Shortcuts ??= new();
        foreach (var kv in defaults)
        {
            if (!Shortcuts.TryGetValue(kv.Key, out var existing) || existing == null)
            {
                Shortcuts[kv.Key] = kv.Value.Clone();
            }
        }
    }

    public ShortcutBinding GetShortcut(ShortcutAction action)
    {
        EnsureShortcutDefaults();
        return Shortcuts.TryGetValue(action.ToString(), out var binding)
            ? binding
            : ShortcutBinding.CreateDefaults()[action.ToString()];
    }

    public void UpdateShortcut(ShortcutAction action, ShortcutBinding newBinding)
    {
        EnsureShortcutDefaults();
        string actionKey = action.ToString();
        var oldBinding = Shortcuts.TryGetValue(actionKey, out var prev) ? prev.Clone() : null;

        // If another action already uses the exact same combo, swap or clear conflict cleanly
        foreach (var kv in Shortcuts)
        {
            if (kv.Key == actionKey) continue;
            if (kv.Value.SameCombo(newBinding))
            {
                Shortcuts[kv.Key] = oldBinding ?? new ShortcutBinding();
                break;
            }
        }

        Shortcuts[actionKey] = newBinding.Clone();
        SaveSettings();
    }

    public void ResetShortcutsToDefault()
    {
        Shortcuts = ShortcutBinding.CreateDefaults();
        SaveSettings();
    }

    public void SeedDefaultData()
    {
        Categories = new List<Category>();

        var catGeneral = new Category { Name = "Główne (General)", ColorHex = "#7C3AED", Icon = "Folder" };
        var subWork = new Subcategory { Name = "Projekty & Szkice" };

        var sampleNote = new Note
        {
            Title = "Architektura Quillfolio (Tablica)",
            Pages = new List<Page>
            {
                new()
                {
                    Title = "Strona 1",
                    PaperStyle = PaperStyle.Grid,
                    PaperColor = PaperColorTheme.White
                }
            }
        };

        // Add a sample dynamic table to demonstrate feature immediately
        sampleNote.Pages[0].Tables.Add(new DynamicTable(180, 220, rows: 3, cols: 3));

        subWork.Notes.Add(sampleNote);
        catGeneral.Subcategories.Add(subWork);
        Categories.Add(catGeneral);

        ActiveNote = sampleNote;
        SaveToDisk();
    }

    public void ClearUndoHistory()
    {
        _undoHistory.Clear();
        _redoStack.Clear();
        UndoStateChanged?.Invoke();
    }

    public void PushUndoState()
    {
        if (ActiveNote?.CurrentPage == null) return;
        try
        {
            string snapshot = JsonSerializer.Serialize(ActiveNote.CurrentPage, _compactJsonOptions);
            _undoHistory.Add(snapshot);
            if (_undoHistory.Count > MaxUndoHistory)
            {
                _undoHistory.RemoveAt(0);
            }
            _redoStack.Clear(); // New action clears redo
            UndoStateChanged?.Invoke();
        }
        catch { }
    }

    public bool Undo()
    {
        if (ActiveNote?.CurrentPage == null || _undoHistory.Count == 0) return false;

        try
        {
            // Save current state to redo
            _redoStack.Push(JsonSerializer.Serialize(ActiveNote.CurrentPage, _compactJsonOptions));

            string prevState = _undoHistory[^1];
            _undoHistory.RemoveAt(_undoHistory.Count - 1);
            var restoredPage = JsonSerializer.Deserialize<Page>(prevState, _compactJsonOptions);
            if (restoredPage != null)
            {
                ActiveNote.Pages[ActiveNote.ActivePageIndex] = restoredPage;
                ActiveNote.UpdatedAt = DateTime.UtcNow;
                SaveToDisk();
                CanvasContentChanged?.Invoke();
                UndoStateChanged?.Invoke();
                return true;
            }
        }
        catch { }

        return false;
    }

    public bool Redo()
    {
        if (ActiveNote?.CurrentPage == null || _redoStack.Count == 0) return false;

        try
        {
            _undoHistory.Add(JsonSerializer.Serialize(ActiveNote.CurrentPage, _compactJsonOptions));
            if (_undoHistory.Count > MaxUndoHistory)
            {
                _undoHistory.RemoveAt(0);
            }

            string nextState = _redoStack.Pop();
            var restoredPage = JsonSerializer.Deserialize<Page>(nextState, _compactJsonOptions);
            if (restoredPage != null)
            {
                ActiveNote.Pages[ActiveNote.ActivePageIndex] = restoredPage;
                ActiveNote.UpdatedAt = DateTime.UtcNow;
                SaveToDisk();
                CanvasContentChanged?.Invoke();
                UndoStateChanged?.Invoke();
                return true;
            }
        }
        catch { }

        return false;
    }

    public Note CreateNote(string? title = null, Subcategory? targetSubcategory = null)
    {
        var newNote = new Note
        {
            Title = string.IsNullOrWhiteSpace(title)
                ? $"{LocalizationService.Instance.Get("NewNote")} {DateTime.Now:dd.MM HH:mm}"
                : title.Trim(),
            Pages = new List<Page>
            {
                new Page
                {
                    Title = $"{LocalizationService.Instance.Get("Page")} 1",
                    PaperStyle = ActiveNote?.CurrentPage?.PaperStyle ?? PaperStyle.Grid,
                    PaperColor = ActiveNote?.CurrentPage?.PaperColor ?? PaperColorTheme.White,
                    Width = DefaultPageWidth,
                    Height = DefaultPageHeight
                }
            }
        };

        if (Categories.Count == 0)
        {
            Categories.Add(new Category());
        }
        if (Categories[0].Subcategories.Count == 0)
        {
            Categories[0].Subcategories.Add(new Subcategory());
        }

        var sub = targetSubcategory ?? Categories[0].Subcategories[0];
        sub.Notes.Insert(0, newNote);
        ActiveNote = newNote;
        ClearUndoHistory();
        SaveToDisk();
        StateChanged?.Invoke();
        return newNote;
    }

    public Category CreateCategory(string name)
    {
        var cat = new Category
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Nowa kategoria" : name.Trim(),
            Subcategories = new List<Subcategory> { new Subcategory { Name = "Ogólne" } }
        };
        Categories.Add(cat);
        SaveToDisk();
        StateChanged?.Invoke();
        return cat;
    }

    public Subcategory CreateSubcategory(Category category, string name)
    {
        var sub = new Subcategory
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Nowy podfolder" : name.Trim()
        };
        category.Subcategories.Add(sub);
        category.IsExpanded = true;
        SaveToDisk();
        StateChanged?.Invoke();
        return sub;
    }

    public void RenameCategory(Category category, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        category.Name = newName.Trim();
        SaveToDisk();
        StateChanged?.Invoke();
    }

    public void RenameSubcategory(Subcategory subcategory, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        subcategory.Name = newName.Trim();
        SaveToDisk();
        StateChanged?.Invoke();
    }

    public void SetCategoryIcon(Category category, string iconKey)
    {
        category.Icon = iconKey;
        SaveToDisk();
        StateChanged?.Invoke();
    }

    public void SetSubcategoryIcon(Subcategory subcategory, string iconKey)
    {
        subcategory.Icon = iconKey;
        SaveToDisk();
        StateChanged?.Invoke();
    }

    public void ToggleNoteFavorite(Note note)
    {
        note.IsFavorite = !note.IsFavorite;
        note.UpdatedAt = DateTime.UtcNow;
        SaveToDisk();
        StateChanged?.Invoke();
    }

    public bool DeleteCategory(Category category)
    {
        if (Categories.Count <= 1) return false;
        if (!Categories.Remove(category)) return false;
        if (ActiveNote == null || !ContainsNote(ActiveNote))
        {
            ActiveNote = FindFirstNote() ?? CreateNote();
            ClearUndoHistory();
            CanvasContentChanged?.Invoke();
        }
        SaveToDisk();
        StateChanged?.Invoke();
        return true;
    }

    public bool DeleteSubcategory(Category parent, Subcategory subcategory)
    {
        if (parent.Subcategories.Count <= 1) return false;
        if (!parent.Subcategories.Remove(subcategory)) return false;
        if (ActiveNote == null || !ContainsNote(ActiveNote))
        {
            ActiveNote = FindFirstNote() ?? CreateNote();
            ClearUndoHistory();
            CanvasContentChanged?.Invoke();
        }
        SaveToDisk();
        StateChanged?.Invoke();
        return true;
    }

    private bool ContainsNote(Note note)
    {
        foreach (var cat in Categories)
            foreach (var sub in cat.Subcategories)
                if (sub.Notes.Contains(note)) return true;
        return false;
    }

    public void MoveNoteToSubcategory(Note note, Subcategory targetSubcategory)
    {
        foreach (var cat in Categories)
        {
            foreach (var sub in cat.Subcategories)
            {
                if (sub.Notes.Remove(note))
                {
                    targetSubcategory.Notes.Insert(0, note);
                    note.UpdatedAt = DateTime.UtcNow;
                    SaveToDisk();
                    StateChanged?.Invoke();
                    return;
                }
            }
        }
    }

    public void ExpandActivePage(double addWidth, double addHeight)
    {
        if (ActiveNote?.CurrentPage == null) return;
        PushUndoState();
        ActiveNote.CurrentPage.Width += addWidth;
        ActiveNote.CurrentPage.Height += addHeight;
        NotifyCanvasChanged();
    }

    public void DeleteNote(Note note)
    {
        foreach (var cat in Categories)
        {
            foreach (var sub in cat.Subcategories)
            {
                if (sub.Notes.Remove(note))
                {
                    if (ActiveNote == note)
                    {
                        ActiveNote = FindFirstNote();
                        ClearUndoHistory();
                    }
                    if (ActiveNote == null)
                    {
                        CreateNote();
                        return;
                    }
                    SaveToDisk();
                    CanvasContentChanged?.Invoke();
                    StateChanged?.Invoke();
                    return;
                }
            }
        }
    }

    public void RenameNote(Note note, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) return;
        note.Title = newTitle.Trim();
        note.UpdatedAt = DateTime.UtcNow;
        SaveToDisk();
        StateChanged?.Invoke();
    }

    public void SelectNote(Note note)
    {
        if (ActiveNote == note) return;
        ActiveNote = note;
        ClearUndoHistory();
        CanvasContentChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void AddPageToActiveNote()
    {
        if (ActiveNote == null) return;
        var current = ActiveNote.CurrentPage;
        var newPage = new Page
        {
            Title = $"{LocalizationService.Instance.Get("Page")} {ActiveNote.Pages.Count + 1}",
            PaperStyle = current.PaperStyle,
            PaperColor = current.PaperColor,
            Width = DefaultPageWidth,
            Height = DefaultPageHeight
        };
        ActiveNote.Pages.Add(newPage);
        ActiveNote.ActivePageIndex = ActiveNote.Pages.Count - 1;
        ActiveNote.UpdatedAt = DateTime.UtcNow;
        ClearUndoHistory();
        SaveToDisk();
        CanvasContentChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public bool DeleteCurrentPageFromActiveNote()
    {
        if (ActiveNote == null || ActiveNote.Pages.Count <= 1) return false;
        int idx = Math.Clamp(ActiveNote.ActivePageIndex, 0, ActiveNote.Pages.Count - 1);
        ActiveNote.Pages.RemoveAt(idx);
        ActiveNote.ActivePageIndex = Math.Clamp(idx, 0, ActiveNote.Pages.Count - 1);
        ActiveNote.UpdatedAt = DateTime.UtcNow;
        ClearUndoHistory();
        SaveToDisk();
        CanvasContentChanged?.Invoke();
        StateChanged?.Invoke();
        return true;
    }

    public void SwitchPage(int index)
    {
        if (ActiveNote == null) return;
        if (index < 0 || index >= ActiveNote.Pages.Count || index == ActiveNote.ActivePageIndex) return;
        ActiveNote.ActivePageIndex = index;
        ClearUndoHistory();
        SaveToDisk();
        CanvasContentChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void ClearCurrentPage()
    {
        if (ActiveNote?.CurrentPage == null) return;
        var page = ActiveNote.CurrentPage;
        if (page.Strokes.Count == 0 && page.Tables.Count == 0 && page.TextBoxes.Count == 0 && page.Images.Count == 0) return;

        PushUndoState();
        page.Strokes.Clear();
        page.Tables.Clear();
        page.TextBoxes.Clear();
        page.Images.Clear();
        NotifyCanvasChanged();
    }

    private Note? FindFirstNote()
    {
        foreach (var cat in Categories)
        {
            foreach (var sub in cat.Subcategories)
            {
                if (sub.Notes.Count > 0) return sub.Notes[0];
            }
        }
        return null;
    }

    public string GetDataFolder()
    {
        string folder = !string.IsNullOrWhiteSpace(CustomDataFolderOverride)
            ? CustomDataFolderOverride
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quillfolio");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        return folder;
    }

    public string GetDataFilePath() => Path.Combine(GetDataFolder(), "quillfolio_data.json");

    public string GetSettingsFilePath() => Path.Combine(GetDataFolder(), "quillfolio_settings.json");

    public void SaveSettings()
    {
        try
        {
            var settings = new AppSettings
            {
                Language = LocalizationService.Instance.CurrentLanguage,
                StylusPressureFactor = StylusPressureFactor,
                IsShapeSnapEnabled = IsShapeSnapEnabled,
                ShapeHoldDelayMs = ShapeHoldDelayMs,
                SelectedColorHex = SelectedColorHex,
                SelectedThickness = SelectedThickness,
                IsSmoothingEnabled = IsSmoothingEnabled,
                GuidelineSpacing = GuidelineSpacing,
                EraserMode = SelectedEraserMode,
                SelectionMode = SelectedSelectionMode,
                FountainNibAngleDegrees = FountainNibAngleDegrees,
                LaserFadeMs = LaserFadeMs,
                EraserSizeMultiplier = EraserSizeMultiplier,
                AutoSelectInsertedShapes = AutoSelectInsertedShapes,
                DeskTheme = DeskTheme,
                AppTheme = AppTheme,
                DefaultPageWidth = DefaultPageWidth,
                DefaultPageHeight = DefaultPageHeight,
                DefaultStartupTool = DefaultStartupTool,
                Shortcuts = Shortcuts
            };
            string json = JsonSerializer.Serialize(settings, _settingsJsonOptions);
            WriteJsonAtomic(GetSettingsFilePath(), json);
        }
        catch { }
    }

    public void LoadSettings()
    {
        try
        {
            string path = GetSettingsFilePath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    LocalizationService.Instance.SetLanguage(settings.Language);
                    StylusPressureFactor = Math.Clamp(settings.StylusPressureFactor, 0.5, 2.5);
                    IsShapeSnapEnabled = settings.IsShapeSnapEnabled;
                    ShapeHoldDelayMs = Math.Clamp(settings.ShapeHoldDelayMs, 250, 800);
                    if (!string.IsNullOrWhiteSpace(settings.SelectedColorHex))
                        SelectedColorHex = settings.SelectedColorHex;
                    SelectedThickness = Math.Clamp(settings.SelectedThickness, 0.5, 40.0);
                    IsSmoothingEnabled = settings.IsSmoothingEnabled;
                    GuidelineSpacing = Math.Clamp(settings.GuidelineSpacing, 16.0, 64.0);
                    SelectedEraserMode = settings.EraserMode;
                    SelectedSelectionMode = settings.SelectionMode;
                    FountainNibAngleDegrees = Math.Clamp(settings.FountainNibAngleDegrees, 15.0, 75.0);
                    LaserFadeMs = Math.Clamp(settings.LaserFadeMs, 500, 2500);
                    EraserSizeMultiplier = Math.Clamp(settings.EraserSizeMultiplier, 1.2, 4.0);
                    AutoSelectInsertedShapes = settings.AutoSelectInsertedShapes;
                    if (!string.IsNullOrWhiteSpace(settings.DeskTheme))
                        DeskTheme = settings.DeskTheme;
                    AppTheme = settings.AppTheme;
                    DefaultPageWidth = Math.Clamp(settings.DefaultPageWidth, 1200.0, 4800.0);
                    DefaultPageHeight = Math.Clamp(settings.DefaultPageHeight, 1600.0, 6400.0);
                    DefaultStartupTool = settings.DefaultStartupTool;
                    if (settings.Shortcuts != null && settings.Shortcuts.Count > 0)
                        Shortcuts = settings.Shortcuts;
                }
            }
        }
        catch { }
    }

    public void SaveToDisk(bool immediate = false)
    {
        _uiSyncContext ??= SynchronizationContext.Current;
        _hasPendingSave = true;

        if (!immediate && _uiSyncContext != null)
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = new Timer(_ =>
            {
                _uiSyncContext?.Post(_ => EnqueueAsyncSerializeAndWrite(), null);
            }, null, 350, Timeout.Infinite);
            return;
        }

        _saveDebounceTimer?.Dispose();
        _saveDebounceTimer = null;
        FlushSaveToDisk();
    }

    private void EnqueueAsyncSerializeAndWrite()
    {
        if (!_hasPendingSave) return;
        _hasPendingSave = false;
        try
        {
            string path = GetDataFilePath();
            string json = JsonSerializer.Serialize(Categories, _compactJsonOptions);
            _lastWriteTask = Task.Run(() => WriteJsonAtomic(path, json));
        }
        catch { }
    }

    public void FlushSaveToDisk()
    {
        _saveDebounceTimer?.Dispose();
        _saveDebounceTimer = null;
        try
        {
            _lastWriteTask.Wait(1500);
        }
        catch { }

        _hasPendingSave = false;
        try
        {
            string path = GetDataFilePath();
            string json = JsonSerializer.Serialize(Categories, _compactJsonOptions);
            WriteJsonAtomic(path, json);
        }
        catch { }
    }

    private void WriteJsonAtomic(string path, string json)
    {
        lock (_fileWriteLock)
        {
            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);
        }
    }

    public void LoadFromDisk()
    {
        try
        {
            string path = GetDataFilePath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<List<Category>>(json, _compactJsonOptions);
                if (loaded != null && loaded.Count > 0)
                {
                    Categories = loaded;
                    ActiveNote = FindFirstNote();
                }
            }
        }
        catch { }
    }

    public void NotifyCanvasChanged()
    {
        if (ActiveNote != null)
        {
            ActiveNote.UpdatedAt = DateTime.UtcNow;
        }
        CanvasContentChanged?.Invoke();
        SaveToDisk();
        UndoStateChanged?.Invoke();
    }
}
