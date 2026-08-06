namespace LimbusCalc.Calculation;

/// <summary>
/// Одна монета скилла. Соответствует одной колонке (D..M) на листе "Формула".
/// </summary>
public sealed class Coin
{
    /// <summary>
    /// Монета выпала орлом. Решка не добавляет силу к броску, но урон монета всё равно наносит —
    /// её бросок равен броску предыдущей монеты (для первой монеты — базовому).
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>"Power" (строка 3): прибавка к броску за эту монету, если выпал орёл.</summary>
    public double Power { get; set; }

    /// <summary>
    /// "Mod dyn" (строка 4): динамические множители — баффы, сродство и т.п.
    /// Хранится множителем: 1.63 соответствует +63%. В интерфейс выводится процентами.
    /// </summary>
    public double ModDyn { get; set; } = 1.0;

    /// <summary>"O-D diff" (строка 5): разница уровня атаки и защиты.</summary>
    public double OffenseDefenseDiff { get; set; }

    /// <summary>
    /// Бонусы монеты. Значения одного вида складываются: проценты суммируются
    /// и округляются вниз один раз, единицы просто прибавляются.
    /// </summary>
    public List<CoinBonus> Bonuses { get; } = [];

    /// <summary>По этой монете прошёл крит. Если нет, <see cref="Crit"/> в расчёт не идёт.</summary>
    public bool HasCrit { get; set; }

    /// <summary>"Crit": прибавка к Mod stat долей единицы (0.2 = +20%). У каждой монеты своя.</summary>
    public double Crit { get; set; }

    /// <summary>"Clash count": число клэшей; каждый добавляет 3% к Mod stat.</summary>
    public int ClashCount { get; set; }

    /// <summary>"Weight": по скольким целям бьёт монета. Только целое.</summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// Сопротивления дополнительных целей, начиная со второй. Чего в списке нет —
    /// считается по основной цели.
    /// </summary>
    public List<ResistanceSet> SubtargetResistances { get; } = [];
}

/// <summary>
/// Скилл — набор монет с общим базовым броском.
/// На листе колонки D..H это первый скилл, I..M — второй (бросок там считается заново от базы).
/// </summary>
public sealed class Skill
{
    public string Name { get; set; } = string.Empty;

    /// <summary>"Base roll" (Q2): бросок до прибавок монет.</summary>
    public double BaseRoll { get; set; }

    /// <summary>Тип урона навыка: slash, blunt или pierce.</summary>
    public Element Type { get; set; } = Element.Slash;

    /// <summary>Грех навыка.</summary>
    public Element Sin { get; set; } = Element.Wrath;

    public List<Coin> Coins { get; } = [];
}

/// <summary>Разбор урона по одной монете — для вывода таблицей в интерфейсе.</summary>
/// <param name="Damage">Итог монеты, уже округлённый вниз до целого.</param>
/// <param name="TargetDamage">
/// Вклад каждой цели до общего округления: нулевая — основная, дальше подцели.
/// </param>
public readonly record struct CoinBreakdown(
    int SkillIndex,
    int CoinIndex,
    double Roll,
    double ModStat,
    double Damage,
    IReadOnlyList<double> TargetDamage);

public sealed class DamageResult
{
    public required IReadOnlyList<CoinBreakdown> Coins { get; init; }

    /// <summary>Сумма урона по всем монетам; вес каждой уже учтён в её уроне.</summary>
    public required double Total { get; init; }

    /// <summary>Сколько всего пришлось по каждой цели: нулевая — основная, дальше подцели.</summary>
    public required IReadOnlyList<double> TotalByTarget { get; init; }
}

/// <summary>Входные данные расчёта целиком.</summary>
public sealed class DamageInput
{
    public List<Skill> Skills { get; } = [];

    /// <summary>Сопротивления основной цели.</summary>
    public ResistanceSet Resistances { get; } = new();
}

public static class DamageCalculator
{
    /// <summary>Константа из знаменателя ModStat.</summary>
    public const double LevelDiffConstant = 25.0;

    /// <summary>Урон монеты, у которой бросок не набрался: она всё равно бьёт на единицу.</summary>
    public const double MinimumDamage = 1.0;

    /// <summary>Вклад одного клэша в Mod stat.</summary>
    public const double ClashCountModifier = 0.03;

    /// <summary>
    /// Допуск при округлении вниз. Дроби вроде 0.03 в double хранятся неточно, и там,
    /// где точная арифметика даёт 115, получается 114.99999999999999 — без допуска
    /// округление вниз теряло бы на этом целую единицу урона.
    /// </summary>
    private const double FloorTolerance = 1e-9;

    private static double FloorWithTolerance(double value) => Math.Floor(value + FloorTolerance);

    /// <summary>
    /// Вклад одного сопротивления в Mod stat. Выше единицы бонус идёт целиком,
    /// ниже единицы штраф вдвое мягче: 0.6 даёт не -0.4, а -0.2.
    /// </summary>
    public static double ResistanceModifier(double resistance) => resistance switch
    {
        > 1.0 => resistance - 1.0,
        < 1.0 => -(1.0 - resistance) / 2.0,
        _ => 0.0,
    };

