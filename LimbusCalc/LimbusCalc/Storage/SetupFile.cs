using System.IO;
using System.Text.Json.Nodes;
using LimbusCalc.Calculation;
using LimbusCalc.ViewModels;

namespace LimbusCalc.Storage;

/// <summary>
/// Saves and restores the whole calculator setup: shared parameters, main target
/// resistances, bonus rows, coins and their subtargets.
/// The shared parts of subtargets (resistances and moratorium) are a separate list linked
/// to coins by name, the same way they are organized in the app.
/// </summary>
public static class SetupFile
{
    public const string DialogFilter = "JSON file (*.json)|*.json";

    /// <summary>Writes the setup to a file the user picked, indented so it can be read by hand.</summary>
    public static void Save(MainViewModel model, string path)
    {
        ArgumentNullException.ThrowIfNull(model);

        File.WriteAllText(path, ToJson(model).ToJsonString(JsonFormat.Readable));
    }

    /// <summary>Replaces the calculator's state with the setup from a file.</summary>
    public static void Load(MainViewModel model, string path)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject setup)
        {
            throw new InvalidDataException("The file should contain a calculator setup.");
        }

        FromJson(model, setup);
    }

    /// <summary>
    /// The setup as JSON with default values left out — the same form that is stored
    /// in table cells.
    /// </summary>
    public static JsonObject ToJson(MainViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        JsonObject resistances = [];

        foreach (ResistanceViewModel resistance in AllResistances(model))
        {
            resistances[resistance.Option.Element.ToString()] = resistance.Value;
        }

        JsonArray bonuses = [];

        for (int i = 0; i < model.BonusRows.Count; i++)
        {
            BonusRowViewModel row = model.BonusRows[i];
            JsonArray values = [];

            foreach (CoinViewModel coin in model.Coins)
            {
                values.Add(i < coin.Bonuses.Count ? coin.Bonuses[i].Value : 0.0);
            }

            bonuses.Add(new JsonObject
            {
                ["kind"] = row.Kind.ToString(),
                ["target"] = row.Target.Element.ToString(),
                ["values"] = values,
            });
        }

        JsonArray coins = [];

        foreach (CoinViewModel coin in model.Coins)
        {
            JsonArray subtargets = [];

            foreach (SubtargetViewModel subtarget in coin.Subtargets)
            {
                subtargets.Add(new JsonObject
                {
                    ["name"] = subtarget.Name,
                    ["modDynPercent"] = subtarget.ModDynPercent,
                    ["offenseDefenseDiff"] = subtarget.OffenseDefenseDiff,
                    ["hasCrit"] = subtarget.HasCrit,
                    ["critPercent"] = subtarget.CritPercent,
                });
            }

            coins.Add(new JsonObject
            {
                ["active"] = coin.Active,
                ["power"] = coin.Power,
                ["modDynPercent"] = coin.ModDynPercent,
                ["offenseDefenseDiff"] = coin.OffenseDefenseDiff,
                ["hasCrit"] = coin.HasCrit,
                ["critPercent"] = coin.CritPercent,
                ["weight"] = coin.Weight,
                ["subtargets"] = subtargets,
            });
        }

        // A subtarget's shared part is common to all coins, so it's written once per name.
        JsonArray targets = [];
        HashSet<string> written = [];

        foreach (SubtargetViewModel subtarget in AllSubtargets(model))
        {
            if (!written.Add(subtarget.Name))
            {
                continue;
            }

            JsonObject targetResistances = [];

            foreach (ResistanceViewModel resistance in subtarget.Resistances)
            {
                targetResistances[resistance.Option.Element.ToString()] = resistance.Value;
            }

            targets.Add(new JsonObject
            {
                ["name"] = subtarget.Name,
                ["resistances"] = targetResistances,
                ["timeMoratorium"] = subtarget.Shared.TimeMoratorium,
                ["timeMoratoriumStacks"] = subtarget.Shared.TimeMoratoriumStacks,
            });
        }

        JsonObject setup = new()
        {
            ["baseRoll"] = model.BaseRoll,
            ["passiveModDynPercent"] = model.PassiveModDynPercent,
            ["skillType"] = model.SkillType.Element.ToString(),
            ["skillSin"] = model.SkillSin.Element.ToString(),
            ["clashCount"] = model.ClashCount,
            ["timeMoratorium"] = model.TimeMoratorium,
            ["timeMoratoriumStacks"] = model.TimeMoratoriumStacks,
            ["resistances"] = resistances,
            ["bonuses"] = bonuses,
            ["coins"] = coins,
            ["targets"] = targets,
        };

        return SetupDefaults.Compact(setup);
    }


    /// <summary>
    /// Puts a stored setup into the calculator. Missing fields mean defaults, so every field
    /// is reset rather than left as it was.
    /// </summary>
    public static void FromJson(MainViewModel model, JsonObject setup)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(setup);

        model.BaseRoll = Number(setup["baseRoll"]);
        model.PassiveModDynPercent = Number(setup["passiveModDynPercent"]);
        model.ClashCount = (int)Number(setup["clashCount"]);
        model.TimeMoratorium = Flag(setup["timeMoratorium"]);
        model.TimeMoratoriumStacks = (int)Number(setup["timeMoratoriumStacks"], SetupDefaults.MoratoriumStacks);

        if (Option(setup["skillType"], ElementOptions.DamageTypes) is ElementOption type)
        {
            model.SkillType = type;
        }

        if (Option(setup["skillSin"], ElementOptions.Sins) is ElementOption sin)
        {
            model.SkillSin = sin;
        }

        // Resistances are always set: a value missing from the file means 1.0, not
        // "keep whatever the calculator had".
        ApplyResistances(AllResistances(model), setup["resistances"] as JsonObject);

        // Coins come before bonuses: a bonus row hands its value to every coin.
        JsonArray coins = setup["coins"] as JsonArray ?? [];

        while (model.Coins.Count > coins.Count && model.Coins.Count > 1)
        {
            model.RemoveLastCoin();
        }

        while (model.Coins.Count < coins.Count)
        {
            model.AddCoin();
        }

        LoadBonuses(model, setup["bonuses"] as JsonArray ?? []);

        for (int i = 0; i < model.Coins.Count && i < coins.Count; i++)
        {
            if (coins[i] is JsonObject stored)
            {
                LoadCoin(model.Coins[i], stored);
            }
        }

        LoadTargets(model, setup["targets"] as JsonArray ?? []);

        model.Recalculate();
    }

    private static void LoadBonuses(MainViewModel model, JsonArray bonuses)
    {
        while (model.BonusRows.Count > 0)
        {
            model.RemoveBonus(model.BonusRows[^1]);
        }

        foreach (JsonNode? node in bonuses)
        {
            if (node is not JsonObject stored)
            {
                continue;
            }

            BonusKind kind = (string?)stored["kind"] == nameof(BonusKind.Percent)
                ? BonusKind.Percent
                : BonusKind.Flat;

            model.AddBonus(kind);

            BonusRowViewModel row = model.BonusRows[^1];

            if (Option(stored["target"], ElementOptions.BonusTargets) is ElementOption target)
            {
                row.Target = target;
            }

            JsonArray values = stored["values"] as JsonArray ?? [];
            int index = model.BonusRows.Count - 1;

            for (int i = 0; i < model.Coins.Count && i < values.Count; i++)
            {
                if (index < model.Coins[i].Bonuses.Count)
                {
                    model.Coins[i].Bonuses[index].Value = Number(values[i]);
                }
            }
        }
    }

    private static void LoadCoin(CoinViewModel coin, JsonObject stored)
    {
        coin.Active = Flag(stored["active"]);
        coin.Power = Number(stored["power"]);
        coin.ModDynPercent = Number(stored["modDynPercent"]);
        coin.OffenseDefenseDiff = Number(stored["offenseDefenseDiff"]);
        coin.HasCrit = Flag(stored["hasCrit"]);
        coin.CritPercent = Number(stored["critPercent"], SetupDefaults.CritPercent);

        // Weight goes last among the simple fields: it creates the subtargets.
        coin.Weight = Math.Max(1, (int)Number(stored["weight"], SetupDefaults.Weight));

        JsonArray subtargets = stored["subtargets"] as JsonArray ?? [];

        for (int i = 0; i < coin.Subtargets.Count && i < subtargets.Count; i++)
        {
            if (subtargets[i] is not JsonObject storedSubtarget)
            {
                continue;
            }

            SubtargetViewModel subtarget = coin.Subtargets[i];

            // Name first: it puts the subtarget into its shared group.
            if ((string?)storedSubtarget["name"] is string name)
            {
                subtarget.Name = name;
            }

            subtarget.ModDynPercent = Number(storedSubtarget["modDynPercent"]);
            subtarget.OffenseDefenseDiff = Number(storedSubtarget["offenseDefenseDiff"]);
            subtarget.HasCrit = Flag(storedSubtarget["hasCrit"]);
            subtarget.CritPercent = Number(storedSubtarget["critPercent"], SetupDefaults.CritPercent);
        }
    }

    private static void LoadTargets(MainViewModel model, JsonArray targets)
    {
        // Reset everything to defaults first: a target that was never changed isn't written,
        // and the calculator's previous state must not linger in it.
        foreach (SubtargetViewModel subtarget in AllSubtargets(model))
        {
            ApplyResistances(subtarget.Resistances, null);
            subtarget.Shared.TimeMoratorium = false;
            subtarget.Shared.TimeMoratoriumStacks = (int)SetupDefaults.MoratoriumStacks;
        }

        foreach (JsonNode? node in targets)
        {
            if (node is not JsonObject stored || (string?)stored["name"] is not string name)
            {
                continue;
            }

            foreach (SubtargetViewModel subtarget in AllSubtargets(model))
            {
                if (subtarget.Name != name)
                {
                    continue;
                }

                ApplyResistances(subtarget.Resistances, stored["resistances"] as JsonObject);

                subtarget.Shared.TimeMoratorium = Flag(stored["timeMoratorium"]);
                subtarget.Shared.TimeMoratoriumStacks = (int)Number(stored["timeMoratoriumStacks"], SetupDefaults.MoratoriumStacks);
                break;
            }
        }
    }

    /// <summary>Resistances from the file; missing ones are neutral, 1.0.</summary>
    private static void ApplyResistances(
        IEnumerable<ResistanceViewModel> resistances,
        JsonObject? stored)
    {
        foreach (ResistanceViewModel resistance in resistances)
        {
            resistance.Value = Number(stored?[resistance.Option.Element.ToString()], SetupDefaults.NeutralResistance);
        }
    }

    private static IEnumerable<ResistanceViewModel> AllResistances(MainViewModel model) =>
        [.. model.TypeResistances, .. model.SinResistancesTop, .. model.SinResistancesBottom];

    private static IEnumerable<SubtargetViewModel> AllSubtargets(MainViewModel model) =>
        model.Coins.SelectMany(coin => coin.Subtargets);

    private static ElementOption? Option(JsonNode? node, IReadOnlyList<ElementOption> options) =>
        (string?)node is string name && Enum.TryParse(name, out Element element)
            ? options.FirstOrDefault(option => option.Element == element)
            : null;

    private static double Number(JsonNode? node, double fallback = 0.0)
    {
        try
        {
            return node is null ? fallback : node.GetValue<double>();
        }
        catch (Exception)
        {
            // Not a number in the file here — use the default.
            return fallback;
        }
    }

    private static bool Flag(JsonNode? node)
    {
        try
        {
            return node is not null && node.GetValue<bool>();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
