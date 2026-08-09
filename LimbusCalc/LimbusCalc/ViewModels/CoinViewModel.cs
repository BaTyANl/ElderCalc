using System.Collections.ObjectModel;
using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>Колонка монеты: сверху поля ввода, под чертой — посчитанные значения.</summary>
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
    private string _damageTooltip = string.Empty;

    /// <summary>Имена свойств, изменение которых требует пересчёта.</summary>
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

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    /// <summary>Монета выпала орлом и участвует в броске.</summary>
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
    /// Динамический модификатор в процентах: 63 означает +63%, то есть множитель 1.63.
    /// Отрицательное значение уменьшает урон: -37 даёт множитель 0.63.
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
    /// Значения бонусов этой монеты. Порядок соответствует строкам бонусов в таблице,
    /// список синхронизирует <see cref="MainViewModel"/>.
    /// </summary>
    public ObservableCollection<CoinBonusViewModel> Bonuses { get; } = [];

    /// <summary>По этой монете прошёл крит.</summary>
    public bool HasCrit
    {
        get => _hasCrit;
        set => SetProperty(ref _hasCrit, value);
    }

    /// <summary>Крит-модификатор этой монеты в процентах: 20 означает +20%.</summary>
    public double CritPercent
    {
        get => _critPercent;
        set => SetProperty(ref _critPercent, value);
    }

    /// <summary>Вес этой монеты — по скольким целям она бьёт. Только целое.</summary>
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

    /// <summary>Есть ли дополнительные цели: со второй и дальше.</summary>
    public bool HasSubtargets => Weight >= 2;

    /// <summary>
    /// Дополнительные цели этой монеты, начиная со второй. Список держит в согласии
    /// с весом <see cref="MainViewModel"/>.
    /// </summary>
    public ObservableCollection<SubtargetViewModel> Subtargets { get; } = [];

    /// <summary>Подписи столбцов в окне подцелей — те же элементы и в том же порядке.</summary>
    public IReadOnlyList<ElementOption> ResistanceHeaders => ElementOptions.ResistanceOrder;

    public double Roll
    {
        get => _roll;
        private set => SetProperty(ref _roll, value);
    }

    public double ModStat
    {
        get => _modStat;
        private set => SetProperty(ref _modStat, value);
    }

    public double Damage
    {
        get => _damage;
        private set => SetProperty(ref _damage, value);
    }

    /// <summary>
    /// Копия всех введённых значений. Номер не переносится: его назначает список монет.
    /// Посчитанные Roll/ModStat/Damage тоже не копируем — они появятся при пересчёте.
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
    /// Общая для всех монет надбавка к Dyn mod в процентах; складывается с собственной.
    /// </param>
    /// <param name="clashCount">Число клэшей — оно общее для всех монет скилла.</param>
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
            coin.SubtargetResistances.Add(subtarget.ToModel());
        }

        return coin;
    }

    /// <summary>Разбивка урона по целям — показывается подсказкой над числом урона.</summary>
    public string DamageTooltip
    {
        get => _damageTooltip;
        private set => SetProperty(ref _damageTooltip, value);
    }

    public void ApplyResult(CoinBreakdown breakdown)
    {
        Roll = breakdown.Roll;
        ModStat = breakdown.ModStat;
        // В таблице показываем урон без Time Moratorium: его прибавка учтена только в итоге.
        Damage = breakdown.BaseDamage;
        DamageTooltip = TargetDamageText.Format(breakdown.TargetDamage);
    }
}
