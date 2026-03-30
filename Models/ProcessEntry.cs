using System.ComponentModel;

namespace ProxyMaster.Models;

/// <summary>Запись о процессе в UI-списке выбора приложений.</summary>
public class ProcessEntry : INotifyPropertyChanged
{
    public string Name        { get; init; } = "";
    public string DisplayPath { get; init; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Name;
}
