namespace LimbusCalc.Calculation;

/// <summary>
/// One coin of a skill. Matches one column (D..M) on the "Формула" (Formula) sheet of damage.xlsx.
/// </summary>
public sealed class Coin
{
    /// <summary>
    /// The coin landed heads. Tails adds no power to the roll, but the coin still deals
    /// damage: its roll equals the previous coin's roll (the base roll for the first coin).
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>"Power" (row 3): added to the roll when this coin lands heads.</summary>
    public double Power { get; set; }

    /// <summary>
    /// "Mod dyn" (row 4): dynamic multipliers such as buffs and affinity.
    /// Stored as a multiplier: 1.63 means +63%. The UI shows it as a percentage.
    /// </summary>
    public double ModDyn { get; set; } = 1.0;

    /// <summary>"O-D diff" (row 5): offense level minus defense level.</summary>
    public double OffenseDefenseDiff { get; set; }

    /// <summary>
    /// Coin bonuses. Bonuses of the same kind stack: percentages are summed and floored
    /// once, flat values are simply added.
    /// </summary>
    public List<CoinBonus> Bonuses { get; } = [];

    /// <summary>The coin crit. When false, <see cref="Crit"/> is ignored.</summary>
    public bool HasCrit { get; set; }

    /// <summary>"Crit": added to Mod stat as a fraction (0.2 = +20%). Set per coin.</summary>
    public double Crit { get; set; }

    /// <summary>"Clash count": each clash adds 3% to Mod stat.</summary>
    public int ClashCount { get; set; }

    /// <summary>"Weight": how many targets the coin hits. Whole numbers only.</summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// Extra targets starting from the second one, each with its own resistances and
    /// parameters. Targets missing from the list use the main target's values.
    /// </summary>
    public List<SubtargetOverride> Subtargets { get; } = [];
}

/// <summary>
/// An extra target of a coin with its own resistances and modifiers. It starts as a copy
/// of the main target and is edited independently afterwards.
/// </summary>
public sealed class SubtargetOverride
{
    public ResistanceSet Resistances { get; } = new();

    /// <summary>"Mod dyn" of this target as a multiplier: 1.63 means +63%.</summary>
    public double ModDyn { get; set; } = 1.0;

    public double OffenseDefenseDiff { get; set; }

    public bool HasCrit { get; set; }

    /// <summary>Crit modifier as a fraction (0.2 = +20%).</summary>
    public double Crit { get; set; }

    public bool TimeMoratorium { get; set; }

    public int TimeMoratoriumStacks { get; set; } = 1;
}

/// <summary>
/// A skill: coins that share one base roll. On the sheet, columns D..H hold the first
/// skill and I..M the second one (whose roll starts from the base again).
/// </summary>
public sealed class Skill
{
    public string Name { get; set; } = string.Empty;

    /// <summary>"Base roll" (Q2): the roll before any coin power is added.</summary>
    public double BaseRoll { get; set; }

    /// <summary>Damage type of the skill: Slash, Blunt or Pierce.</summary>
    public Element Type { get; set; } = Element.Slash;

    /// <summary>Sin of the skill.</summary>
    public Element Sin { get; set; } = Element.Wrath;

    public List<Coin> Coins { get; } = [];
}

/// <summary>Damage of one coin, broken down for the result table in the UI.</summary>
/// <param name="Damage">The coin's total including Time Moratorium, floored.</param>
/// <param name="BaseDamage">The same total without Time Moratorium; this is what the table shows.</param>
/// <param name="TargetDamage">
/// Each target's share without Time Moratorium: index 0 is the main target, then subtargets.
/// Same meaning as <paramref name="BaseDamage"/>.
/// </param>
/// <param name="TargetDamageFinal">The same shares with each target's Time Moratorium applied.</param>
/// <param name="TargetMoratoriumBuff">
/// The Time Moratorium multiplier of each target as the formula defines it; 1 means none.
/// The breakdown shows this value because the ratio of floored totals is not what the user
/// set: with damage 2 and a 1.15 multiplier the ratio comes out as exactly 1.
/// </param>
public readonly record struct CoinBreakdown(
    int SkillIndex,
    int CoinIndex,
    double Roll,
    double ModStat,
    double Damage,
    double BaseDamage,
    IReadOnlyList<double> TargetDamage,
    IReadOnlyList<double> TargetDamageFinal,
    IReadOnlyList<double> TargetMoratoriumBuff);

