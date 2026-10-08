using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>State of the main window: shared parameters, the coin list and the total.</summary>
public sealed class MainViewModel : ObservableObject
{
    private double _baseRoll;
    private double _passiveModDynPercent;
    private ElementOption _skillType = ElementOptions.DamageTypes[0];
    private ElementOption _skillSin = ElementOptions.Sins[0];
    private int _clashCount;
    private bool _timeMoratorium;
    private int _timeMoratoriumStacks = 1;
    private double _total;
    private double _totalBase;
    private double _totalLeading;
    private double _moratoriumBuff = 1.0;
    private bool _showMoratoriumEquation;
    private IReadOnlyList<TargetDamageRow> _damageByTarget = [];
    private IReadOnlyList<TargetColumnViewModel> _coinColumns = [];
    private TargetColumnViewModel _titleColumn = null!;
    private TargetColumnViewModel _totalColumn = null!;
    private bool _hasMultipleTargets;

    private TargetSortKey _sortKey = TargetSortKey.None;
    private int _sortCoin = -1;
    private bool _sortDescending;

    private readonly List<ResistanceViewModel> _allResistances = [];

    /// <summary>
    /// Shared parts of subtargets by enemy name. Case and surrounding spaces don't matter:
    /// "Boss" and "boss " are the same enemy.
    /// </summary>
    private readonly Dictionary<string, SharedTargetViewModel> _sharedTargets =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The skill's coins, left to right.</summary>
    public ObservableCollection<CoinViewModel> Coins { get; } = [];

    /// <summary>The ID reference table (the ID tab).</summary>
    public TableViewModel IdTable { get; } = TableViewModel.CreateIdTable();

    /// <summary>The E.G.O. reference table (the E.G.O. tab).</summary>
    public TableViewModel EgoTable { get; } = TableViewModel.CreateEgoTable();

    /// <summary>Bonus rows: kind and target are shared by all coins, values are per coin.</summary>
    public ObservableCollection<BonusRowViewModel> BonusRows { get; } = [];

    /// <summary>Resistances to damage types, three in a row.</summary>
    public IReadOnlyList<ResistanceViewModel> TypeResistances { get; }

    /// <summary>Top row of sin resistances: three of them.</summary>
    public IReadOnlyList<ResistanceViewModel> SinResistancesTop { get; }

    /// <summary>Bottom row of sin resistances: four of them.</summary>
    public IReadOnlyList<ResistanceViewModel> SinResistancesBottom { get; }

    public MainViewModel()
    {
        // Same order as in the game: Slash-Pierce-Blunt, then sins as a 3 + 4 trapezoid.
        TypeResistances = CreateResistances(Element.Slash, Element.Pierce, Element.Blunt);
        SinResistancesTop = CreateResistances(Element.Wrath, Element.Lust, Element.Sloth);
        SinResistancesBottom = CreateResistances(
            Element.Gluttony, Element.Gloom, Element.Pride, Element.Envy);

        Coins.CollectionChanged += OnCoinsCollectionChanged;

        for (int i = 0; i < 3; i++)
        {
            AddCoin();
        }
    }

    public IReadOnlyList<ElementOption> DamageTypeOptions => ElementOptions.DamageTypes;

    public IReadOnlyList<ElementOption> SinOptions => ElementOptions.Sins;

    /// <summary>"Base roll": the roll before coin power is added.</summary>
    public double BaseRoll
    {
        get => _baseRoll;
        set
        {
            if (SetProperty(ref _baseRoll, value))
            {
                Recalculate();
            }
        }
    }

    /// <summary>A Dyn mod bonus in percent shared by all coins.</summary>
    public double PassiveModDynPercent
    {
        get => _passiveModDynPercent;
        set
        {
            if (SetProperty(ref _passiveModDynPercent, value))
            {
                Recalculate();
            }
        }
    }

    /// <summary>Damage type of the skill; the target's resistance to it affects Mod stat.</summary>
    public ElementOption SkillType
    {
        get => _skillType;
        set
        {
            if (SetProperty(ref _skillType, value))
            {
                Recalculate();
            }
        }
    }

    /// <summary>Sin of the skill; the target's resistance to it affects Mod stat.</summary>
    public ElementOption SkillSin
    {
        get => _skillSin;
        set
        {
            if (SetProperty(ref _skillSin, value))
            {
                Recalculate();
            }
        }
    }

    /// <summary>Number of clashes, shared by all coins of the skill; each adds 3% to Mod stat.</summary>
    public int ClashCount
    {
        get => _clashCount;
        set
        {
            if (SetProperty(ref _clashCount, value))
            {
                Recalculate();
            }
        }
    }

