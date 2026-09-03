using System.Collections.ObjectModel;

namespace LimbusCalc.ViewModels;

/// <summary>Строка справочника как пункт выпадающего списка.</summary>
public sealed class ExportTargetViewModel
{
    public required TableRowViewModel Row { get; init; }

    /// <summary>Подпись пункта: грешник и название айди.</summary>
    public required string Display { get; init; }

    public override string ToString() => Display;
}

/// <summary>
/// Куда выгружать набор калькулятора: строка справочника и столбец скилла.
/// </summary>
public sealed class ExportToTableViewModel : ObservableObject
{
    private ExportTargetViewModel? _selectedTarget;
    private TableColumn? _selectedSkill;
    private string _search = string.Empty;

    /// <summary>Заголовок окна: в какую именно таблицу выгружаем.</summary>
    public required string Caption { get; init; }

    /// <summary>Все именованные строки справочника; список сужается поиском.</summary>
    public required IReadOnlyList<ExportTargetViewModel> AllTargets { get; init; }

    /// <summary>Что показывать в списке сейчас.</summary>
    public ObservableCollection<ExportTargetViewModel> Targets { get; } = [];

    public required IReadOnlyList<TableColumn> Skills { get; init; }

    /// <summary>
    /// Поиск по подписи пункта. В ней и название айди, и грешник, поэтому набрать
    /// можно любое из двух — в справочнике на сотню строк иначе не найтись.
    /// </summary>
    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
            {
                ApplySearch();
            }
        }
    }

    public ExportTargetViewModel? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public TableColumn? SelectedSkill
    {
        get => _selectedSkill;
        set
        {
            if (SetProperty(ref _selectedSkill, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    /// <summary>Пока не выбраны и айди, и скилл, сохранять некуда.</summary>
    public bool CanSave => SelectedTarget is not null && SelectedSkill is not null;

    /// <summary>Ничего не нашлось: список пуст не потому, что справочник пуст.</summary>
    public bool NothingFound => Targets.Count == 0 && AllTargets.Count > 0;

    /// <summary>Клетка, в которую пойдёт выгрузка.</summary>
    public TableCell? TargetCell =>
        SelectedTarget is null || SelectedSkill is null
            ? null
            : SelectedTarget.Row.CellOf(SelectedSkill);

    /// <summary>Собирает список из строк справочника; безымянные строки пропускаем.</summary>
    public static ExportToTableViewModel Create(TableViewModel table)
    {
        ArgumentNullException.ThrowIfNull(table);

        List<ExportTargetViewModel> targets = [];

        foreach (TableRowViewModel row in table.Rows)
        {
            string name = row.CellOf("Name")?.Value.Trim() ?? string.Empty;

            if (name.Length == 0)
            {
                continue;
            }

            string sinner = row.CellOf("Sinner")?.Value.Trim() ?? string.Empty;

            targets.Add(new ExportTargetViewModel
            {
                Row = row,
                // Одно и то же название встречается у разных грешников, поэтому
                // в подписи оба: иначе LCB Sinner выглядел бы двенадцать раз подряд.
                Display = sinner.Length == 0 ? name : $"{sinner} — {name}",
            });
        }

        ExportToTableViewModel model = new()
        {
            Caption = $"Export to {table.Title}",
            AllTargets = targets,
            Skills = [.. table.Columns.Where(column => column.AcceptsSetup)],
        };

        model.ApplySearch();
        return model;
    }

    /// <summary>
    /// Пересобирает показанный список. Выбранный айди сохраняем, если он прошёл поиск:
    /// иначе набранная буква сбрасывала бы уже сделанный выбор.
    /// </summary>
    private void ApplySearch()
    {
        ExportTargetViewModel? chosen = SelectedTarget;

        Targets.Clear();

        foreach (ExportTargetViewModel target in AllTargets)
        {
            if (_search.Length == 0
                || target.Display.Contains(_search, StringComparison.CurrentCultureIgnoreCase))
            {
                Targets.Add(target);
            }
        }

        // Осталась одна подсказка — выбирать между чем-то уже не из чего, и заставлять
        // ткнуть в единственную строку незачем. Прежний выбор держим, пока он подходит.
        SelectedTarget =
            chosen is not null && Targets.Contains(chosen) ? chosen
            : _search.Length > 0 && Targets.Count == 1 ? Targets[0]
            : null;

        OnPropertyChanged(nameof(NothingFound));
    }
}