public sealed class DamageResult
{
    public required IReadOnlyList<CoinBreakdown> Coins { get; init; }

    /// <summary>Damage of all coins added up; each coin's weight is already included.</summary>
    public required double Total { get; init; }

    /// <summary>The same total without Time Moratorium: the left side of the equation in the UI.</summary>
    public required double TotalBase { get; init; }

    /// <summary>Total damage per target: index 0 is the main target, then subtargets.</summary>
    public required IReadOnlyList<double> TotalByTarget { get; init; }
}

/// <summary>Everything the calculation needs.</summary>
public sealed class DamageInput
{
    public List<Skill> Skills { get; } = [];

    /// <summary>Resistances of the main target.</summary>
    public ResistanceSet Resistances { get; } = new();

    /// <summary>
    /// Time Moratorium: damage grows by 15% per stack and turns into pure Sloth damage,
    /// so it is also multiplied by the target's Sloth resistance at the end.
    /// </summary>
    public bool TimeMoratorium { get; set; }

    /// <summary>Number of Time Moratorium stacks: 1 or 2.</summary>
    public int TimeMoratoriumStacks { get; set; } = 1;
}

public static class DamageCalculator
{
    /// <summary>The constant in the denominator of Mod stat.</summary>
    public const double LevelDiffConstant = 25.0;

    /// <summary>Damage of a coin whose roll did not add up to anything: it still hits for one.</summary>
    public const double MinimumDamage = 1.0;

    /// <summary>How much one clash adds to Mod stat.</summary>
    public const double ClashCountModifier = 0.03;

    /// <summary>Damage added per Time Moratorium stack.</summary>
    public const double TimeMoratoriumPerStack = 0.15;

    /// <summary>
    /// Tolerance for flooring. Fractions such as 0.03 are inexact in a double, so where exact
    /// arithmetic gives 115 the result is 114.99999999999999; without the tolerance flooring
    /// would lose a whole point of damage.
    /// </summary>
    private const double FloorTolerance = 1e-9;

    private static double FloorWithTolerance(double value) => Math.Floor(value + FloorTolerance);

    /// <summary>
    /// What one resistance adds to Mod stat. Above 1 the bonus counts in full; below 1 the
    /// penalty is halved: 0.6 gives -0.2 rather than -0.4.
    /// </summary>
    public static double ResistanceModifier(double resistance) => resistance switch
    {
        > 1.0 => resistance - 1.0,
        < 1.0 => -(1.0 - resistance) / 2.0,
        _ => 0.0,
    };

    /// <summary>
    /// "Mod stat" (row 6): 1 + crit + diff / (|diff| + 25) + 3% per clash, plus the target's
    /// resistances to the skill's damage type and sin.
    /// damage.xlsx writes the denominator as |diff + 25|, but the reference MyCalculator sheet
    /// of the shared spreadsheet uses |diff| + 25. Both agree for diff > 0; for diff < 0 the
    /// damage.xlsx version gives an absurdly large penalty, so the reference formula is used.
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

    /// <summary>What applies to one particular target: resistances and modifiers.</summary>
    private readonly record struct TargetParameters(
        ResistanceSet Resistances,
        double ModDyn,
        double OffenseDefenseDiff,
        bool HasCrit,
        double Crit,
        bool TimeMoratorium,
        int TimeMoratoriumStacks);

    /// <summary>
    /// Parameters of target number <paramref name="targetIndex"/> (0 is the main target).
    /// Extra targets without their own settings use the main target's values.
    /// </summary>
    private static TargetParameters ParametersFor(DamageInput input, Coin coin, int targetIndex)
    {
        if (targetIndex >= 1 && targetIndex - 1 < coin.Subtargets.Count)
        {
            SubtargetOverride subtarget = coin.Subtargets[targetIndex - 1];

            return new TargetParameters(
                subtarget.Resistances,
                subtarget.ModDyn,
                subtarget.OffenseDefenseDiff,
                subtarget.HasCrit,
                subtarget.Crit,
                subtarget.TimeMoratorium,
                subtarget.TimeMoratoriumStacks);
        }

        return new TargetParameters(
            input.Resistances,
            coin.ModDyn,
            coin.OffenseDefenseDiff,
            coin.HasCrit,
            coin.Crit,
            input.TimeMoratorium,
            input.TimeMoratoriumStacks);
    }

