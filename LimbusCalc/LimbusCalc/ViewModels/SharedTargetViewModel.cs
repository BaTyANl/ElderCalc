namespace LimbusCalc.ViewModels;

/// <summary>
/// Общая часть дополнительной цели: подцель с одним номером — это один враг,
/// поэтому его сопротивления и Time Moratorium одни на все монеты скилла.
/// Всё остальное (dyn mod, крит, разница уровней) у каждой монеты своё.
/// </summary>
public sealed class SharedTargetViewModel : ObservableObject
{
    private bool _timeMoratorium;
    private int _timeMoratoriumStacks = 1;

    /// <summary>Сопротивления в порядке <see cref="ElementOptions.ResistanceOrder"/>.</summary>
    public required IReadOnlyList<ResistanceViewModel> Resistances { get; init; }

    public bool TimeMoratorium
    {
        get => _timeMoratorium;
        set => SetProperty(ref _timeMoratorium, value);
    }

    /// <summary>Число стаков; допустимы только 1 и 2.</summary>
    public int TimeMoratoriumStacks
    {
        get => _timeMoratoriumStacks;
        set
        {
            int clamped = Math.Clamp(value, 1, 2);

            if (_timeMoratoriumStacks != clamped)
            {
                _timeMoratoriumStacks = clamped;
                OnPropertyChanged();
            }
            else if (value != clamped)
            {
                // Значение уже на границе, но ввели за её пределами — вернём поле к границе.
                OnPropertyChanged();
            }
        }
    }
}
