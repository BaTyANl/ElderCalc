namespace LimbusCalc.Calculation;

/// <summary>
/// A damage type, a sin or true damage. One enum covers all of them because a bonus
/// can target any of these, and icons and labels are defined in one place.
/// </summary>
public enum Element
{
    Slash,
    Blunt,
    Pierce,

    Wrath,
    Lust,
    Sloth,
    Gluttony,
    Gloom,
    Pride,
    Envy,

    /// <summary>True damage: resistances never reduce it.</summary>
    True,
}

/// <summary>Resistances of one target. Anything not set counts as 1.0.</summary>
public sealed class ResistanceSet
{
    private readonly Dictionary<Element, double> _values = [];

    /// <summary>True damage is never affected by resistances.</summary>
    public double this[Element element]
    {
        get => element != Element.True && _values.TryGetValue(element, out double value) ? value : 1.0;
        set => _values[element] = value;
    }
}

/// <summary>How a bonus is applied to a coin's damage.</summary>
public enum BonusKind
{
    /// <summary>A flat addition in units, applied before multiplying by weight.</summary>
    Flat,

    /// <summary>An addition in percent of the base damage.</summary>
    Percent,
}

/// <summary>
/// A coin bonus. <see cref="Target"/> picks the resistance that scales it: a bonus aimed
/// at Sloth is multiplied by the target's Sloth resistance, not by the skill's type or sin.
/// </summary>
public sealed class CoinBonus
{
    public BonusKind Kind { get; set; }

    public Element Target { get; set; } = Element.True;

    public double Value { get; set; }
}