    /// <summary>
    /// Calculates every coin of every skill: its roll, Mod stat and damage to each target,
    /// then the totals with and without Time Moratorium.
    /// </summary>
    public static DamageResult Calculate(DamageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<CoinBreakdown> breakdown = [];
        List<double> totalByTarget = [];
        double sum = 0.0;
        double sumBase = 0.0;

        for (int s = 0; s < input.Skills.Count; s++)
        {
            Skill skill = input.Skills[s];

            // The roll accumulates over the skill's coins: heads adds the coin's power,
            // tails keeps the previous coin's roll.
            double previousRoll = skill.BaseRoll;

            for (int c = 0; c < skill.Coins.Count; c++)
            {
                Coin coin = skill.Coins[c];

                double roll = previousRoll + (coin.Active ? coin.Power : 0.0);

                // Weight is the number of targets. Each has its own resistances, so damage is
                // calculated per target and summed. With equal resistances this matches the
                // old "multiply by weight" result.
                double coinDamage = 0.0;
                double coinBaseDamage = 0.0;
                List<double> targetDamage = [];
                List<double> targetDamageFinal = [];
                List<double> targetBuff = [];

                for (int t = 0; t < coin.Weight; t++)
                {
                    TargetParameters target = ParametersFor(input, coin, t);
                    ResistanceSet resistances = target.Resistances;

                    double targetModStat = ModStat(
                        target.OffenseDefenseDiff,
                        target.HasCrit ? target.Crit : 0.0,
                        coin.ClashCount,
                        ResistanceModifier(resistances[skill.Type])
                            + ResistanceModifier(resistances[skill.Sin]));

                    // A roll at or below zero doesn't zero the coin: it deals the minimum instead.
                    double core = roll <= 0.0
                        ? MinimumDamage
                        : roll * target.ModDyn * targetModStat;

                    double flatBonus = 0.0;
                    double percentBonus = 0.0;

                    foreach (CoinBonus bonus in coin.Bonuses)
                    {
                        // A bonus is scaled by the resistance to its own target, not to the
                        // skill's type: flat 10 against 0.5 gives 5, against 2.0 gives 20.
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

                    // A percent bonus is an addition, not a multiplier: a floored share of the
                    // base. Floored per target so every target takes whole damage; otherwise the
                    // per-target breakdown would show fractions.
                    double perTarget = FloorWithTolerance(
                        FloorWithTolerance(core)
                        + FloorWithTolerance(core * percentBonus / 100.0)
                        + flatBonus);

                    // The table shows damage without the moratorium, so keep both totals.
                    coinBaseDamage += perTarget;
                    targetDamage.Add(perTarget);

                    // Damage is already calculated by the normal rules; the stacks add on top,
                    // and the conversion to Sloth also applies the target's Sloth resistance.
                    double buff = target.TimeMoratorium
                        ? (1.0 + (TimeMoratoriumPerStack * target.TimeMoratoriumStacks))
                            * resistances[Element.Sloth]
                        : 1.0;

                    double perTargetFinal = target.TimeMoratorium
                        ? FloorWithTolerance(perTarget * buff)
                        : perTarget;

                    coinDamage += perTargetFinal;
                    targetDamageFinal.Add(perTargetFinal);
                    targetBuff.Add(buff);

                    while (totalByTarget.Count <= t)
                    {
                        totalByTarget.Add(0.0);
                    }

                    totalByTarget[t] += perTarget;
                }

                double damage = FloorWithTolerance(coinDamage);
                double baseDamage = FloorWithTolerance(coinBaseDamage);

                // The table shows Mod stat for the main target.
                double modStat = ModStat(
                    coin.OffenseDefenseDiff,
                    coin.HasCrit ? coin.Crit : 0.0,
                    coin.ClashCount,
                    ResistanceModifier(input.Resistances[skill.Type])
                        + ResistanceModifier(input.Resistances[skill.Sin]));

                breakdown.Add(new CoinBreakdown(
                    s, c, roll, modStat, damage, baseDamage,
                    targetDamage, targetDamageFinal, targetBuff));
                sum += damage;
                sumBase += baseDamage;

                previousRoll = roll;
            }
        }

        return new DamageResult
        {
            Coins = breakdown,
            Total = sum,
            TotalBase = sumBase,
            TotalByTarget = totalByTarget,
        };
    }
}
