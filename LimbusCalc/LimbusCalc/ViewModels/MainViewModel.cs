using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>Состояние главного окна: общие параметры, список монет и итог.</summary>
public sealed class MainViewModel : ObservableObject
{
    private double _baseRoll;
    private double _passiveModDynPercent;
    private ElementOption _skillType = ElementOptions.DamageTypes[0];
    private ElementOption _skillSin = ElementOptions.Sins[0];
    private double _total;
    private string _totalTooltip = string.Empty;

    private readonly List<ResistanceViewModel> _allResistances = [];

    public ObservableCollection<CoinViewModel> Coins { get; } = [];

    /// <summary>Строки бонусов: вид и цель общие для всех монет, значения — свои у каждой.</summary>
    public ObservableCollection<BonusRowViewModel> BonusRows { get; } = [];

    /// <summary>Сопротивления к типам урона — три в ряд.</summary>
    public IReadOnlyList<ResistanceViewModel> TypeResistances { get; }

    /// <summary>Верхний ряд сопротивлений к грехам: три штуки.</summary>
    public IReadOnlyList<ResistanceViewModel> SinResistancesTop { get; }

    /// <summary>Нижний ряд сопротивлений к грехам: четыре штуки.</summary>
    public IReadOnlyList<ResistanceViewModel> SinResistancesBottom { get; }

    public MainViewModel()
    {
        // Порядок как в игре: типы slash-pierce-blunt, грехи трапецией 3 + 4.
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

    /// <summary>"Base roll": бросок до прибавок монет.</summary>
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

    /// <summary>Надбавка к Dyn mod в процентах, общая для всех монет.</summary>
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

    /// <summary>Тип урона навыка. В расчёте пока не участвует — ждёт сопротивлений.</summary>
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

    /// <summary>Грех навыка. В расчёте пока не участвует — ждёт сопротивлений.</summary>
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

    public double Total
    {
        get => _total;
        private set => SetProperty(ref _total, value);
    }

    /// <summary>Разбивка итога по целям — показывается подсказкой над итоговым числом.</summary>
    public string TotalTooltip
    {
        get => _totalTooltip;
        private set => SetProperty(ref _totalTooltip, value);
    }

    /// <summary>
    /// Новая монета повторяет предыдущую: у монет одного скилла параметры обычно совпадают,
    /// и перенабирать их заново незачем. Первая монета создаётся с нулями.
    /// </summary>
    public void AddCoin()
    {
        CoinViewModel? source = Coins.Count > 0 ? Coins[^1] : null;
        CoinViewModel coin = source?.Clone() ?? new CoinViewModel();

        // Строки бонусов общие, поэтому у новой монеты должно быть столько же значений.
        for (int i = 0; i < BonusRows.Count; i++)
        {
            double value = source is not null && i < source.Bonuses.Count ? source.Bonuses[i].Value : 0.0;
            coin.Bonuses.Add(CreateBonusValue(BonusRows[i], value));
        }

        // Вес скопирован, поэтому подцелей столько же; переносим и их сопротивления —
        // у монет одного скилла цели, как правило, те же самые.
        SyncSubtargets(coin);

        if (source is not null)
        {
            int shared = Math.Min(coin.Subtargets.Count, source.Subtargets.Count);

            for (int i = 0; i < shared; i++)
            {
                CopyResistances(source.Subtargets[i], coin.Subtargets[i]);
            }
        }

        Coins.Add(coin);
    }

    public void RemoveLastCoin()
    {
        if (Coins.Count > 0)
        {
            Coins.RemoveAt(Coins.Count - 1);
        }
    }

    /// <summary>Добавляет строку бонуса и по нулевому значению каждой монете.</summary>
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
            skill.Coins.Add(coin.ToModel(PassiveModDynPercent));
        }

        DamageInput input = new();
        input.Skills.Add(skill);

        foreach (ResistanceViewModel resistance in _allResistances)
        {
            input.Resistances[resistance.Option.Element] = resistance.Value;
        }

        // Сопротивления подцелей уже перенесены внутри ToModel каждой монеты.

        DamageResult result = DamageCalculator.Calculate(input);

        for (int i = 0; i < Coins.Count; i++)
        {
            Coins[i].ApplyResult(result.Coins[i]);
        }

        Total = result.Total;
        TotalTooltip = TargetDamageText.Format(result.TotalByTarget);
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
        // Пересчитываем только по правкам пользователя: свойства с результатами
        // меняем мы сами внутри Recalculate, и реакция на них зациклила бы расчёт.
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
    /// Приводит список дополнительных целей монеты в согласие с её весом.
    /// Новые цели заводятся с сопротивлениями основной — их потом можно поправить.
    /// </summary>
    private void SyncSubtargets(CoinViewModel coin)
    {
        int needed = Math.Max(0, coin.Weight - 1);

        while (coin.Subtargets.Count > needed)
        {
            SubtargetViewModel removed = coin.Subtargets[^1];

            foreach (ResistanceViewModel resistance in removed.Resistances)
            {
                resistance.PropertyChanged -= OnResistanceChanged;
            }

            coin.Subtargets.RemoveAt(coin.Subtargets.Count - 1);
        }

        while (coin.Subtargets.Count < needed)
        {
            coin.Subtargets.Add(CreateSubtarget(coin.Subtargets.Count + 2));
        }
    }

    /// <summary>Списки сопротивлений идут в одном порядке, поэтому переносим по позициям.</summary>
    private static void CopyResistances(SubtargetViewModel from, SubtargetViewModel to)
    {
        int shared = Math.Min(from.Resistances.Count, to.Resistances.Count);

        for (int i = 0; i < shared; i++)
        {
            to.Resistances[i].Value = from.Resistances[i].Value;
        }
    }

    private SubtargetViewModel CreateSubtarget(int number)
    {
        List<ResistanceViewModel> values = [];

        foreach (ElementOption option in ElementOptions.ResistanceOrder)
        {
            ResistanceViewModel resistance = new()
            {
                Option = option,
                Value = MainResistance(option.Element),
            };

            resistance.PropertyChanged += OnResistanceChanged;
            values.Add(resistance);
        }

        return new SubtargetViewModel
        {
            Number = number,
            Resistances = values,
            MainResistance = MainResistance,
        };
    }

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
