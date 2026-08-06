using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>
/// Дополнительная цель монеты. Нумерация начинается со второй: первая — основная,
/// её сопротивления настраиваются в панели Parameters.
/// </summary>
public sealed class SubtargetViewModel : ObservableObject
{
    private int _number;

    public int Number
    {
        get => _number;
        set
        {
            if (SetProperty(ref _number, value))
            {
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public string Title => $"Subtarget {Number}";

    /// <summary>Сопротивления в порядке <see cref="ElementOptions.ResistanceOrder"/>.</summary>
    public IReadOnlyList<ResistanceViewModel> Resistances { get; init; } = [];

    /// <summary>
    /// Откуда брать сопротивления основной цели при сбросе. Подцель не знает про
    /// панель Parameters, поэтому источник ей выдаёт список монет.
    /// </summary>
    public required Func<Element, double> MainResistance { get; init; }

    /// <summary>Вернуть сопротивления к текущим значениям основной цели.</summary>
    public void ResetToMain()
    {
        foreach (ResistanceViewModel resistance in Resistances)
        {
            resistance.Value = MainResistance(resistance.Option.Element);
        }
    }

    public ResistanceSet ToModel()
    {
        ResistanceSet set = new();

        foreach (ResistanceViewModel resistance in Resistances)
        {
            set[resistance.Option.Element] = resistance.Value;
        }

        return set;
    }
}
