using System.ComponentModel;
using Quillfolio.Core.Services;

namespace Quillfolio.WPF.Services;

/// <summary>
/// Bindable localization proxy for XAML elements and DataTemplates.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private Loc()
    {
        LocalizationService.Instance.LanguageChanged += () =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        };
    }

    public string this[string key] => LocalizationService.Instance.Get(key);
}
