using System.Globalization;
using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>Which column the damage-by-target table is sorted by.</summary>
public enum TargetSortKey
{
    /// <summary>Calculation order: the main target, then by position.</summary>
    None,

    /// <summary>Target name, alphabetically.</summary>
    Title,

    /// <summary>Damage of one coin.</summary>
    Coin,

    /// <summary>Total per target.</summary>
    Total,
}

/// <summary>A row of the damage-by-target table: one target, a cell per coin plus the total.</summary>
public sealed class TargetDamageRow
{
    public required string Title { get; init; }

    /// <summary>Damage per coin without Time Moratorium, as in the main window.</summary>
    public required IReadOnlyList<string> CoinDamage { get; init; }

    /// <summary>
    /// The same damage as numbers, for sorting. Empty where the coin doesn't hit the target:
    /// that's not zero damage but no hit at all, so such rows go to the bottom.
    /// </summary>
    public required IReadOnlyList<double?> CoinValues { get; init; }

    /// <summary>Sum over coins without Time Moratorium.</summary>
    public required double BaseTotal { get; init; }

    /// <summary>This target's Time Moratorium multiplier; 1 means no moratorium.</summary>
    public required double MoratoriumBuff { get; init; }

    /// <summary>Total per target including Time Moratorium.</summary>
    public required double Total { get; init; }

    /// <summary>The moratorium changed the damage to this target.</summary>
    public bool Affected => Total != BaseTotal;

    /// <summary>
    /// Every coin hitting this target produced the same multiplier. If not, there is no single
    /// number, and the equation can't be shown: the ratio of totals would look like a
    /// multiplier nobody set.
    /// </summary>
    public required bool UniformBuff { get; init; }

    /// <summary>Whether the total cell shows an equation instead of a single number.</summary>
    public bool ShowEquation => Affected && UniformBuff;

    /// <summary>The total is affected by the moratorium but can't be shown as an equation.</summary>
    public bool ShowPlainAffected => Affected && !UniformBuff;

    /// <summary>
    /// The first number in the total cell: the left side of the equation, or the total itself.
    /// </summary>
    public string LeadingText => Format(ShowEquation ? BaseTotal : Total);

    public string BaseTotalText => Format(BaseTotal);

    public string BuffText => MoratoriumBuff.ToString("0.##", CultureInfo.InvariantCulture);

    public string TotalText => Format(Total);

