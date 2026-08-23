using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LimbusCalc.ViewModels;

/// <summary>
/// Пункт фильтра с галочкой: тип урона, грех или грешник. Иконки у грешников нет,
/// поэтому она может быть пустой.
/// </summary>
public sealed class FilterOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public required string Name { get; init; }

    public string? IconPath { get; init; }

    /// <summary>Что именно выбрано; у грешника пусто — он сравнивается по названию.</summary>
    public ElementOption? Option { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>Один список фильтра: подпись для окошка и сами пункты.</summary>
public sealed class FilterListViewModel : ObservableObject
{
    public required string Title { get; init; }

    public required IReadOnlyList<FilterOptionViewModel> Items { get; init; }

    /// <summary>Подпись кнопки: сколько пунктов отмечено.</summary>
    public string Label
    {
        get
        {
            int count = Items.Count(item => item.IsSelected);

            return count == 0 ? $"{Title}: any" : $"{Title}: {count}";
        }
    }

    public bool Any => Items.Any(item => item.IsSelected);

    public void Clear()
    {
        foreach (FilterOptionViewModel item in Items)
        {
            item.IsSelected = false;
        }
    }

    internal void Refresh() => OnPropertyChanged(nameof(Label));
}

/// <summary>
/// Отбор строк и значений таблицы. В каждом списке можно отметить несколько пунктов;
/// пустой список значит «любой». Тип и грех сравниваются с метками клетки,
/// грешники — со столбцом Sinner.
/// </summary>
public sealed class TableFilterViewModel : ObservableObject
{
    private string _search = string.Empty;

    public TableFilterViewModel(IReadOnlyList<string> sinners, IReadOnlyList<string> rarities)
    {
        ArgumentNullException.ThrowIfNull(sinners);
        ArgumentNullException.ThrowIfNull(rarities);

        TypeList = new FilterListViewModel
        {
            Title = "Type",
            Items = [.. ElementOptions.DamageTypes.Select(Wrap)],
        };

        SinList = new FilterListViewModel
        {
            Title = "Sin",
            Items = [.. ElementOptions.Sins.Select(Wrap)],
        };

        SinnerList = new FilterListViewModel
        {
            Title = "Sinners",
            Items = [.. sinners.Select(name => new FilterOptionViewModel { Name = name })],
        };

        RarityList = new FilterListViewModel
        {
            Title = "Rarity",
            Items = [.. rarities.Select(name => new FilterOptionViewModel { Name = name })],
        };

        foreach (FilterListViewModel list in Lists)
        {
            foreach (FilterOptionViewModel item in list.Items)
            {
                item.PropertyChanged += OnItemChanged;
            }
        }
    }

    public FilterListViewModel TypeList { get; }

    public FilterListViewModel SinList { get; }

    public FilterListViewModel SinnerList { get; }

    public FilterListViewModel RarityList { get; }

    /// <summary>Фильтр изменился — таблицу нужно пересобрать.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Поиск по названию: строка остаётся, если название содержит набранное.
    /// Регистр не важен — искать «salsu» и «Salsu» одинаково законно.
    /// </summary>
    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsActive));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Заданы ли метки: по ним отбираются отдельные значения, а не только строки.</summary>
    public bool FiltersMarks => TypeList.Any || SinList.Any;

    public bool IsActive =>
        FiltersMarks || SinnerList.Any || RarityList.Any || _search.Length > 0;

    /// <summary>Подходит ли название строки под поиск.</summary>
    public bool AllowsName(string? name) =>
        _search.Length == 0
        || (name is not null && name.Contains(_search, StringComparison.CurrentCultureIgnoreCase));

    public bool AllowsSinner(string? name) => Allows(SinnerList, name);

    public bool AllowsRarity(string? rarity) => Allows(RarityList, rarity);

    private static bool Allows(FilterListViewModel list, string? name) =>
        !list.Any || list.Items.Any(item => item.IsSelected && item.Name == name);

    /// <summary>
    /// Подходит ли клетка по меткам. Клетка без метки под заданный фильтр не подходит:
    /// про неё неизвестно, тот ли это тип урона.
    /// </summary>
    public bool AllowsMarks(ElementOption? type, ElementOption? sin) =>
        Matches(TypeList, type) && Matches(SinList, sin);

    public void Reset()
    {
        foreach (FilterListViewModel list in Lists)
        {
            list.Clear();
        }

        Search = string.Empty;
    }

    private IEnumerable<FilterListViewModel> Lists => [TypeList, SinList, SinnerList, RarityList];

    private static bool Matches(FilterListViewModel list, ElementOption? actual) =>
        !list.Any
        || (actual is not null
            && list.Items.Any(item =>
                item.IsSelected && item.Option?.Element == actual.Element));

    private static FilterOptionViewModel Wrap(ElementOption option) => new()
    {
        Name = option.Name,
        IconPath = option.IconPath,
        Option = option,
    };

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (FilterListViewModel list in Lists)
        {
            list.Refresh();
        }

        OnPropertyChanged(nameof(IsActive));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
