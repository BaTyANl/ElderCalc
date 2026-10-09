using System.Collections.ObjectModel;

namespace LimbusCalc.ViewModels;

/// <summary>A table row as a suggestion list item.</summary>
public sealed class ExportTargetViewModel
{
    public required TableRowViewModel Row { get; init; }

    /// <summary>The item label: sinner and ID name.</summary>
    public required string Display { get; init; }

    public override string ToString() => Display;
}

/// <summary>
/// Where the calculator setup goes: a table row and a skill column.
/// </summary>
public sealed class ExportToTableViewModel : ObservableObject
{
    private ExportTargetViewModel? _selectedTarget;
    private TableColumn? _selectedSkill;
    private string _search = string.Empty;

    /// <summary>Window title: which table we export to.</summary>
    public required string Caption { get; init; }

    /// <summary>All named rows of the table; the search narrows the list.</summary>
    public required IReadOnlyList<ExportTargetViewModel> AllTargets { get; init; }

    /// <summary>What the list shows right now.</summary>
    public ObservableCollection<ExportTargetViewModel> Targets { get; } = [];

    public required IReadOnlyList<TableColumn> Skills { get; init; }

    /// <summary>
    /// Search over the item label. It contains both the ID name and the sinner, so either
    /// can be typed — otherwise there's no finding anything among a hundred rows.
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

    /// <summary>Nothing to save until both the row and the skill are chosen.</summary>
    public bool CanSave => SelectedTarget is not null && SelectedSkill is not null;

    /// <summary>The search found nothing (as opposed to the table being empty).</summary>
    public bool NothingFound => Targets.Count == 0 && AllTargets.Count > 0;

    /// <summary>The cell that receives the export.</summary>
    public TableCell? TargetCell =>
        SelectedTarget is null || SelectedSkill is null
            ? null
            : SelectedTarget.Row.CellOf(SelectedSkill);

    /// <summary>Builds the list from the table's rows; unnamed rows are skipped.</summary>
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

            // E.G.O. kind: Awakening or Corrosion. The ID table has no such column.
            string kind = row.CellOf("Type")?.Value.Trim() ?? string.Empty;

            string label = sinner.Length == 0 ? name : $"{sinner} — {name}";

            targets.Add(new ExportTargetViewModel
            {
                Row = row,
                // The same name appears under different sinners, so the label has both:
                // otherwise "LCB Sinner" would show up twelve times in a row. An E.G.O.
                // also shows its kind, since its awakening and corrosion are separate rows.
                Display = kind.Length == 0 ? label : $"{label} ({kind})",
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
    /// Rebuilds the shown list. The chosen row is kept if it still matches the search;
    /// otherwise every typed letter would drop a choice already made.
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

        // With a single match left there's nothing to choose between, so it's selected
        // automatically. The previous choice is kept while it still matches.
        SelectedTarget =
            chosen is not null && Targets.Contains(chosen) ? chosen
            : _search.Length > 0 && Targets.Count == 1 ? Targets[0]
            : null;

        OnPropertyChanged(nameof(NothingFound));
    }
}
