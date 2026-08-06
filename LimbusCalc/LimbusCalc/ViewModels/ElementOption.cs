using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>Пункт выпадающего списка: значение, подпись и иконка.</summary>
public sealed class ElementOption
{
    public required Element Element { get; init; }

    public required string Name { get; init; }

    /// <summary>Путь к иконке; у истинного урона картинки нет.</summary>
    public string? IconPath { get; init; }

    public override string ToString() => Name;
}

/// <summary>Готовые наборы пунктов для выпадающих списков.</summary>
public static class ElementOptions
{
    /// <summary>Имя файла иконки не всегда совпадает с названием: gluttony лежит как glut.png.</summary>
    private static ElementOption Create(Element element, string name, string iconFile) => new()
    {
        Element = element,
        Name = name,
        IconPath = $"pack://application:,,,/Assets/Icons/{iconFile}.png",
    };

    public static IReadOnlyList<ElementOption> DamageTypes { get; } =
    [
        Create(Element.Slash, "Slash", "slash"),
        Create(Element.Blunt, "Blunt", "blunt"),
        Create(Element.Pierce, "Pierce", "pierce"),
    ];

    public static IReadOnlyList<ElementOption> Sins { get; } =
    [
        Create(Element.Wrath, "Wrath", "wrath"),
        Create(Element.Lust, "Lust", "lust"),
        Create(Element.Sloth, "Sloth", "sloth"),
        Create(Element.Gluttony, "Gluttony", "glut"),
        Create(Element.Gloom, "Gloom", "gloom"),
        Create(Element.Pride, "Pride", "pride"),
        Create(Element.Envy, "Envy", "envy"),
    ];

    /// <summary>На что может целиться бонус: типы урона, грехи и истинный урон.</summary>
    public static IReadOnlyList<ElementOption> BonusTargets { get; } =
    [
        .. DamageTypes,
        .. Sins,
        new ElementOption { Element = Element.True, Name = "True" },
    ];

    /// <summary>
    /// Порядок сопротивлений: сперва типы урона, затем грехи. В этом же порядке
    /// идут поля в таблице подцелей и иконки над ними.
    /// </summary>
    public static IReadOnlyList<ElementOption> ResistanceOrder { get; } =
    [
        For(Element.Slash), For(Element.Pierce), For(Element.Blunt),
        .. Sins,
    ];

    public static ElementOption For(Element element) =>
        BonusTargets.First(option => option.Element == element);
}
