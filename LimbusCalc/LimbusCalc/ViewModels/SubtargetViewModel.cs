using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>
/// Значения основной цели монеты. Подцель заводится их копией и возвращается к ним по сбросу.
/// </summary>
public readonly record struct MainTargetParameters(
    double ModDynPercent,
    double OffenseDefenseDiff,
    bool HasCrit,
    double CritPercent,
    bool TimeMoratorium,
    int TimeMoratoriumStacks);

/// <summary>
/// Дополнительная цель монеты. Нумерация начинается со второй: первая — основная.
/// Врага задаёт название: подцели с одинаковым названием — это один и тот же враг,
/// поэтому сопротивления и Time Moratorium они делят через <see cref="Shared"/>.
/// Переименование сразу переносит подцель в группу нового названия.
/// Модификаторы атаки (dyn mod, крит, разница уровней) у каждой монеты свои.
/// </summary>
public sealed class SubtargetViewModel : ObservableObject
{
    private int _number;
    private string _name = string.Empty;
    private SharedTargetViewModel _shared = null!;
    private double _modDynPercent;
    private double _offenseDefenseDiff;
    private bool _hasCrit;
    private double _critPercent = 20.0;

    public int Number
    {
        get => _number;
        set
        {
            if (SetProperty(ref _number, value))
            {
                OnPropertyChanged(nameof(DefaultName));
                OnPropertyChanged(nameof(ParametersTitle));
            }
        }
    }

    /// <summary>Как цель называется по умолчанию; к нему возвращает сброс.</summary>
    public string DefaultName => $"Subtarget {Number}";

    /// <summary>
    /// Название цели, его можно поменять в списке подцелей. Оно же определяет врага:
    /// стоит вписать название подцели с другой монеты — и сюда подтянутся её
    /// сопротивления и мораторий.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ParametersTitle));

            // При первом присваивании из инициализатора общей части ещё нет —
            // её кладут туда же, в инициализатор, и переезжать некуда.
            if (_shared is not null)
            {
                Shared = SharedFor(this);
            }
        }
    }

    public string ParametersTitle =>
        string.IsNullOrWhiteSpace(Name) ? $"{DefaultName} parameters" : $"{Name} parameters";

    /// <summary>Общая с другими монетами часть цели: группа этого названия.</summary>
    public required SharedTargetViewModel Shared
    {
        get => _shared;
        set
        {
            if (SetProperty(ref _shared, value))
            {
                OnPropertyChanged(nameof(Resistances));
            }
        }
    }

    /// <summary>
    /// Где взять общую часть для текущего названия подцели. Группами заведует список
    /// монет: он один на всё окно и знает про все названия сразу.
    /// </summary>
    public required Func<SubtargetViewModel, SharedTargetViewModel> SharedFor { get; init; }

    public IReadOnlyList<ResistanceViewModel> Resistances => Shared.Resistances;

    /// <summary>Динамический модификатор в процентах: 63 означает +63%.</summary>
    public double ModDynPercent
    {
        get => _modDynPercent;
        set => SetProperty(ref _modDynPercent, value);
    }

    public double OffenseDefenseDiff
    {
        get => _offenseDefenseDiff;
        set => SetProperty(ref _offenseDefenseDiff, value);
    }

    public bool HasCrit
    {
        get => _hasCrit;
        set => SetProperty(ref _hasCrit, value);
    }

    /// <summary>Крит-модификатор в процентах: 20 означает +20%.</summary>
    public double CritPercent
    {
        get => _critPercent;
        set => SetProperty(ref _critPercent, value);
    }

    /// <summary>
    /// Откуда брать сопротивления основной цели при сбросе. Подцель не знает про
    /// панель Parameters, поэтому источник ей выдаёт список монет.
    /// </summary>
    public required Func<Element, double> MainResistance { get; init; }

    /// <summary>Откуда брать модификаторы основной цели при сбросе.</summary>
    public required Func<MainTargetParameters> MainParameters { get; init; }

    /// <summary>
    /// Вернуть подцель к состоянию основной цели и к названию по умолчанию.
    /// Название меняем первым: сбрасывать нужно ту группу, в которой окажемся,
    /// а не ту, из которой уходим. Она общая — сброс виден и на других монетах.
    /// </summary>
    public void ResetToMain()
    {
        Name = DefaultName;

        foreach (ResistanceViewModel resistance in Resistances)
        {
            resistance.Value = MainResistance(resistance.Option.Element);
        }

        MainTargetParameters main = MainParameters();

        ModDynPercent = main.ModDynPercent;
        OffenseDefenseDiff = main.OffenseDefenseDiff;
        HasCrit = main.HasCrit;
        CritPercent = main.CritPercent;
        Shared.TimeMoratorium = main.TimeMoratorium;
        Shared.TimeMoratoriumStacks = main.TimeMoratoriumStacks;
    }

    /// <param name="passiveModDynPercent">Общая надбавка к Dyn mod; складывается с собственной.</param>
    public SubtargetOverride ToModel(double passiveModDynPercent)
    {
        SubtargetOverride model = new()
        {
            ModDyn = 1.0 + ((ModDynPercent + passiveModDynPercent) / 100.0),
            OffenseDefenseDiff = OffenseDefenseDiff,
            HasCrit = HasCrit,
            Crit = CritPercent / 100.0,
            TimeMoratorium = Shared.TimeMoratorium,
            TimeMoratoriumStacks = Shared.TimeMoratoriumStacks,
        };

        foreach (ResistanceViewModel resistance in Resistances)
        {
            model.Resistances[resistance.Option.Element] = resistance.Value;
        }

        return model;
    }
}
