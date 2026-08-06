using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LimbusCalc.ViewModels;

/// <summary>
/// Базовый класс для моделей представления: сообщает интерфейсу об изменении свойств,
/// чтобы привязки в XAML обновлялись сами.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Записывает значение и уведомляет интерфейс, если оно действительно изменилось.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
