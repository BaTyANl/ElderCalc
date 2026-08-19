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
    /// Общие части подцелей по названию врага. Регистр не важен, пробелы по краям тоже:
    /// «Boss» и «boss » — один и тот же враг.
    /// </summary>
    private readonly Dictionary<string, SharedTargetViewModel> _sharedTargets =
        new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<CoinViewModel> Coins { get; } = [];

    /// <summary>Справочник личностей — вкладка ID. В расчёте пока не участвует.</summary>
    public TableViewModel IdTable { get; } = TableViewModel.CreateIdTable();

    /// <summary>Справочник E.G.O. — одноимённая вкладка.</summary>
    public TableViewModel EgoTable { get; } = TableViewModel.CreateEgoTable();

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

    /// <summary>Число клэшей — общее для всех монет скилла, каждый даёт 3% к Mod stat.</summary>
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

    /// <summary>Time Moratorium включён: урон растёт за стаки и становится sloth-уроном.</summary>
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

    /// <summary>Число стаков; допустимы только 1 и 2, всё прочее подтягивается к границе.</summary>
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
                // Значение уже на границе, но ввели за её пределами — вернём поле к границе.
                OnPropertyChanged();
            }
        }
    }

    public double Total
    {
        get => _total;
        private set => SetProperty(ref _total, value);
    }

    /// <summary>Итог без Time Moratorium — левая часть равенства в строке итога.</summary>
    public double TotalBase
    {
        get => _totalBase;
        private set => SetProperty(ref _totalBase, value);
    }

    /// <summary>
    /// Красное число в строке итога. Когда показываем равенство, это его левая часть,
    /// то есть урон до моратория. Когда равенства нет — сразу настоящий итог,
    /// иначе прибавка моратория потерялась бы с экрана.
    /// </summary>
    public double TotalLeading
    {
        get => _totalLeading;
        private set => SetProperty(ref _totalLeading, value);
    }

    /// <summary>
    /// Множитель Time Moratorium: прибавка за стаки на сопротивление основной цели к sloth.
    /// Берётся по формуле, а не как отношение итогов — иначе округление урона вниз
    /// искажало бы его на мелких числах (5 превращается в 11, и выходит 2.2 вместо 2.3).
    /// Если у основной цели моратория нет, а у подцелей есть, единого множителя из
    /// формулы не существует — тогда показываем фактическое отношение итогов.
    /// </summary>
    public double MoratoriumBuff
    {
        get => _moratoriumBuff;
        private set => SetProperty(ref _moratoriumBuff, value);
    }

    /// <summary>
    /// Показывать ли равенство в строке итога. Опираемся на то, что мораторий
    /// действительно изменил урон, а не только на общий флажок: он может быть выключен,
    /// а у подцели включён.
    /// </summary>
    public bool ShowMoratoriumEquation
    {
        get => _showMoratoriumEquation;
        private set => SetProperty(ref _showMoratoriumEquation, value);
    }

    /// <summary>Распределение урона по целям — таблица в отдельном окне.</summary>
    public IReadOnlyList<TargetDamageRow> DamageByTarget
    {
        get => _damageByTarget;
        private set => SetProperty(ref _damageByTarget, value);
    }

    /// <summary>Столбцы монет в таблице распределения — они же кнопки сортировки.</summary>
    public IReadOnlyList<TargetColumnViewModel> CoinColumns
    {
        get => _coinColumns;
        private set => SetProperty(ref _coinColumns, value);
    }

    /// <summary>Столбец с названиями целей: сортирует по алфавиту.</summary>
    public TargetColumnViewModel TitleColumn
    {
        get => _titleColumn;
        private set => SetProperty(ref _titleColumn, value);
    }

    /// <summary>Столбец итога по цели: сортирует по урону.</summary>
    public TargetColumnViewModel TotalColumn
    {
        get => _totalColumn;
        private set => SetProperty(ref _totalColumn, value);
    }

    /// <summary>
    /// Отсортировать таблицу распределения по этому столбцу. Тот же столбец второй раз
    /// переворачивает порядок. Урон начинаем с большего — интересен обычно он,
    /// а названия с начала алфавита.
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

    /// <summary>Есть ли вообще что распределять: хоть у одной монеты больше одной цели.</summary>
    public bool HasMultipleTargets
    {
        get => _hasMultipleTargets;
        private set => SetProperty(ref _hasMultipleTargets, value);
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

        // Вес скопирован, поэтому подцелей столько же; переносим и их настройки —
        // у монет одного скилла цели, как правило, те же самые.
        SyncSubtargets(coin);

        // Монета в списке до переноса названий: по списку считается, держит ли группу
        // кто-то ещё, и новая монета должна попасть в этот подсчёт.
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

        // Сопротивления подцелей уже перенесены внутри ToModel каждой монеты.

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

        // При нескольких целях разбор моратория живёт в окне распределения,
        // а в главном окне остаётся просто итог. При одной цели показать его негде.
        ShowMoratoriumEquation = result.Total != result.TotalBase && !HasMultipleTargets;
        TotalLeading = ShowMoratoriumEquation ? result.TotalBase : result.Total;

        MoratoriumBuff = TimeMoratorium
            ? (1.0 + (DamageCalculator.TimeMoratoriumPerStack * TimeMoratoriumStacks))
                * MainResistance(Element.Sloth)
            : result.TotalBase != 0.0 ? result.Total / result.TotalBase : 1.0;
    }

    /// <summary>Пересобирает заголовки таблицы: подписи монет и стрелку сортировки.</summary>
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
    /// Названия подцелей по монетам для таблицы распределения: строка там — это враг,
    /// поэтому важно, как именно каждая монета зовёт цель с каждой позиции.
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
            // Отписываем только собственные свойства подцели: общая часть переживает
            // монету и достаётся следующей, чтобы настройки врага не терялись.
            coin.Subtargets[^1].PropertyChanged -= OnResistanceChanged;
            coin.Subtargets.RemoveAt(coin.Subtargets.Count - 1);
        }

        while (coin.Subtargets.Count < needed)
        {
            coin.Subtargets.Add(CreateSubtarget(coin, coin.Subtargets.Count + 2));
        }
    }

    /// <summary>
    /// Переносит подцель на новую монету. Название копируем первым: по нему подцель
    /// попадает в ту же группу врага, а с группой приезжают сопротивления и мораторий.
    /// </summary>
    private static void CopySubtarget(SubtargetViewModel from, SubtargetViewModel to)
    {
        to.Name = from.Name;
        to.ModDynPercent = from.ModDynPercent;
        to.OffenseDefenseDiff = from.OffenseDefenseDiff;
        to.HasCrit = from.HasCrit;
        to.CritPercent = from.CritPercent;
    }

    /// <summary>Новая подцель повторяет основную: и сопротивления, и модификаторы монеты.</summary>
    private SubtargetViewModel CreateSubtarget(CoinViewModel coin, int number)
    {
        // По умолчанию — всё как у основной цели этой монеты; к ним же возвращает сброс.
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
    /// Куда переехать подцели после переименования. Есть группа с таким названием —
    /// подключаемся к ней, и сопротивления с мораторием подтягиваются оттуда сразу же.
    /// Названия ещё нет: если группу больше никто не держит, переносим её под новое
    /// название целиком, иначе отделяемся от соседей копией — их настройки не наши.
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

        // Ту же группу под другим ключом: набранные значения остаются, а старое
        // название освобождается — иначе при наборе по букве копились бы огрызки.
        Forget(current);
        _sharedTargets.Add(key, current);
        return current;
    }

    /// <summary>Убирает название, за которым больше не стоит ни одна подцель.</summary>
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

    /// <summary>Держит ли эту группу кто-то ещё, кроме самой подцели.</summary>
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
    /// Общая часть подцели с этим названием: заводится один раз и достаётся всем монетам,
    /// где такое название вписано. Поэтому правка сопротивлений или моратория у одной
    /// монеты видна у остальных, а подцели с разными названиями друг друга не задевают.
    /// При уменьшении веса группу не выбрасываем: вернёшь вес — настройки врага на месте.
    /// </summary>
    /// <param name="template">
    /// Откуда взять значения, если группы ещё нет. При переименовании это прежняя группа
    /// подцели: имя поменяли, а набранные сопротивления терять незачем.
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
    /// Текущие значения основной цели. Читаем их при каждом обращении, а не запоминаем:
    /// сброс должен подтягивать то, что стоит сейчас, а не то, что было при создании.
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
