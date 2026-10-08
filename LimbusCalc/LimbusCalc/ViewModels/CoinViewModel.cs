using System.Collections.ObjectModel;
using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>A coin column: inputs on top, calculated values below the line.</summary>
public sealed class CoinViewModel : ObservableObject
{
    private int _number;
    private bool _active = true;
    private double _power;
    private double _modDynPercent;
    private double _offenseDefenseDiff;
    private bool _hasCrit;
    private double _critPercent = 20.0;
    private int _weight = 1;

    private double _roll;
    private double _modStat;
    private double _damage;

    /// <summary>Names of properties whose change requires a recalculation.</summary>
    public static readonly HashSet<string> InputPropertyNames =
    [
        nameof(Active),
        nameof(Power),
        nameof(ModDynPercent),
        nameof(OffenseDefenseDiff),
        nameof(HasCrit),
        nameof(CritPercent),
        nameof(Weight),
    ];

    /// <summary>1-based position of the coin in the skill; shown as "Coin N".</summary>
    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    /// <summary>The coin landed heads and adds its power to the roll.</summary>
    public bool Active
    {
        get => _active;
        set => SetProperty(ref _active, value);
    }

    public double Power
    {
        get => _power;
        set => SetProperty(ref _power, value);
    }

    /// <summary>
    /// Dynamic modifier in percent: 63 means +63%, a multiplier of 1.63.
    /// A negative value lowers damage: -37 gives a multiplier of 0.63.
    /// </summary>
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

    /// <summary>
    /// This coin's bonus values. The order matches the bonus rows in the table;
    /// <see cref="MainViewModel"/> keeps the list in sync.
    /// </summary>
    public ObservableCollection<CoinBonusViewModel> Bonuses { get; } = [];

    /// <summary>The coin crit.</summary>
    public bool HasCrit
    {
        get => _hasCrit;
        set => SetProperty(ref _hasCrit, value);
    }

    /// <summary>This coin's crit modifier in percent: 20 means +20%.</summary>
    public double CritPercent
    {
        get => _critPercent;
        set => SetProperty(ref _critPercent, value);
    }

    /// <summary>This coin's weight — how many targets it hits. Whole numbers only.</summary>
    public int Weight
    {
        get => _weight;
        set
        {
            if (SetProperty(ref _weight, value))
            {
                OnPropertyChanged(nameof(HasSubtargets));
            }
        }
    }

    /// <summary>Whether there are extra targets, from the second one on.</summary>
    public bool HasSubtargets => Weight >= 2;

    /// <summary>
    /// Extra targets of this coin, starting from the second. <see cref="MainViewModel"/>
    /// keeps the list in line with the weight.
    /// </summary>
    public ObservableCollection<SubtargetViewModel> Subtargets { get; } = [];

    /// <summary>Column labels in the subtargets window: the same elements in the same order.</summary>
    public IReadOnlyList<ElementOption> ResistanceHeaders => ElementOptions.ResistanceOrder;

    /// <summary>Calculated roll of this coin: the previous roll plus its power when heads.</summary>
    public double Roll
    {
        get => _roll;
        private set => SetProperty(ref _roll, value);
    }

    /// <summary>Calculated Mod stat for the main target.</summary>
    public double ModStat
    {
        get => _modStat;
        private set => SetProperty(ref _modStat, value);
    }

    /// <summary>Calculated damage of the coin without Time Moratorium, as shown in the column.</summary>
    public double Damage
    {
        get => _damage;
        private set => SetProperty(ref _damage, value);
    }

    /// <summary>
    /// A copy of all entered values. The number isn't copied: the coin list assigns it.
    /// Calculated Roll/ModStat/Damage aren't copied either — they come with the next recalculation.
    /// </summary>
    public CoinViewModel Clone() => new()
    {
        Active = Active,
        Power = Power,
        ModDynPercent = ModDynPercent,
        OffenseDefenseDiff = OffenseDefenseDiff,
        HasCrit = HasCrit,
        CritPercent = CritPercent,
        Weight = Weight,
    };

    /// <param name="passiveModDynPercent">
    /// A Dyn mod bonus in percent shared by all coins; added to the coin's own.
    /// </param>
    /// <param name="clashCount">Number of clashes, shared by all coins of the skill.</param>
    public Coin ToModel(double passiveModDynPercent, int clashCount)
    {
        Coin coin = new()
        {
            Active = Active,
            Power = Power,
            ModDyn = 1.0 + ((ModDynPercent + passiveModDynPercent) / 100.0),
            OffenseDefenseDiff = OffenseDefenseDiff,
            HasCrit = HasCrit,
            Crit = CritPercent / 100.0,
            ClashCount = clashCount,
            Weight = Weight,
        };

        foreach (CoinBonusViewModel bonus in Bonuses)
        {
            coin.Bonuses.Add(new CoinBonus
            {
                Kind = bonus.Row.Kind,
                Target = bonus.Row.Target.Element,
                Value = bonus.Value,
            });
        }

        foreach (SubtargetViewModel subtarget in Subtargets)
        {
            coin.Subtargets.Add(subtarget.ToModel(passiveModDynPercent));
        }

        return coin;
    }

    /// <summary>Shows the calculated values of this coin.</summary>
    public void ApplyResult(CoinBreakdown breakdown)
    {
        Roll = breakdown.Roll;
        ModStat = breakdown.ModStat;
        // The table shows damage without Time Moratorium: its bonus counts only in the total.
        Damage = breakdown.BaseDamage;
    }
}
