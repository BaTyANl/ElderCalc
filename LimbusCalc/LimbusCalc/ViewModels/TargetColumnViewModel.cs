namespace LimbusCalc.ViewModels;

/// <summary>
/// A column header of the damage-by-target table. Clicking it sorts by this column;
/// clicking again reverses the order.
/// </summary>
public sealed class TargetColumnViewModel
{
    public required string Title { get; init; }

    public required TargetSortKey Key { get; init; }

    /// <summary>Coin index for <see cref="TargetSortKey.Coin"/>; -1 otherwise.</summary>
    public required int CoinIndex { get; init; }

    /// <summary>Sort direction arrow on the column the table is sorted by.</summary>
    public required string Indicator { get; init; }

    /// <summary>The full header: the title plus the arrow when sorted by this column.</summary>
    public string Header => Indicator.Length == 0 ? Title : $"{Title} {Indicator}";
}
