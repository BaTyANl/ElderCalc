using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>A dropdown item: value, label and icon.</summary>
public sealed class ElementOption
{
    public required Element Element { get; init; }

    public required string Name { get; init; }

    /// <summary>Path to the icon; true damage has none.</summary>
    public string? IconPath { get; init; }

    public override string ToString() => Name;
}

/// <summary>Ready-made item lists for dropdowns.</summary>
public static class ElementOptions
{
    /// <summary>The icon file name doesn't always match the label: gluttony is glut.png.</summary>
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

    /// <summary>What a bonus can target: damage types, sins and true damage.</summary>
    public static IReadOnlyList<ElementOption> BonusTargets { get; } =
    [
        .. DamageTypes,
        .. Sins,
        new ElementOption { Element = Element.True, Name = "True" },
    ];

    /// <summary>
    /// Resistance order: damage types first, then sins. Subtarget fields and the icons
    /// above them follow the same order.
    /// </summary>
    public static IReadOnlyList<ElementOption> ResistanceOrder { get; } =
    [
        For(Element.Slash), For(Element.Pierce), For(Element.Blunt),
        .. Sins,
    ];

    public static ElementOption For(Element element) =>
        BonusTargets.First(option => option.Element == element);
}