    /// <summary>Time Moratorium is on: damage grows per stack and becomes Sloth damage.</summary>
    public bool TimeMoratorium
    {
        get => _timeMoratorium;
        set
        {
            if (SetProperty(ref _timeMoratorium, value))
            {
                Recalculate();
            }
        }
    }

    /// <summary>Number of stacks; only 1 and 2 are allowed, anything else snaps to the limit.</summary>
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
                Recalculate();
            }
            else if (value != clamped)
            {
                // Already at the limit but the input went past it: snap the field back.
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The skill's total damage including Time Moratorium.</summary>
    public double Total
    {
        get => _total;
        private set => SetProperty(ref _total, value);
    }

    /// <summary>The total without Time Moratorium: the left side of the equation in the total row.</summary>
    public double TotalBase
    {
        get => _totalBase;
        private set => SetProperty(ref _totalBase, value);
    }

    /// <summary>
    /// The red number in the total row. With the equation shown it's the left side, i.e. the
    /// damage before the moratorium. Without the equation it's the real total, otherwise
    /// the moratorium bonus would vanish from the screen.
    /// </summary>
    public double TotalLeading
    {
        get => _totalLeading;
        private set => SetProperty(ref _totalLeading, value);
    }

    /// <summary>
    /// The Time Moratorium multiplier: the per-stack bonus times the main target's Sloth
    /// resistance. Taken from the formula rather than as a ratio of totals, because flooring
    /// distorts small numbers (5 becomes 11, giving 2.2 instead of 2.3).
    /// If the main target has no moratorium but subtargets do, the formula has no single
    /// multiplier, so the actual ratio of totals is shown instead.
    /// </summary>
    public double MoratoriumBuff
    {
        get => _moratoriumBuff;
        private set => SetProperty(ref _moratoriumBuff, value);
    }

    /// <summary>
    /// Whether the total row shows the equation. Based on the moratorium actually changing
    /// the damage, not just on the shared checkbox: it may be off while a subtarget has it on.
    /// </summary>
    public bool ShowMoratoriumEquation
    {
        get => _showMoratoriumEquation;
        private set => SetProperty(ref _showMoratoriumEquation, value);
    }

    /// <summary>Damage split by target — the table in a separate window.</summary>
    public IReadOnlyList<TargetDamageRow> DamageByTarget
    {
        get => _damageByTarget;
        private set => SetProperty(ref _damageByTarget, value);
    }

    /// <summary>Coin columns of the damage-by-target table; they double as sort buttons.</summary>
    public IReadOnlyList<TargetColumnViewModel> CoinColumns
    {
        get => _coinColumns;
        private set => SetProperty(ref _coinColumns, value);
    }

    /// <summary>The target name column: sorts alphabetically.</summary>
    public TargetColumnViewModel TitleColumn
    {
        get => _titleColumn;
        private set => SetProperty(ref _titleColumn, value);
    }

    /// <summary>The per-target total column: sorts by damage.</summary>
    public TargetColumnViewModel TotalColumn
    {
        get => _totalColumn;
        private set => SetProperty(ref _totalColumn, value);
    }

    /// <summary>
    /// Sorts the damage-by-target table by this column. Clicking the same column again
    /// reverses the order. Damage starts from the highest — that's usually what matters —
    /// and names from the start of the alphabet.
    /// </summary>
    public void SortDamageByTarget(TargetColumnViewModel column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (_sortKey == column.Key && _sortCoin == column.CoinIndex)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortKey = column.Key;
            _sortCoin = column.CoinIndex;
            _sortDescending = column.Key != TargetSortKey.Title;
        }

        Recalculate();
    }

    /// <summary>Whether there's anything to split: at least one coin hits more than one target.</summary>
    public bool HasMultipleTargets
    {
        get => _hasMultipleTargets;
        private set => SetProperty(ref _hasMultipleTargets, value);
    }

    /// <summary>
    /// A new coin copies the previous one: coins of one skill usually share parameters,
    /// and retyping them is pointless. The first coin starts with zeros.
    /// </summary>
    public void AddCoin()
    {
        CoinViewModel? source = Coins.Count > 0 ? Coins[^1] : null;
        CoinViewModel coin = source?.Clone() ?? new CoinViewModel();

        // Bonus rows are shared, so the new coin needs as many values.
        for (int i = 0; i < BonusRows.Count; i++)
        {
            double value = source is not null && i < source.Bonuses.Count ? source.Bonuses[i].Value : 0.0;
            coin.Bonuses.Add(CreateBonusValue(BonusRows[i], value));
        }

        // The weight was copied, so there are as many subtargets; their settings come along
        // too — coins of one skill usually hit the same targets.
        SyncSubtargets(coin);

        // Add the coin to the list before copying names: the list is used to count who else
        // holds a group, and the new coin must be part of that count.
        Coins.Add(coin);

        if (source is not null)
        {
            int shared = Math.Min(coin.Subtargets.Count, source.Subtargets.Count);

            for (int i = 0; i < shared; i++)
            {
                CopySubtarget(source.Subtargets[i], coin.Subtargets[i]);
            }
        }
    }

    public void RemoveLastCoin()
    {
        if (Coins.Count > 0)
        {
            Coins.RemoveAt(Coins.Count - 1);
        }
    }

    /// <summary>Adds a bonus row with a zero value for every coin.</summary>
    public void AddBonus(BonusKind kind)
    {
        BonusRowViewModel row = new() { Kind = kind };
        row.PropertyChanged += OnBonusRowPropertyChanged;
        BonusRows.Add(row);

        foreach (CoinViewModel coin in Coins)
        {
            coin.Bonuses.Add(CreateBonusValue(row, 0.0));
        }

        Recalculate();
    }

    /// <summary>Removes a bonus row together with its value on every coin.</summary>
    public void RemoveBonus(BonusRowViewModel row)
    {
        int index = BonusRows.IndexOf(row);

        if (index < 0)
        {
            return;
        }

        row.PropertyChanged -= OnBonusRowPropertyChanged;
        BonusRows.RemoveAt(index);

        foreach (CoinViewModel coin in Coins)
        {
            if (index < coin.Bonuses.Count)
            {
                coin.Bonuses[index].PropertyChanged -= OnBonusValuePropertyChanged;
                coin.Bonuses.RemoveAt(index);
            }
        }

        Recalculate();
    }

    /// <summary>
    /// Builds the calculation input from the current state and updates every result:
    /// per-coin values, totals, the moratorium equation and the damage-by-target table.
    /// </summary>
    public void Recalculate()
    {
        Skill skill = new()
        {
            BaseRoll = BaseRoll,
            Type = SkillType.Element,
            Sin = SkillSin.Element,
        };

        foreach (CoinViewModel coin in Coins)
        {
            skill.Coins.Add(coin.ToModel(PassiveModDynPercent, ClashCount));
        }

        DamageInput input = new()
        {
            TimeMoratorium = TimeMoratorium,
            TimeMoratoriumStacks = TimeMoratoriumStacks,
        };

        input.Skills.Add(skill);

        foreach (ResistanceViewModel resistance in _allResistances)
        {
            input.Resistances[resistance.Option.Element] = resistance.Value;
        }

        // Subtarget resistances are already carried over inside each coin's ToModel.

        DamageResult result = DamageCalculator.Calculate(input);

        for (int i = 0; i < Coins.Count; i++)
        {
            Coins[i].ApplyResult(result.Coins[i]);
        }

        Total = result.Total;
        TotalBase = result.TotalBase;
        DamageByTarget = TargetDamageRow.Sort(
            TargetDamageRow.Build(result.Coins, SubtargetTitles()),
            _sortKey,
            _sortCoin,
            _sortDescending);

        UpdateColumns();

        HasMultipleTargets = DamageByTarget.Count > 1;

        // With several targets the moratorium breakdown lives in the damage-by-target window,
        // and the main window shows just the total. With one target there's nowhere else to show it.
        ShowMoratoriumEquation = result.Total != result.TotalBase && !HasMultipleTargets;
        TotalLeading = ShowMoratoriumEquation ? result.TotalBase : result.Total;

        MoratoriumBuff = TimeMoratorium
            ? (1.0 + (DamageCalculator.TimeMoratoriumPerStack * TimeMoratoriumStacks))
                * MainResistance(Element.Sloth)
            : result.TotalBase != 0.0 ? result.Total / result.TotalBase : 1.0;
    }

    /// <summary>Rebuilds the table headers: coin labels and the sort arrow.</summary>
    private void UpdateColumns()
    {
        TitleColumn = CreateColumn("Target", TargetSortKey.Title, -1);
        TotalColumn = CreateColumn("Total", TargetSortKey.Total, -1);
        CoinColumns =
        [
            .. Coins.Select((coin, index) =>
                CreateColumn($"Coin {coin.Number}", TargetSortKey.Coin, index)),
        ];
    }

    private TargetColumnViewModel CreateColumn(string title, TargetSortKey key, int coinIndex)
    {
        bool active = _sortKey == key && _sortCoin == coinIndex;

        return new TargetColumnViewModel
        {
            Title = title,
            Key = key,
            CoinIndex = coinIndex,
            Indicator = active ? (_sortDescending ? "▼" : "▲") : string.Empty,
        };
    }

    /// <summary>
    /// Subtarget names per coin for the damage-by-target table: a row there is an enemy,
    /// so what each coin calls the target at each position matters.
    /// </summary>
    private IReadOnlyList<IReadOnlyList<string>> SubtargetTitles()
    {
        List<IReadOnlyList<string>> titles = [];

        foreach (CoinViewModel coin in Coins)
        {
            titles.Add([.. coin.Subtargets.Select(subtarget => subtarget.Name)]);
        }

        return titles;
    }

    private ResistanceViewModel[] CreateResistances(params Element[] elements)
    {
        ResistanceViewModel[] created = new ResistanceViewModel[elements.Length];

        for (int i = 0; i < elements.Length; i++)
        {
            ResistanceViewModel resistance = new() { Option = ElementOptions.For(elements[i]) };
            resistance.PropertyChanged += (_, _) => Recalculate();
            created[i] = resistance;
            _allResistances.Add(resistance);
        }

        return created;
    }

    private CoinBonusViewModel CreateBonusValue(BonusRowViewModel row, double value)
    {
        CoinBonusViewModel bonus = new() { Row = row, Value = value };
        bonus.PropertyChanged += OnBonusValuePropertyChanged;
        return bonus;
    }

    private void OnBonusValuePropertyChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

    private void OnBonusRowPropertyChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

    private void OnCoinsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (CoinViewModel coin in e.OldItems?.OfType<CoinViewModel>() ?? [])
        {
            coin.PropertyChanged -= OnCoinPropertyChanged;

            foreach (CoinBonusViewModel bonus in coin.Bonuses)
            {
                bonus.PropertyChanged -= OnBonusValuePropertyChanged;
            }
        }

        foreach (CoinViewModel coin in e.NewItems?.OfType<CoinViewModel>() ?? [])
        {
            coin.PropertyChanged += OnCoinPropertyChanged;
        }

        RenumberCoins();
        Recalculate();
    }

    private void OnCoinPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Recalculate only on user edits: Recalculate itself sets the result properties,
        // and reacting to them would loop the calculation.
        if (e.PropertyName is null || !CoinViewModel.InputPropertyNames.Contains(e.PropertyName))
        {
            return;
        }

        if (e.PropertyName == nameof(CoinViewModel.Weight) && sender is CoinViewModel changed)
        {
            SyncSubtargets(changed);
        }

        Recalculate();
    }

    /// <summary>
    /// Brings a coin's extra targets in line with its weight.
    /// New targets start with the main target's resistances and can be edited afterwards.
    /// </summary>
    private void SyncSubtargets(CoinViewModel coin)
    {
        int needed = Math.Max(0, coin.Weight - 1);

        while (coin.Subtargets.Count > needed)
        {
            // Unsubscribe only the subtarget's own properties: the shared part outlives the
            // coin and goes to the next one, so the enemy's settings aren't lost.
            coin.Subtargets[^1].PropertyChanged -= OnResistanceChanged;
            coin.Subtargets.RemoveAt(coin.Subtargets.Count - 1);
        }

        while (coin.Subtargets.Count < needed)
        {
            coin.Subtargets.Add(CreateSubtarget(coin, coin.Subtargets.Count + 2));
        }
    }

    /// <summary>
    /// Moves a subtarget to a new coin. The name is copied first: it puts the subtarget into
    /// the same enemy group, and the group brings resistances and the moratorium along.
    /// </summary>
    private static void CopySubtarget(SubtargetViewModel from, SubtargetViewModel to)
    {
        to.Name = from.Name;
        to.ModDynPercent = from.ModDynPercent;
        to.OffenseDefenseDiff = from.OffenseDefenseDiff;
        to.HasCrit = from.HasCrit;
        to.CritPercent = from.CritPercent;
    }

    /// <summary>A new subtarget copies the main target: both the resistances and the coin's modifiers.</summary>
    private SubtargetViewModel CreateSubtarget(CoinViewModel coin, int number)
    {
        // By default everything matches this coin's main target; a reset returns to these values.
        MainTargetParameters defaults = MainParametersOf(coin);

        SubtargetViewModel subtarget = new()
        {
            Number = number,
            SharedFor = ResolveShared,
            Shared = SharedTargetFor($"Subtarget {number}", null),
            Name = $"Subtarget {number}",
            MainResistance = MainResistance,
            MainParameters = () => MainParametersOf(coin),

            ModDynPercent = defaults.ModDynPercent,
            OffenseDefenseDiff = defaults.OffenseDefenseDiff,
            HasCrit = defaults.HasCrit,
            CritPercent = defaults.CritPercent,
        };

        subtarget.PropertyChanged += OnResistanceChanged;
        return subtarget;
    }

    /// <summary>
    /// Where a subtarget goes after being renamed. If a group with that name exists, it joins
    /// it and takes its resistances and moratorium right away. If the name is new: when nobody
    /// else holds the current group, the whole group moves under the new name; otherwise the
    /// subtarget splits off with a copy — the neighbors' settings aren't its own.
    /// </summary>
    private SharedTargetViewModel ResolveShared(SubtargetViewModel subtarget)
    {
        string key = subtarget.Name.Trim();
        SharedTargetViewModel current = subtarget.Shared;

        if (_sharedTargets.TryGetValue(key, out SharedTargetViewModel? existing))
        {
            if (!ReferenceEquals(existing, current) && IsSoleOwner(subtarget, current))
            {
                Forget(current);
            }

            return existing;
        }

        if (!IsSoleOwner(subtarget, current))
        {
            return SharedTargetFor(key, current);
        }

        // The same group under a different key: the entered values stay and the old name is
        // freed — otherwise typing letter by letter would leave stubs behind.
        Forget(current);
        _sharedTargets.Add(key, current);
        return current;
    }

    /// <summary>Removes a name that no subtarget uses any more.</summary>
    private void Forget(SharedTargetViewModel shared)
    {
        foreach (KeyValuePair<string, SharedTargetViewModel> pair in _sharedTargets)
        {
            if (ReferenceEquals(pair.Value, shared))
            {
                _sharedTargets.Remove(pair.Key);
                return;
            }
        }
    }

    /// <summary>Whether anyone besides this subtarget holds the group.</summary>
    private bool IsSoleOwner(SubtargetViewModel subtarget, SharedTargetViewModel shared)
    {
        foreach (CoinViewModel coin in Coins)
        {
            foreach (SubtargetViewModel other in coin.Subtargets)
            {
                if (!ReferenceEquals(other, subtarget) && ReferenceEquals(other.Shared, shared))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The shared part of the subtarget with this name: created once and given to every coin
    /// where the name is entered. So editing resistances or the moratorium on one coin shows
    /// on the others, while subtargets with different names don't affect each other.
    /// Lowering the weight doesn't drop the group: restore the weight and the enemy's settings are back.
    /// </summary>
    /// <param name="template">
    /// Where to take the values if the group doesn't exist yet. On rename it's the subtarget's
    /// previous group: the name changed, but the entered resistances shouldn't be lost.
    /// </param>
    private SharedTargetViewModel SharedTargetFor(string name, SharedTargetViewModel? template)
    {
        string key = name.Trim();

        if (_sharedTargets.TryGetValue(key, out SharedTargetViewModel? existing))
        {
            return existing;
        }

        List<ResistanceViewModel> values = [];

        foreach (ElementOption option in ElementOptions.ResistanceOrder)
        {
            ResistanceViewModel resistance = new()
            {
                Option = option,
                Value = template is null
                    ? MainResistance(option.Element)
                    : template.Resistances[values.Count].Value,
            };

            resistance.PropertyChanged += OnResistanceChanged;
            values.Add(resistance);
        }

        SharedTargetViewModel shared = new()
        {
            Resistances = values,
            TimeMoratorium = template?.TimeMoratorium ?? TimeMoratorium,
            TimeMoratoriumStacks = template?.TimeMoratoriumStacks ?? TimeMoratoriumStacks,
        };

        shared.PropertyChanged += OnResistanceChanged;
        _sharedTargets.Add(key, shared);
        return shared;
    }

    /// <summary>
    /// The main target's current values. Read on every call rather than cached: a reset must
    /// pick up what's set now, not what was set when the subtarget was created.
    /// </summary>
    private MainTargetParameters MainParametersOf(CoinViewModel coin) => new(
        coin.ModDynPercent,
        coin.OffenseDefenseDiff,
        coin.HasCrit,
        coin.CritPercent,
        TimeMoratorium,
        TimeMoratoriumStacks);

    private double MainResistance(Element element)
    {
        foreach (ResistanceViewModel resistance in _allResistances)
        {
            if (resistance.Option.Element == element)
            {
                return resistance.Value;
            }
        }

        return 1.0;
    }

    private void OnResistanceChanged(object? sender, PropertyChangedEventArgs e) => Recalculate();

    private void RenumberCoins()
    {
        for (int i = 0; i < Coins.Count; i++)
        {
            Coins[i].Number = i + 1;
        }
    }
}