    /// <summary>
    /// "Mod stat" (строка 6): 1 + crit + diff / (|diff| + 25) + 3% за каждый клэш
    /// + поправка на сопротивления цели к типу урона и греху навыка.
    /// В damage.xlsx знаменатель записан как |diff + 25|, но эталонный лист MyCalculator
    /// из общей таблицы использует |diff| + 25. При diff > 0 обе записи совпадают,
    /// при diff < 0 вариант с листа даёт неадекватно большой штраф, поэтому взят эталонный.
    /// </summary>
    public static double ModStat(
        double offenseDefenseDiff,
        double crit,
        int clashCount = 0,
        double resistanceModifier = 0.0)
    {
        double levelDiffModifier =
            offenseDefenseDiff / (Math.Abs(offenseDefenseDiff) + LevelDiffConstant);

        return 1.0 + crit + levelDiffModifier + (ClashCountModifier * clashCount) + resistanceModifier;
    }

    /// <summary>
    /// Сопротивления цели с номером <paramref name="targetIndex"/> (0 — основная).
    /// Для дополнительных целей без своих настроек берутся сопротивления основной.
    /// </summary>
    private static ResistanceSet TargetResistances(DamageInput input, Coin coin, int targetIndex) =>
        targetIndex >= 1 && targetIndex - 1 < coin.SubtargetResistances.Count
            ? coin.SubtargetResistances[targetIndex - 1]
            : input.Resistances;

    public static DamageResult Calculate(DamageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<CoinBreakdown> breakdown = [];
        List<double> totalByTarget = [];
        double sum = 0.0;

        for (int s = 0; s < input.Skills.Count; s++)
        {
            Skill skill = input.Skills[s];

            // Бросок накапливается по монетам скилла: орёл добавляет силу монеты,
            // решка оставляет бросок предыдущей монеты без изменений.
            double previousRoll = skill.BaseRoll;

            for (int c = 0; c < skill.Coins.Count; c++)
            {
                Coin coin = skill.Coins[c];

                double roll = previousRoll + (coin.Active ? coin.Power : 0.0);

                // Вес — это число целей. У каждой свои сопротивления, поэтому урон
                // считается по каждой отдельно и складывается. При одинаковых
                // сопротивлениях итог совпадает с прежним умножением на вес.
                double coinDamage = 0.0;
                List<double> targetDamage = [];

                for (int t = 0; t < coin.Weight; t++)
                {
                    ResistanceSet resistances = TargetResistances(input, coin, t);

                    double targetModStat = ModStat(
                        coin.OffenseDefenseDiff,
                        coin.HasCrit ? coin.Crit : 0.0,
                        coin.ClashCount,
                        ResistanceModifier(resistances[skill.Type])
                            + ResistanceModifier(resistances[skill.Sin]));

                    // Ненабранный бросок не обнуляет монету: вместо расчёта берётся единица.
                    double core = roll <= 0.0
                        ? MinimumDamage
                        : roll * coin.ModDyn * targetModStat;

                    double flatBonus = 0.0;
                    double percentBonus = 0.0;

                    foreach (CoinBonus bonus in coin.Bonuses)
                    {
                        // Бонус масштабируется сопротивлением к своей цели, а не к типу навыка:
                        // flat 10 по цели с сопротивлением 0.5 даёт 5, с 2.0 — двадцать.
                        double scaled = bonus.Value * resistances[bonus.Target];

                        if (bonus.Kind == BonusKind.Flat)
                        {
                            flatBonus += scaled;
                        }
                        else
                        {
                            percentBonus += scaled;
                        }
                    }

                    // Процентный бонус — прибавка, а не множитель: доля от основы, вниз.
                    double perTarget = FloorWithTolerance(core)
                        + FloorWithTolerance(core * percentBonus / 100.0)
                        + flatBonus;

                    coinDamage += perTarget;
                    targetDamage.Add(perTarget);

                    while (totalByTarget.Count <= t)
                    {
                        totalByTarget.Add(0.0);
                    }

                    totalByTarget[t] += perTarget;
                }

                double damage = FloorWithTolerance(coinDamage);

                // В таблице показываем Mod stat по основной цели.
                double modStat = ModStat(
                    coin.OffenseDefenseDiff,
                    coin.HasCrit ? coin.Crit : 0.0,
                    coin.ClashCount,
                    ResistanceModifier(input.Resistances[skill.Type])
                        + ResistanceModifier(input.Resistances[skill.Sin]));

                breakdown.Add(new CoinBreakdown(s, c, roll, modStat, damage, targetDamage));
                sum += damage;

                previousRoll = roll;
            }
        }

        return new DamageResult
        {
            Coins = breakdown,
            Total = sum,
            TotalByTarget = totalByTarget,
        };
    }
}