    private static string Format(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>What the main target of every coin is called.</summary>
    private const string MainTitle = "Main target";

    /// <summary>
    /// Splits the calculation result by target. A row is an enemy, not a list position:
    /// subtargets with the same name on different coins share a row, different names get
    /// different rows. A coin that doesn't hit this enemy gets a dash — not zero damage,
    /// but no hit.
    /// </summary>
    /// <param name="coinSubtargetTitles">
    /// Subtarget names per coin: the outer list is coins, the inner one positions, where
    /// position zero is subtarget 2. An unnamed subtarget is named by its number.
    /// </param>
    public static IReadOnlyList<TargetDamageRow> Build(
        IReadOnlyList<CoinBreakdown> coins,
        IReadOnlyList<IReadOnlyList<string>>? coinSubtargetTitles = null)
    {
        int targets = 0;

        foreach (CoinBreakdown coin in coins)
        {
            targets = Math.Max(targets, coin.TargetDamage.Count);
        }

        // Row order: the main target, then by position left to right, so a new enemy lands
        // where it was first hit.
        List<string> order = [];
        HashSet<string> known = new(StringComparer.OrdinalIgnoreCase);

        for (int t = 0; t < targets; t++)
        {
            for (int c = 0; c < coins.Count; c++)
            {
                if (t >= coins[c].TargetDamage.Count)
                {
                    continue;
                }

                string title = TitleOf(c, t, coinSubtargetTitles);

                if (known.Add(title))
                {
                    order.Add(title);
                }
            }
        }

        List<TargetDamageRow> rows = [];

        foreach (string title in order)
        {
            List<string> cells = [];
            List<double?> values = [];
            double baseTotal = 0.0;
            double total = 0.0;
            double? sharedBuff = null;
            bool uniform = true;

            for (int c = 0; c < coins.Count; c++)
            {
                CoinBreakdown coin = coins[c];
                double coinBase = 0.0;
                double coinFinal = 0.0;
                bool hits = false;

                for (int t = 0; t < coin.TargetDamage.Count; t++)
                {
                    if (!string.Equals(
                            TitleOf(c, t, coinSubtargetTitles),
                            title,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    hits = true;
                    coinBase += coin.TargetDamage[t];
                    coinFinal += coin.TargetDamageFinal[t];

                    // This coin's multiplier for this target. Take the one the formula set, not
                    // the ratio of totals: flooring would distort it on small damage.
                    double buff = t < coin.TargetMoratoriumBuff.Count
                        ? coin.TargetMoratoriumBuff[t]
                        : 1.0;

                    if (sharedBuff is null)
                    {
                        sharedBuff = buff;
                    }
                    else if (Math.Abs(sharedBuff.Value - buff) > 1e-6)
                    {
                        uniform = false;
                    }
                }

                cells.Add(hits ? Format(coinBase) : "—");
                values.Add(hits ? coinBase : null);
                baseTotal += coinBase;
                total += coinFinal;
            }

            rows.Add(new TargetDamageRow
            {
                Title = title,
                CoinDamage = cells,
                CoinValues = values,
                BaseTotal = baseTotal,
                MoratoriumBuff = sharedBuff ?? 1.0,
                UniformBuff = uniform,
                Total = total,
            });
        }

        return rows;
    }

    /// <summary>
    /// Reorders rows by the chosen column. The sort is stable: rows with equal values keep
    /// their calculation order.
    /// </summary>
    /// <param name="coinIndex">Which coin, when sorting by a coin column.</param>
    public static IReadOnlyList<TargetDamageRow> Sort(
        IReadOnlyList<TargetDamageRow> rows,
        TargetSortKey key,
        int coinIndex,
        bool descending)
    {
        switch (key)
        {
            case TargetSortKey.Title:
                return [.. descending
                    ? rows.OrderByDescending(row => row.Title, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase)];

            case TargetSortKey.Total:
                return [.. descending
                    ? rows.OrderByDescending(row => row.Total)
                    : rows.OrderBy(row => row.Total)];

            case TargetSortKey.Coin:
                // Targets the coin doesn't reach always go last: they have a dash, not zero
                // damage, and it doesn't belong among the numbers.
                bool Hits(TargetDamageRow row) =>
                    coinIndex >= 0
                    && coinIndex < row.CoinValues.Count
                    && row.CoinValues[coinIndex] is not null;

                IEnumerable<TargetDamageRow> hitting = rows.Where(Hits);

                hitting = descending
                    ? hitting.OrderByDescending(row => row.CoinValues[coinIndex]!.Value)
                    : hitting.OrderBy(row => row.CoinValues[coinIndex]!.Value);

                return [.. hitting, .. rows.Where(row => !Hits(row))];

            default:
                return rows;
        }
    }

    /// <summary>
    /// What this coin calls its target at this position. The name decides the row:
    /// the same name is one enemy across coins, different names are different rows.
    /// </summary>
    private static string TitleOf(
        int coin,
        int target,
        IReadOnlyList<IReadOnlyList<string>>? coinSubtargetTitles)
    {
        if (target == 0)
        {
            return MainTitle;
        }

        int index = target - 1;

        if (coinSubtargetTitles is not null && coin < coinSubtargetTitles.Count)
        {
            IReadOnlyList<string> titles = coinSubtargetTitles[coin];

            if (index < titles.Count && !string.IsNullOrWhiteSpace(titles[index]))
            {
                return titles[index].Trim();
            }
        }

        return $"Subtarget {target + 1}";
    }
}
