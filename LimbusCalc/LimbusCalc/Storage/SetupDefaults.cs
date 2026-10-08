using System.Text.Json.Nodes;

namespace LimbusCalc.Storage;

/// <summary>
/// Default values of calculator setup fields, used when a field is missing from the file.
/// The writer omits fields equal to these and the reader fills them back in, so both take
/// them from here. No WPF dependencies: the console checks can test compaction, and it runs
/// while tables load on a background thread.
/// </summary>
public static class SetupDefaults
{
    public const double NeutralResistance = 1.0;

    public const double CritPercent = 20.0;

    public const double Weight = 1.0;

    public const double MoratoriumStacks = 1.0;

    /// <summary>
    /// Removes fields the reader would get as defaults anyway: zero modifiers, neutral
    /// resistances, empty lists, untouched targets. The setup becomes several times shorter
    /// and reads back exactly the same. Also works on older setups written in full, which
    /// are compacted this way when a table loads.
    /// </summary>
    public static JsonObject Compact(JsonObject setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        RemoveIf(setup, "baseRoll", 0.0);
        RemoveIf(setup, "passiveModDynPercent", 0.0);
        RemoveIf(setup, "clashCount", 0.0);
        RemoveIf(setup, "timeMoratorium", false);
        RemoveIf(setup, "timeMoratoriumStacks", MoratoriumStacks);
        CompactResistances(setup);

        if (setup["coins"] is JsonArray coins)
        {
            foreach (JsonObject coin in coins.OfType<JsonObject>())
            {
                RemoveIf(coin, "active", false);
                RemoveIf(coin, "power", 0.0);
                RemoveIf(coin, "modDynPercent", 0.0);
                RemoveIf(coin, "offenseDefenseDiff", 0.0);
                RemoveIf(coin, "hasCrit", false);
                RemoveIf(coin, "critPercent", CritPercent);
                RemoveIf(coin, "weight", Weight);

                // A coin's subtargets are positional, so they stay in the list and only lose
                // their default fields. The name is always kept: it links the subtarget to
                // its shared part.
                if (coin["subtargets"] is JsonArray subtargets)
                {
                    foreach (JsonObject subtarget in subtargets.OfType<JsonObject>())
                    {
                        RemoveIf(subtarget, "modDynPercent", 0.0);
                        RemoveIf(subtarget, "offenseDefenseDiff", 0.0);
                        RemoveIf(subtarget, "hasCrit", false);
                        RemoveIf(subtarget, "critPercent", CritPercent);
                    }
                }

                RemoveIfEmpty(coin, "subtargets");
            }
        }

        // A target with everything at default isn't written at all: the reader resets
        // every target before applying the stored ones.
        if (setup["targets"] is JsonArray targets)
        {
            foreach (JsonObject target in targets.OfType<JsonObject>())
            {
                CompactResistances(target);
                RemoveIf(target, "timeMoratorium", false);
                RemoveIf(target, "timeMoratoriumStacks", MoratoriumStacks);
            }

            foreach (JsonObject bare in targets.OfType<JsonObject>().Where(target => target.Count <= 1).ToList())
            {
                targets.Remove(bare);
            }
        }

        RemoveIfEmpty(setup, "bonuses");
        RemoveIfEmpty(setup, "targets");
        return setup;
    }

    private static void CompactResistances(JsonObject owner)
    {
        if (owner["resistances"] is not JsonObject resistances)
        {
            return;
        }

        foreach (string element in resistances.Select(pair => pair.Key).ToList())
        {
            RemoveIf(resistances, element, NeutralResistance);
        }

        RemoveIfEmpty(owner, "resistances");
    }

    private static void RemoveIf(JsonObject owner, string key, double value)
    {
        if (owner[key] is JsonValue stored && stored.TryGetValue(out double actual) && actual == value)
        {
            owner.Remove(key);
        }
    }

    private static void RemoveIf(JsonObject owner, string key, bool value)
    {
        if (owner[key] is JsonValue stored && stored.TryGetValue(out bool actual) && actual == value)
        {
            owner.Remove(key);
        }
    }

    private static void RemoveIfEmpty(JsonObject owner, string key)
    {
        if (owner[key] is JsonArray { Count: 0 } or JsonObject { Count: 0 })
        {
            owner.Remove(key);
        }
    }
}
