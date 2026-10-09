using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LimbusCalc.ViewModels;

/// <summary>
/// A checkable filter item: damage type, sin or sinner. Sinners have no icon,
/// so it may be empty.
/// </summary>
public sealed class FilterOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public required string Name { get; init; }

    public string? IconPath { get; init; }

    /// <summary>What exactly is selected; empty for sinners, which compare by name.</summary>
    public ElementOption? Option { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>One filter list: the popup title and its items.</summary>
public sealed class FilterListViewModel : ObservableObject
{
    public required string Title { get; init; }

    public required IReadOnlyList<FilterOptionViewModel> Items { get; init; }

    /// <summary>Button label: how many items are checked.</summary>
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
/// Filters the table's rows and values. Each list allows several checked items; an empty
/// list means "any". Type and sin are compared with the cell's marks, sinners with the
/// Sinner column.
/// </summary>
public sealed class TableFilterViewModel : ObservableObject
{
    private string _search = string.Empty;
    private bool _favoritesOnly;

    public TableFilterViewModel(
        IReadOnlyList<string> sinners,
        IReadOnlyList<string> rarities,
        IReadOnlyList<string> egoTypes,
        IReadOnlyList<string> rangeColumns)
    {
        ArgumentNullException.ThrowIfNull(sinners);
        ArgumentNullException.ThrowIfNull(rarities);
        ArgumentNullException.ThrowIfNull(egoTypes);
        ArgumentNullException.ThrowIfNull(rangeColumns);

        // "Type" in E.G.O. means Awakening/Corrosion, so the damage type is "Attack Type".
        TypeList = new FilterListViewModel
        {
            Title = "Attack Type",
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

        EgoTypeList = new FilterListViewModel
        {
            Title = "Type",
            Items = [.. egoTypes.Select(name => new FilterOptionViewModel { Name = name })],
        };

        foreach (FilterListViewModel list in Lists)
        {
            foreach (FilterOptionViewModel item in list.Items)
            {
                item.PropertyChanged += OnItemChanged;
            }
        }

        Ranges = [.. rangeColumns.Select(key => new RangeFilterViewModel { Key = key })];

        foreach (RangeFilterViewModel range in Ranges)
        {
            range.Changed += (_, _) => OnRangeChanged();
        }
    }

    /// <summary>Value ranges, one per numeric column; empty bounds mean "no limit".</summary>
    public IReadOnlyList<RangeFilterViewModel> Ranges { get; }

    /// <summary>Range button label: how many ranges are set.</summary>
    public string RangeLabel
    {
        get
        {
            int count = Ranges.Count(range => range.IsSet);

            return count == 0 ? "Range: any" : $"Range: {count}";
        }
    }

    /// <summary>Show only favorite rows.</summary>
    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (SetProperty(ref _favoritesOnly, value))
            {
                OnPropertyChanged(nameof(IsActive));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Whether a row's values fit every set range; <paramref name="valueOf"/> reads a column by key.</summary>
    public bool AllowsRanges(Func<string, double?> valueOf)
    {
        ArgumentNullException.ThrowIfNull(valueOf);

        foreach (RangeFilterViewModel range in Ranges)
        {
            if (range.IsSet && !range.Allows(valueOf(range.Key)))
            {
                return false;
            }
        }

        return true;
    }

    private void OnRangeChanged()
    {
        OnPropertyChanged(nameof(RangeLabel));
        OnPropertyChanged(nameof(IsActive));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public FilterListViewModel TypeList { get; }

    public FilterListViewModel SinList { get; }

    public FilterListViewModel SinnerList { get; }

    public FilterListViewModel RarityList { get; }

    /// <summary>E.G.O. kind: Awakening or Corrosion. Filters whole rows.</summary>
    public FilterListViewModel EgoTypeList { get; }

    /// <summary>The filter changed and the table must be filtered again.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Name search: a row stays if its name contains the text. Case-insensitive —
    /// "salsu" and "Salsu" are equally valid.
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

    /// <summary>Whether marks are filtered: they select individual values, not just rows.</summary>
    public bool FiltersMarks => TypeList.Any || SinList.Any;

    /// <summary>Whether any filter or the search is set.</summary>
    public bool IsActive =>
        FiltersMarks || SinnerList.Any || RarityList.Any || EgoTypeList.Any || _search.Length > 0
        || _favoritesOnly || Ranges.Any(range => range.IsSet);

    /// <summary>Whether the row's name matches the search.</summary>
    public bool AllowsName(string? name) =>
        _search.Length == 0
        || (name is not null && name.Contains(_search, StringComparison.CurrentCultureIgnoreCase));

    public bool AllowsSinner(string? name) => Allows(SinnerList, name);

    public bool AllowsRarity(string? rarity) => Allows(RarityList, rarity);

    public bool AllowsEgoType(string? type) => Allows(EgoTypeList, type);

    private static bool Allows(FilterListViewModel list, string? name) =>
        !list.Any || list.Items.Any(item => item.IsSelected && item.Name == name);

    /// <summary>
    /// Whether a cell matches by marks. A cell without a mark fails an active filter:
    /// there's no telling whether it is that damage type.
    /// </summary>
    public bool AllowsMarks(ElementOption? type, ElementOption? sin) =>
        Matches(TypeList, type) && Matches(SinList, sin);

    /// <summary>Checked items of each list by its title — to remember between launches.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Selections =>
        Lists.ToDictionary(
            list => list.Title,
            list => (IReadOnlyList<string>)[.. list.Items.Where(item => item.IsSelected).Select(item => item.Name)]);

    /// <summary>
    /// Restores a remembered filter. Items that no longer exist are skipped, and lists
    /// not mentioned stay empty.
    /// </summary>
    public void Restore(IReadOnlyDictionary<string, IReadOnlyList<string>> selections, string search)
    {
        ArgumentNullException.ThrowIfNull(selections);

        foreach (FilterListViewModel list in Lists)
        {
            IReadOnlyList<string> names = selections.TryGetValue(list.Title, out IReadOnlyList<string>? stored) ? stored : [];

            foreach (FilterOptionViewModel item in list.Items)
            {
                item.IsSelected = names.Contains(item.Name);
            }
        }

        Search = search ?? string.Empty;
    }

    public void Reset()
    {
        foreach (FilterListViewModel list in Lists)
        {
            list.Clear();
        }

        foreach (RangeFilterViewModel range in Ranges)
        {
            range.Clear();
        }

        FavoritesOnly = false;
        Search = string.Empty;
    }

    private IEnumerable<FilterListViewModel> Lists =>
        [TypeList, SinList, SinnerList, RarityList, EgoTypeList];

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

/// <summary>
/// A value range on one numeric column: rows outside it are hidden. A row without a value
/// in the column doesn't fit a set range — there's nothing to compare.
/// </summary>
public sealed class RangeFilterViewModel : ObservableObject
{
    private string _min = string.Empty;
    private string _max = string.Empty;

    /// <summary>Key of the column; also the label, since the four DPSC columns share a title.</summary>
    public required string Key { get; init; }

    /// <summary>The lower bound as typed; empty or not a number means no bound.</summary>
    public string Min
    {
        get => _min;
        set => SetBound(ref _min, value);
    }

    /// <summary>The upper bound as typed; empty or not a number means no bound.</summary>
    public string Max
    {
        get => _max;
        set => SetBound(ref _max, value);
    }

    public bool IsSet => Parse(_min) is not null || Parse(_max) is not null;

    internal event EventHandler? Changed;

    public bool Allows(double? value)
    {
        double? min = Parse(_min);
        double? max = Parse(_max);

        if (min is null && max is null)
        {
            return true;
        }

        return value is double actual && (min is null || actual >= min) && (max is null || actual <= max);
    }

    internal void Clear()
    {
        Min = string.Empty;
        Max = string.Empty;
    }

    private void SetBound(ref string field, string? value)
    {
        if (SetProperty(ref field, value ?? string.Empty))
        {
            OnPropertyChanged(nameof(IsSet));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Accepts both a dot and a comma as the decimal separator.</summary>
    private static double? Parse(string text) =>
        double.TryParse(
            text.Trim().Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double value)
            ? value
            : null;
}
