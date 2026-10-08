using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>
/// Main target values of a coin. A subtarget starts as a copy of them and returns to them on reset.
/// </summary>
public readonly record struct MainTargetParameters(
    double ModDynPercent,
    double OffenseDefenseDiff,
    bool HasCrit,
    double CritPercent,
    bool TimeMoratorium,
    int TimeMoratoriumStacks);

/// <summary>
/// An extra target of a coin. Numbering starts from the second: the first is the main target.
/// The name identifies the enemy: subtargets with the same name are the same enemy, so they
/// share resistances and Time Moratorium through <see cref="Shared"/>. Renaming moves the
/// subtarget into the new name's group right away.
/// Attack modifiers (dyn mod, crit, level difference) are per coin.
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

    /// <summary>Position among the coin's targets: 2 for the first subtarget, since 1 is the main target.</summary>
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

    /// <summary>The default name; a reset returns to it.</summary>
    public string DefaultName => $"Subtarget {Number}";

    /// <summary>
    /// The target's name, editable in the subtargets list. It also identifies the enemy:
    /// type the name of another coin's subtarget and its resistances and moratorium
    /// come along.
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

            // On the first assignment from the initializer there's no shared part yet —
            // the initializer sets it too, so there's nothing to move.
            if (_shared is not null)
            {
                Shared = SharedFor(this);
            }
        }
    }

    /// <summary>Title of the parameters window for this subtarget.</summary>
    public string ParametersTitle =>
        string.IsNullOrWhiteSpace(Name) ? $"{DefaultName} parameters" : $"{Name} parameters";

    /// <summary>The part shared with other coins: the group of this name.</summary>
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
    /// Where to get the shared part for the current name. The coin list manages the groups:
    /// there's one for the whole window and it knows every name.
    /// </summary>
    public required Func<SubtargetViewModel, SharedTargetViewModel> SharedFor { get; init; }

    public IReadOnlyList<ResistanceViewModel> Resistances => Shared.Resistances;

    /// <summary>Dynamic modifier in percent: 63 means +63%.</summary>
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

    /// <summary>Crit modifier in percent: 20 means +20%.</summary>
    public double CritPercent
    {
        get => _critPercent;
        set => SetProperty(ref _critPercent, value);
    }

    /// <summary>
    /// Where to take the main target's resistances on reset. The subtarget doesn't know about
    /// the Parameters panel, so the coin list supplies the source.
    /// </summary>
    public required Func<Element, double> MainResistance { get; init; }

    /// <summary>Where to take the main target's modifiers on reset.</summary>
    public required Func<MainTargetParameters> MainParameters { get; init; }

    /// <summary>
    /// Returns the subtarget to the main target's state and its default name.
    /// The name changes first: the group to reset is the one we end up in, not the one we
    /// leave. It's shared, so the reset shows on other coins too.
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

    /// <param name="passiveModDynPercent">A shared Dyn mod bonus; added to the subtarget's own.</param>
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
