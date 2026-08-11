namespace LimbusCalc.ViewModels;

/// <summary>
/// Заголовок столбца таблицы распределения. По нажатию таблица сортируется по этому
/// столбцу; повторное нажатие переворачивает порядок.
/// </summary>
public sealed class TargetColumnViewModel
{
    public required string Title { get; init; }

    public required TargetSortKey Key { get; init; }

    /// <summary>Номер монеты для <see cref="TargetSortKey.Coin"/>; иначе -1.</summary>
    public required int CoinIndex { get; init; }

    /// <summary>Стрелка направления у столбца, по которому сейчас сортируем.</summary>
    public required string Indicator { get; init; }

    /// <summary>Подпись целиком: название и, если сортируем по нему, стрелка.</summary>
    public string Header => Indicator.Length == 0 ? Title : $"{Title} {Indicator}";
}
