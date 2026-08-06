namespace LimbusCalc.Calculation;

/// <summary>
/// Тип урона, грех или истинный урон. Один перечисляемый тип на всё, потому что
/// бонус может целиться в любое из этого, а иконки и подписи задаются в одном месте.
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

    /// <summary>Истинный урон: сопротивления его не снижают.</summary>
    True,
}

/// <summary>Сопротивления одной цели. Чего нет в наборе — считается за 1.0.</summary>
public sealed class ResistanceSet
{
    private readonly Dictionary<Element, double> _values = [];

    /// <summary>Истинный урон сопротивлениями не задевается никогда.</summary>
    public double this[Element element]
    {
        get => element != Element.True && _values.TryGetValue(element, out double value) ? value : 1.0;
        set => _values[element] = value;
    }
}

/// <summary>Как бонус применяется к урону монеты.</summary>
public enum BonusKind
{
    /// <summary>Прибавка в единицах, до умножения на вес.</summary>
    Flat,

    /// <summary>Прибавка в процентах от основы.</summary>
    Percent,
}

/// <summary>
/// Бонус монеты. <see cref="Target"/> пока только хранится: он понадобится,
/// когда появятся сопротивления.
/// </summary>
public sealed class CoinBonus
{
    public BonusKind Kind { get; set; }

    public Element Target { get; set; } = Element.True;

    public double Value { get; set; }
}
