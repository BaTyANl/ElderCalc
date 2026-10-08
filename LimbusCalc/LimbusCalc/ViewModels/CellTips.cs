using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace LimbusCalc.ViewModels;

/// <summary>
/// What to show in a tooltip over a table cell: a summary of the setup for a calculator cell,
/// the calculation for DPSC. Other cells need no tooltip — everything is visible in the cell.
/// </summary>
public static class CellTips
{
    /// <summary>Tooltip text for a cell, or null when the cell itself shows everything.</summary>
    public static string? Describe(TableCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (!cell.IsVisible || cell.IsEmpty)
        {
            return null;
        }

        if (cell.Column.Kind == TableCellKind.Computed)
        {
            return DescribeRatio(cell);
        }

        return cell.HasSetup ? DescribeSetup(cell.Setup!) : null;
    }

    /// <summary>"1T Damage 240 / Sin Cost 12 = 20".</summary>
    private static string? DescribeRatio(TableCell cell)
    {
        if (cell.Row is not TableRowViewModel row
            || cell.Column.DividendKey is not string dividendKey
            || cell.Column.DivisorKey is not string divisorKey
            || row.CellOf(dividendKey) is not TableCell dividend
            || row.CellOf(divisorKey) is not TableCell divisor)
        {
            return null;
        }

        string title = cell.Column.Description ?? cell.Column.Title;

        return $"{title}{Environment.NewLine}"
            + $"{dividend.Column.Title} {dividend.Value} / {divisor.Column.Title} {divisor.Value} = {cell.Value}";
    }

    /// <summary>
    /// The setup in a few lines: type and sin, base and coins, and whatever else differs from
    /// the defaults. Defaults are left out of the stored setup anyway, so they aren't shown.
    /// </summary>
    public static string? DescribeSetup(string setupJson)
    {
        JsonObject setup;

        try
        {
            if (JsonNode.Parse(setupJson) is not JsonObject parsed)
            {
                return null;
            }

            setup = parsed;
        }
        catch (Exception)
        {
            return null;
        }

        StringBuilder text = new();
        text.Append("From calculator");

        string type = Text(setup, "skillType");
        string sin = Text(setup, "skillSin");

        if (type.Length > 0 || sin.Length > 0)
        {
            text.AppendLine().Append(string.Join(" · ", new[] { type, sin }.Where(part => part.Length > 0)));
        }

        List<string> powers = [];
        List<int> crits = [];
        int inactive = 0;

        if (setup["coins"] is JsonArray coins)
        {
            for (int i = 0; i < coins.Count; i++)
            {
                if (coins[i] is not JsonObject coin)
                {
                    continue;
                }

                if (!Flag(coin, "active"))
                {
                    inactive++;
                    continue;
                }

                powers.Add(Signed(Number(coin, "power")));

                if (Flag(coin, "hasCrit"))
                {
                    crits.Add(i + 1);
                }
            }
        }

        text.AppendLine().Append($"Base {Format(Number(setup, "baseRoll"))}");

        if (powers.Count > 0)
        {
            text.Append($" · Coins {string.Join(" ", powers)}");
        }

        if (inactive > 0)
        {
            text.Append($" ({inactive} off)");
        }

        if (crits.Count > 0)
        {
            text.AppendLine().Append($"Crit on coin {string.Join(", ", crits)}");
        }

        double clashes = Number(setup, "clashCount");
        double passive = Number(setup, "passiveModDynPercent");

        if (clashes != 0)
        {
            text.AppendLine().Append($"Clashes {Format(clashes)}");
        }

        if (passive != 0)
        {
            text.AppendLine().Append($"Passive {Signed(passive)}%");
        }

        if (setup["bonuses"] is JsonArray { Count: > 0 } bonuses)
        {
            text.AppendLine().Append(bonuses.Count == 1 ? "1 bonus row" : $"{bonuses.Count} bonus rows");
        }

        if (setup["resistances"] is JsonObject { Count: > 0 } resistances)
        {
            text.AppendLine().Append("Resistances " + string.Join(", ", resistances
                .Select(pair => $"{pair.Key} ×{Format(Number(resistances, pair.Key))}")));
        }

        if (Flag(setup, "timeMoratorium"))
        {
            double stacks = setup["timeMoratoriumStacks"] is null ? 1 : Number(setup, "timeMoratoriumStacks");
            text.AppendLine().Append($"Time Moratorium ×{Format(stacks)}");
        }

        if (setup["targets"] is JsonArray { Count: > 0 } targets)
        {
            IEnumerable<string> names = targets
                .OfType<JsonObject>()
                .Select(target => Text(target, "name"))
                .Where(name => name.Length > 0);

            text.AppendLine().Append($"Subtargets: {string.Join(", ", names)}");
        }

        text.AppendLine().Append("Right-click → Export to ElderCalc to open it");
        return text.ToString();
    }

    private static string Text(JsonObject owner, string key)
    {
        try
        {
            return (string?)owner[key] ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static double Number(JsonObject owner, string key)
    {
        try
        {
            return owner[key]?.GetValue<double>() ?? 0.0;
        }
        catch (Exception)
        {
            return 0.0;
        }
    }

    private static bool Flag(JsonObject owner, string key)
    {
        try
        {
            return owner[key]?.GetValue<bool>() ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Signed(double value) => value >= 0 ? $"+{Format(value)}" : Format(value);
}
