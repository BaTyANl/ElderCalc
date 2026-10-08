using System.Text.Json.Nodes;

namespace LimbusCalc.Storage;

/// <summary>
/// Значения полей набора калькулятора, которые подставляются, если в файле поля нет.
/// Писатель опускает равные им поля, читатель их подставляет — поэтому и то и другое
/// берёт их отсюда. Без WPF: так ужатие проверяется консольными проверками и
/// работает при загрузке таблицы в фоновом потоке.
/// </summary>
public static class SetupDefaults
{
    public const double NeutralResistance = 1.0;

    public const double CritPercent = 20.0;

    public const double Weight = 1.0;

    public const double MoratoriumStacks = 1.0;

    /// <summary>
    /// Убирает из набора поля, которые читатель и так получит значением по умолчанию:
    /// нулевые моды, нейтральные сопротивления, пустые списки, цели без изменений.
    /// Набор становится в разы короче, а прочитается ровно так же. Годится и для
    /// наборов, записанных раньше целиком, — их так ужимают при загрузке таблицы.
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

                // Подцели монеты идут по порядку, поэтому сами остаются в списке —
                // выбрасываем только их поля по умолчанию. Название держим всегда:
                // по нему подцель находит свою общую часть.
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

        // Цель, у которой всё по умолчанию, не пишется вовсе: читатель сбрасывает
        // все цели перед тем, как расставить записанные.
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
