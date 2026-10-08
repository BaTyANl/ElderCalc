using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LimbusCalc.Calculation;
using LimbusCalc.Storage;
using Microsoft.Win32;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;
using LimbusCalc.Views;

namespace LimbusCalc
{
    /// <summary>
    /// The main window: the ID and E.G.O. reference tables and the calculator. The calculation
    /// itself lives in <see cref="MainViewModel"/>; this class handles table editing, saving,
    /// file dialogs and the view state between launches.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel = new();

        /// <summary>App settings: theme, cell look and outlines.</summary>
        private readonly SettingsViewModel _settings;

        /// <summary>The reference tables and the files they are stored in.</summary>
        private readonly Dictionary<TableViewModel, string> _tableFiles;

        /// <summary>Tables changed since the last save; other files are left alone.</summary>
        private readonly HashSet<TableViewModel> _changedTables = [];

        /// <summary>
        /// Delays saving tables: while typing every key is an edit, and writing the file after
        /// each one is pointless.
        /// </summary>
        private readonly DispatcherTimer _tableSaveTimer = new()
        {
            Interval = TimeSpan.FromSeconds(1.5),
        };

        /// <summary>Background save queue; each save waits for the previous one.</summary>
        private Task _saving = Task.CompletedTask;

        /// <summary>
        /// Number of the latest edit of each table. If the number grew after a save finished,
        /// the disk doesn't hold the latest content yet and it's too early to say "Saved".
        /// </summary>
        private readonly Dictionary<TableViewModel, int> _versions = [];

        /// <summary>
        /// Tables whose last save failed, and why. Written from the background, read on exit
        /// while the UI thread waits for the save queue and isn't processing messages.
        /// </summary>
        private readonly Dictionary<TableViewModel, string> _saveFailures = [];

        /// <summary>
        /// The calculator setup that is already stored somewhere: in a file, in a table cell,
        /// or just loaded from one. If the current setup differs, there's something to lose.
        /// </summary>
        private string _calculatorKept;

        /// <summary>The view from the last launch: window, tab, table sorting and filters.</summary>
        private readonly ViewState _view = ViewState.Load();

        private SubtargetsWindow? _subtargetsWindow;

        private DamageByTargetWindow? _damageByTargetWindow;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = _viewModel;

            // Restore the user's choice before the window shows so the theme doesn't flash,
            // and set the outline brushes now, otherwise cells would be drawn without them.
            ThemeManager.Apply(AppSettings.LoadTheme());
            _settings = new SettingsViewModel();
            _settings.Apply();

            RestoreWindow();

            _tableFiles = new Dictionary<TableViewModel, string>
            {
                [_viewModel.IdTable] = TableStorage.IdFileName,
                [_viewModel.EgoTable] = TableStorage.EgoFileName,
            };

            // Tables are read once the window is shown: a file of a couple of megabytes would
            // otherwise keep the startup on a blank screen. Until then a cover hides them.
            foreach (TableViewModel table in _tableFiles.Keys)
            {
                table.IsLoading = true;
            }

            ContentRendered += LoadTablesOnce;
            _tableSaveTimer.Tick += (_, _) => SaveTables();

            // The calculator starts empty: there's nothing to lose in a blank setup.
            _calculatorKept = CalculatorState();
        }

        /// <summary>
        /// Reads the tables in the background and puts them in place. Saving is hooked up only
        /// after that: writing back what was just read is pointless, and doing it earlier is risky.
        /// </summary>
        private async void LoadTablesOnce(object? sender, EventArgs e)
        {
            ContentRendered -= LoadTablesOnce;

            Dictionary<TableViewModel, Task<TableLoadResult>> reading = _tableFiles.ToDictionary(
                pair => pair.Key,
                pair => Task.Run(() => TableStorage.Read(pair.Value)));

            List<string> problems = [];

            foreach ((TableViewModel table, Task<TableLoadResult> task) in reading)
            {
                TableLoadResult result;

                try
                {
                    result = await task;
                }
                catch (Exception error)
                {
                    // Only the unexpected ends up here: Read handles reading errors itself.
                    // There's no telling what happened to the file, so it won't be written to.
                    result = new TableLoadResult(
                        TableLoadOutcome.Failed, TableData.Empty, error.Message, null, CanSave: false);
                }

                TableStorage.Apply(table, result.Data);

                // The view comes after the rows are read: there's nothing to sort before that.
                // And before subscribing to edits: re-sorting is no reason to rewrite the file.
                if (_view.Tables.TryGetValue(table.Title, out TableViewState? state))
                {
                    state.ApplyTo(table);
                }

                string? problem = Describe(table, result);

                if (result.CanSave)
                {
                    table.Changed += OnTableChanged;
                }
                else
                {
                    table.SaveState = TableSaveState.Off;
                    table.SaveProblem = problem ?? string.Empty;
                }

                if (problem is not null)
                {
                    problems.Add(problem);
                }

                table.IsLoading = false;
            }

            if (problems.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine + Environment.NewLine, problems),
                    "Table file problem",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>What to tell the user about reading a table; null when everything is fine.</summary>
        private string? Describe(TableViewModel table, TableLoadResult result)
        {
            string path = TableStorage.PathOf(_tableFiles[table]);

            return result.Outcome switch
            {
                TableLoadOutcome.RestoredFromBackup =>
                    $"The {table.Title} table file could not be read ({result.Problem}). "
                    + "The previous version was restored from the backup copy. "
                    + $"The damaged file was kept as {result.BrokenCopy}.",

                TableLoadOutcome.Failed when result.CanSave =>
                    $"The {table.Title} table file could not be read ({result.Problem}), "
                    + "and there was no readable backup copy. The table starts empty. "
                    + $"The damaged file was kept as {result.BrokenCopy}.",

                TableLoadOutcome.Failed =>
                    $"The {table.Title} table file could not be read ({result.Problem}), "
                    + "and it could not be set aside either. So that nothing is lost, changes "
                    + $"to this table will not be saved until the app is restarted. File: {path}",

                _ => null,
            };
        }

        private void OnTableChanged(object? sender, EventArgs e)
        {
            if (sender is TableViewModel table)
            {
                _changedTables.Add(table);
                _versions[table] = _versions.GetValueOrDefault(table) + 1;
                table.SaveState = TableSaveState.Saving;
            }

            _tableSaveTimer.Stop();
            _tableSaveTimer.Start();
        }

        /// <summary>
        /// The table snapshot is taken on the UI thread in a fraction of a millisecond; the write
        /// itself happens in the background, so the table stays usable while megabytes are written.
        /// </summary>
        private void SaveTables()
        {
            _tableSaveTimer.Stop();

            foreach (TableViewModel table in _changedTables)
            {
                QueueSave(table, TableStorage.Snapshot(table), _versions.GetValueOrDefault(table));
            }

            _changedTables.Clear();
        }

        /// <summary>
        /// Saves run strictly one after another: otherwise an older snapshot could land on top
        /// of a newer one written a moment earlier.
        /// </summary>
        private void QueueSave(TableViewModel table, TableSnapshot snapshot, int version)
        {
            string file = _tableFiles[table];

            _saving = _saving.ContinueWith(
                _ =>
                {
                    try
                    {
                        TableStorage.Save(file, snapshot);

                        lock (_saveFailures)
                        {
                            _saveFailures.Remove(table);
                        }

                        Dispatcher.BeginInvoke(() => ReportSaved(table, version));
                    }
                    catch (Exception error)
                    {
                        lock (_saveFailures)
                        {
                            _saveFailures[table] = error.Message;
                        }

                        Dispatcher.BeginInvoke(() => ReportSaveError(table, error));
                    }
                },
                TaskScheduler.Default);
        }

        /// <summary>Saved. Shows "Saved" only if the table wasn't edited after this snapshot.</summary>
        private void ReportSaved(TableViewModel table, int version)
        {
            if (_versions.GetValueOrDefault(table) == version && !_changedTables.Contains(table))
            {
                table.SaveState = TableSaveState.Saved;
                table.SaveProblem = string.Empty;
            }
        }

        /// <summary>
        /// The save failed. The previous file is intact — the swap happens only after a complete
        /// write. The table is marked changed so the save is retried. Work isn't interrupted with
        /// a dialog: the indicator above the table turns red, and the user is asked on exit.
        /// </summary>
        private void ReportSaveError(TableViewModel table, Exception error)
        {
            _changedTables.Add(table);
            table.SaveState = TableSaveState.Failed;
            table.SaveProblem = error.Message;
        }

        /// <summary>Click on the save indicator: explains what's wrong and offers to retry.</summary>
        private void SaveStatus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableViewModel table })
            {
                return;
            }

            switch (table.SaveState)
            {
                case TableSaveState.Failed:
                    bool retry = ConfirmWindow.Ask(
                        this,
                        "Not saved",
                        $"The {table.Title} table could not be saved: {table.SaveProblem}"
                            + Environment.NewLine + Environment.NewLine
                            + "The previous version of the file is intact. Saving is retried "
                            + "after the next change and when the app closes.",
                        "Try again");

                    if (retry)
                    {
                        _changedTables.Add(table);
                        table.SaveState = TableSaveState.Saving;
                        SaveTables();
                    }

                    break;

                case TableSaveState.Off:
                    MessageBox.Show(
                        this,
                        table.SaveProblem,
                        "Saving off",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    break;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            SaveView();

            // An edit may not have been saved by the timer yet — save it on exit and wait for
            // the write to finish, otherwise the process would cut it off midway.
            SaveTables();
            _saving.Wait(TimeSpan.FromSeconds(15));

            List<string> failures;

            lock (_saveFailures)
            {
                failures = [.. _saveFailures.Select(pair => $"{pair.Key.Title}: {pair.Value}")];
            }

            if (failures.Count == 0)
            {
                return;
            }

            // The latest edits didn't reach the disk. Closing silently would lose them.
            bool close = ConfirmWindow.Ask(
                this,
                "Changes not saved",
                "The latest changes could not be saved:"
                    + Environment.NewLine + string.Join(Environment.NewLine, failures)
                    + Environment.NewLine + Environment.NewLine
                    + "If you close now, they will be lost. The previous version of the file is intact.",
                "Close anyway");

            e.Cancel = !close;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // The window handle exists only now; the title bar can't be themed earlier.
            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }

        /// <summary>
        /// Reference table buttons. The table comes from the button's own binding: ID and
        /// E.G.O. share the markup but have different models.
        /// </summary>
        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableViewModel table } element)
            {
                return;
            }

            EndEdit();

            TableRowViewModel row = table.AddRow();

            // Filters almost always hide an empty row — but it was just added to be filled in.
            if (!row.IsVisible)
            {
                table.Filter.Reset();
            }

            EditName(element, table, row);
        }

        /// <summary>
        /// Opens the name editor of a freshly added row so it can be named right away.
        /// The row may be far down; it scrolls into view.
        /// </summary>
        private void EditName(DependencyObject element, TableViewModel table, TableRowViewModel row)
        {
            if (RowsOf(element, table) is ItemsControl rows && row.CellOf("Name") is TableCell name)
            {
                ShowCell(rows, row, name, openList: false);
            }
        }

        /// <summary>The copy goes below the row with its name editor open, ready for renaming.</summary>
        private void DuplicateRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem
                {
                    Parent: ContextMenu
                    {
                        DataContext: TableRowViewModel row,
                        Tag: TableViewModel table,
                        PlacementTarget: UIElement placement,
                    },
                })
            {
                return;
            }

            EndEdit();
            EditName(placement, table, table.Duplicate(row));
        }

        /// <summary>
        /// Clears the whole table. It can be undone, but a slip is costly, so the user is asked
        /// first and told how many rows will go.
        /// </summary>
        private void ClearTable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableViewModel table }
                || table.Rows.Count == 0)
            {
                return;
            }

            bool confirmed = ConfirmWindow.Ask(
                this,
                $"Clear {table.Title}",
                $"All {table.Rows.Count} rows will be removed, together with the setups stored in their cells. You can bring them back with Undo (Ctrl+Z).",
                "Clear table");

            if (confirmed)
            {
                table.Clear();
            }
        }

        /// <summary>
        /// Right click on a row. The cell is found by the click position: damage cells have their
        /// own menu, computed and filtered-out cells have none, other cells get the row menu.
        /// </summary>
        private void RowView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not TableRowView view
                || view.CellAt(Mouse.GetPosition(view), out _) is not TableCell cell)
            {
                return;
            }

            e.Handled = true;

            if (cell.Column.Kind == TableCellKind.Computed || !cell.IsVisible)
            {
                return;
            }

            // A cell like Sin Cost has nothing to offer — no setup, no marks — so it gets the row menu.
            if (cell.HasSetup || cell.CanEditMarks)
            {
                OpenCellMenu(cell, view);
            }
            else
            {
                OpenRowMenu(view);
            }
        }

        /// <summary>The row menu. The row and the table are taken from the tree above the element.</summary>
        private void OpenRowMenu(FrameworkElement element)
        {
            if (DataOf<TableRowViewModel>(element) is not TableRowViewModel row
                || DataOf<TableViewModel>(element) is not TableViewModel table)
            {
                return;
            }

            ContextMenu menu = (ContextMenu)FindResource("RowMenu");

            menu.DataContext = row;
            menu.Tag = table;
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }

        /// <summary>The nearest data context of the given type up the visual tree.</summary>
        private static T? DataOf<T>(DependencyObject start)
            where T : class
        {
            for (DependencyObject? node = start; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is FrameworkElement { DataContext: T found })
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Deletes a row together with the setups stored in its cells. A slip is easy, so the user
        /// is asked first and told what is being deleted. Undo brings everything back.
        /// </summary>
        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem
                {
                    Parent: ContextMenu { DataContext: TableRowViewModel row, Tag: TableViewModel table },
                })
            {
                return;
            }

            string name = row.CellOf("Name")?.Value.Trim() ?? string.Empty;
            string sinner = row.CellOf("Sinner")?.Value.Trim() ?? string.Empty;

            string label =
                name.Length == 0 ? "This row"
                : sinner.Length == 0 ? $"“{name}”"
                : $"“{sinner} — {name}”";

            bool confirmed = ConfirmWindow.Ask(
                this,
                "Delete row",
                $"{label} will be removed, together with the setups stored in its cells. You can bring it back with Undo (Ctrl+Z).",
                "Delete row");

            if (confirmed)
            {
                table.Remove(row);
            }
        }

        /// <summary>
        /// The viewport size changed: recalculate the stretching column. ScrollChanged alone
        /// isn't enough: on the first layout it arrives before the real width is known.
        /// </summary>
        private void TableScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ScrollViewer { DataContext: TableViewModel table } viewer)
            {
                viewer.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    () => table.UpdateColumnWidths(VisibleWidth(viewer)));
            }
        }

        /// <summary>
        /// How much width the columns get. ViewportWidth of a virtualizing scroll viewer doesn't
        /// report the full available width, so take the viewer's own size and subtract the
        /// scrollbar when it's shown.
        /// </summary>
        private static double VisibleWidth(ScrollViewer viewer)
        {
            double scrollbar = viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible
                ? SystemParameters.VerticalScrollBarWidth
                : 0.0;

            return viewer.ActualWidth - scrollbar;
        }

        /// <summary>
        /// The header and the averages row sit outside the rows' scroll viewer, so they are moved
        /// horizontally by hand — otherwise the titles would drift away from their columns.
        /// Any scroll also closes the cell editor and the cell tooltip.
        /// </summary>
        private void TableScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not DependencyObject body)
            {
                return;
            }

            // The stretching column is sized from the rows' viewport: the header and averages
            // have their own, and using it would misalign the columns by the scrollbar width.
            if (e.ViewportWidthChange != 0.0
                && sender is ScrollViewer { DataContext: TableViewModel table } viewer)
            {
                table.UpdateColumnWidths(VisibleWidth(viewer));
            }

            // The editor sits over the cell and doesn't move with it: after a scroll it would be
            // over someone else's cell. Close it — the value is already written.
            if (e.VerticalChange != 0.0 || e.HorizontalChange != 0.0)
            {
                EndEdit();
                TableRowView.HideTip();
            }

            if (e.HorizontalChange == 0.0)
            {
                return;
            }

            // The table template exists twice, for ID and E.G.O., so instead of a name lookup
            // search next to ourselves: the header and averages share a panel with the rows.
            foreach (ScrollViewer paired in Siblings<ScrollViewer>(body))
            {
                if (paired.Tag as string == "TableSyncScroll")
                {
                    paired.ScrollToHorizontalOffset(e.HorizontalOffset);
                }
            }
        }

        private static IEnumerable<T> Siblings<T>(DependencyObject element)
            where T : DependencyObject
        {
            // Look for the table's own panel: the rows have wrappers of their own, while the
            // header and averages sit one level up, in the shared dock panel.
            DependencyObject? parent = VisualTreeHelper.GetParent(element);

            while (parent is not null and not DockPanel)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            return parent is null ? [] : Descendants<T>(parent);
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);

            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                if (child is T found)
                {
                    yield return found;
                }

                foreach (T nested in Descendants<T>(child))
                {
                    yield return nested;
                }
            }
        }

        /// <summary>
        /// Opens a filter list. The button itself tells which one: the list is in its Tag,
        /// so one popup serves all of them.
        /// </summary>
        private void FilterList_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: FilterListViewModel list } element)
            {
                return;
            }

            FilterPopup.IsOpen = false;
            FilterPopup.DataContext = list;
            FilterPopup.PlacementTarget = element;
            FilterPopup.IsOpen = true;
        }

        /// <summary>Opens the columns list next to the button that was clicked.</summary>
        private void Columns_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableViewModel table } element)
            {
                return;
            }

            EndEdit();

            ColumnsPopup.IsOpen = false;
            ColumnsPopup.DataContext = table;
            ColumnsPopup.PlacementTarget = element;
            ColumnsPopup.IsOpen = true;
        }

        private void ShowAllColumns_Click(object sender, RoutedEventArgs e)
        {
            if (ColumnsPopup.DataContext is TableViewModel table)
            {
                table.ShowAllColumns();
            }
        }

        private void HideAllColumns_Click(object sender, RoutedEventArgs e)
        {
            if (ColumnsPopup.DataContext is TableViewModel table)
            {
                EndEdit();
                table.HideAllColumns();
            }
        }

        /// <summary>
        /// Puts the window where it was. If that screen is gone — say, a second monitor was
        /// unplugged — the window stays centered, as on first launch.
        /// </summary>
        private void RestoreWindow()
        {
            if (_view.Tab is int tab && tab >= 0 && tab < Tabs.Items.Count)
            {
                Tabs.SelectedIndex = tab;
            }

            if (_view.Window is not WindowViewState saved
                || saved.Width < MinWidth
                || saved.Height < MinHeight)
            {
                return;
            }

            Rect screen = new(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            Rect visible = Rect.Intersect(screen, new Rect(saved.Left, saved.Top, saved.Width, saved.Height));

            // Enough of it must be visible to grab the title bar.
            if (visible.IsEmpty || visible.Width < 120 || visible.Height < 60)
            {
                return;
            }

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left;
            Top = saved.Top;
            Width = saved.Width;
            Height = saved.Height;
            WindowState = saved.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        /// <summary>Remembers the view on exit. The size is the normal window size, even when maximized.</summary>
        private void SaveView()
        {
            Rect bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            if (!bounds.IsEmpty)
            {
                _view.Window = new WindowViewState
                {
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Maximized = WindowState == WindowState.Maximized,
                };
            }

            _view.Tab = Tabs.SelectedIndex;

            // A table that never finished loading is skipped: its view stays as it was.
            foreach (TableViewModel table in _tableFiles.Keys.Where(table => !table.IsLoading))
            {
                _view.Tables[table.Title] = TableViewState.Capture(table);
            }

            _view.Save();
        }

        private void FilterReset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableFilterViewModel filter })
            {
                filter.Reset();
            }
        }

        /// <summary>
        /// Export buttons on the calculator tab: the user picks a row and a skill, the cell
        /// gets the total damage and keeps the setup.
        /// </summary>
        private void ExportToId_Click(object sender, RoutedEventArgs e) =>
            ExportToTable(_viewModel.IdTable);

        private void ExportToEgo_Click(object sender, RoutedEventArgs e) =>
            ExportToTable(_viewModel.EgoTable);

        /// <summary>
        /// Puts the current setup into a table cell: the user picks the row by name and the
        /// column it goes to.
        /// </summary>
        private void ExportToTable(TableViewModel table)
        {
            if (table.IsLoading)
            {
                MessageBox.Show(
                    this,
                    $"The {table.Title} table is still loading. Try again in a moment.",
                    $"Export to {table.Title}",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ExportToTableViewModel selection = ExportToTableViewModel.Create(table);

            if (selection.AllTargets.Count == 0)
            {
                MessageBox.Show(
                    this,
                    $"The {table.Title} table has no named rows yet. Add a row and fill in Name first.",
                    selection.Caption,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ExportToTableWindow window = new(selection) { Owner = this };

            if (window.ShowDialog() != true || selection.TargetCell is not TableCell cell)
            {
                return;
            }

            EndEdit();

            string setup = CalculatorState();

            // Number, marks and setup are one edit and are undone together.
            using (table.BeginCellEdit(cell, $"Export to {cell.Column.Title}"))
            {
                cell.Value = _viewModel.Total.ToString("0.##", CultureInfo.InvariantCulture);

                // Type and sin come from the setup itself: the table shows them as icons, so
                // there's no need to set them separately after an export.
                cell.SkillType = _viewModel.SkillType;
                cell.SkillSin = _viewModel.SkillSin;
                cell.Setup = setup;
            }

            _calculatorKept = setup;
        }

        /// <summary>The calculator setup as a string, for telling whether anything would be lost.</summary>
        private string CalculatorState() => SetupFile.ToJson(_viewModel).ToJsonString();

        /// <summary>
        /// The calculator setup is about to be replaced. If the current one isn't stored anywhere,
        /// ask first: there would be nowhere to get it back from.
        /// </summary>
        private bool MayReplaceCalculator(string source)
        {
            if (CalculatorState() == _calculatorKept)
            {
                return true;
            }

            return ConfirmWindow.Ask(
                this,
                "Replace calculator setup",
                "The calculator has changes that are not saved to a file or to a table cell. "
                    + $"They will be replaced by the setup from {source}.",
                "Replace");
        }

        /// <summary>
        /// The damage cell menu. There's one for the whole window: creating one per cell with
        /// submenus and images was the most expensive part of scrolling.
        /// </summary>
        private void OpenCellMenu(TableCell cell, FrameworkElement placement)
        {
            ContextMenu menu = (ContextMenu)FindResource("CellMenu");

            menu.DataContext = cell;
            menu.PlacementTarget = placement;
            menu.IsOpen = true;
        }

        /// <summary>The cell being edited and the layer with its editor.</summary>
        private TableCell? _editingCell;

        private Border? _editorHost;

        /// <summary>The cell edit for the history: everything typed in the editor is one undo step.</summary>
        private CellEdit? _cellEdit;

        /// <summary>
        /// Left click on a row: the cell is found by the click position. Computed cells aren't
        /// edited, and a cell hidden by the filter is blank on screen — nothing to edit there.
        /// </summary>
        private void RowView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TableRowView view
                && view.CellAt(e.GetPosition(view), out Rect bounds) is TableCell cell
                && cell.Column.Kind != TableCellKind.Computed
                && cell.IsVisible)
            {
                BeginEdit(cell, view, bounds);
            }
        }

        /// <summary>
        /// An editor appears over the cell — one per table: an editor in every cell is costly with
        /// hundreds on screen. Cell bounds are in <paramref name="element"/> coordinates.
        /// </summary>
        private void BeginEdit(TableCell cell, FrameworkElement element, Rect bounds, bool openList = true)
        {
            if (ReferenceEquals(_editingCell, cell))
            {
                return;
            }

            EndEdit();

            if (EditorLayer(element) is not Canvas layer
                || Tagged<Border>(layer, "CellEditorHost") is not Border host
                || Tagged<ContentControl>(host, "CellEditorContent") is not ContentControl slot
                || TableOf(element) is not TableViewModel table)
            {
                return;
            }

            string template = cell.Column.Kind switch
            {
                TableCellKind.Integer when !cell.Column.AcceptsSetup => "PlainNumberEditorTemplate",
                TableCellKind.Integer => "IntegerEditorTemplate",
                TableCellKind.Options => "OptionsEditorTemplate",
                _ => "TextEditorTemplate",
            };

            Rect box = element.TransformToVisual(layer).TransformBounds(bounds);

            Canvas.SetLeft(host, box.X);
            Canvas.SetTop(host, box.Y);
            host.Width = box.Width;
            host.Height = box.Height;

            // The editor's font matches the cell underneath: the column's own or the table's.
            if (cell.Column.FontSize is double size)
            {
                slot.FontSize = size;
            }
            else
            {
                slot.ClearValue(FontSizeProperty);
            }

            slot.ContentTemplate = (DataTemplate)FindResource(template);
            slot.Content = cell;
            host.Visibility = Visibility.Visible;

            _editingCell = cell;
            _editorHost = host;
            _cellEdit = table.BeginCellEdit(cell);

            // The editor appears after layout; that's when it gets keyboard focus.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FocusEditor(slot, openList)));
        }

        /// <summary>
        /// Gives keyboard focus to the editor that was just placed. A list opens only on a click:
        /// when moving through cells with the keyboard it's opened with F4 or Alt+Down.
        /// </summary>
        private void FocusEditor(DependencyObject slot, bool openList)
        {
            switch (FirstChild<Control>(slot))
            {
                case TextBox box:
                    box.Focus();
                    box.SelectAll();
                    break;

                case ComboBox chooser when !openList:
                    chooser.Focus();
                    break;

                case ComboBox chooser:
                    chooser.Focus();

                    // Opening before the list is in place doesn't work: the popup captures the
                    // mouse and closes again immediately.
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        new Action(() => chooser.IsDropDownOpen = true));
                    break;
            }
        }

        /// <summary>The first matching element down the visual tree.</summary>
        private static T? FirstChild<T>(DependencyObject root, Func<T, bool>? match = null)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);

            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);

                if (child is T found && (match is null || match(found)))
                {
                    return found;
                }

                if (FirstChild(child, match) is T deeper)
                {
                    return deeper;
                }
            }

            return null;
        }

        /// <summary>
        /// The rows list of the table the element belongs to. Goes up to the table template's
        /// root — above it the binding is no longer to this table — and searches down from there.
        /// </summary>
        private static ItemsControl? RowsOf(DependencyObject element, TableViewModel table)
        {
            DependencyObject root = element;

            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is FrameworkElement { DataContext: TableViewModel owner } && ReferenceEquals(owner, table))
                {
                    root = node;
                }
            }

            return FirstChild<ItemsControl>(root, list => ReferenceEquals(list.ItemsSource, table.Rows));
        }

        /// <summary>
        /// Opens an editor for a cell that may be off screen: the row scrolls into view first,
        /// then the editor is placed over the cell.
        /// </summary>
        private void ShowCell(ItemsControl rows, TableRowViewModel row, TableCell cell, bool openList)
        {
            EndEdit();

            int index = rows.Items.IndexOf(row);

            if (index < 0)
            {
                return;
            }

            // A row far off screen doesn't exist yet: the list is virtualized.
            if (rows.ItemContainerGenerator.ContainerFromIndex(index) is null
                && FirstChild<VirtualizingStackPanel>(rows) is VirtualizingStackPanel panel)
            {
                panel.BringIndexIntoViewPublic(index);
                rows.UpdateLayout();
            }

            if (rows.ItemContainerGenerator.ContainerFromIndex(index) is not DependencyObject container
                || FirstChild<TableRowView>(container) is not TableRowView view)
            {
                return;
            }

            Rect bounds = view.BoundsOf(cell);

            if (bounds.IsEmpty)
            {
                return;
            }

            // Scroll horizontally too: the cell may be past the right edge. Run layout right
            // away, otherwise the scroll would close the editor we're about to open.
            view.BringIntoView(bounds);
            rows.UpdateLayout();

            BeginEdit(cell, view, view.BoundsOf(cell), openList);
        }

        /// <summary>Whether a cell can be edited: not computed, not hidden by the filter or column visibility.</summary>
        private static bool CanEdit(TableCell cell) =>
            cell.Column.Kind != TableCellKind.Computed && cell.IsVisible && !cell.Column.IsHidden;

        /// <summary>
        /// Moves the edit to a neighboring cell: one row up or down in the same column, or one cell
        /// left or right. Rows and cells hidden by filters, hidden columns and DPSC are skipped.
        /// With <paramref name="wrap"/> moving past the end of a row continues on the next one.
        /// </summary>
        private bool MoveEdit(int rowStep, int columnStep, bool wrap)
        {
            if (_editingCell is not { Row: TableRowViewModel row } cell
                || _editorHost is not FrameworkElement host
                || TableOf(host) is not TableViewModel table
                || RowsOf(host, table) is not ItemsControl rows)
            {
                return false;
            }

            List<TableRowViewModel> visible = [.. table.Rows.Where(item => item.IsVisible)];
            int rowIndex = visible.IndexOf(row);
            int columnIndex = IndexOf(row.Cells, cell);

            if (rowIndex < 0 || columnIndex < 0)
            {
                return false;
            }

            if (rowStep != 0)
            {
                for (int i = rowIndex + rowStep; i >= 0 && i < visible.Count; i += rowStep)
                {
                    if (visible[i].CellOf(cell.Column) is TableCell below && CanEdit(below))
                    {
                        ShowCell(rows, visible[i], below, openList: false);
                        return true;
                    }
                }

                return false;
            }

            int width = row.Cells.Count;

            while (true)
            {
                columnIndex += columnStep;

                if (columnIndex < 0 || columnIndex >= width)
                {
                    rowIndex += columnStep;

                    if (!wrap || rowIndex < 0 || rowIndex >= visible.Count)
                    {
                        return false;
                    }

                    columnIndex = columnStep > 0 ? 0 : width - 1;
                }

                TableCell next = visible[rowIndex].Cells[columnIndex];

                if (CanEdit(next))
                {
                    ShowCell(rows, visible[rowIndex], next, openList: false);
                    return true;
                }
            }
        }

        private static int IndexOf(IReadOnlyList<TableCell> cells, TableCell cell)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (ReferenceEquals(cells[i], cell))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Whether the caret is at the edge of the text, beyond which is the next cell. A freshly
        /// opened editor has all text selected, so an arrow moves to the next cell right away.
        /// </summary>
        private static bool AtEdge(Control? editor, bool left) => editor switch
        {
            TextBox box => box.SelectionLength == box.Text.Length
                || (box.SelectionLength == 0 && box.CaretIndex == (left ? 0 : box.Text.Length)),
            _ => true,
        };

        /// <summary>Removes the editor: the edit is over.</summary>
        private void EndEdit()
        {
            if (_editorHost is not null)
            {
                _editorHost.Visibility = Visibility.Collapsed;

                if (Tagged<ContentControl>(_editorHost, "CellEditorContent") is ContentControl slot)
                {
                    // Clear the template along with the content. Otherwise a cell of the same kind
                    // would get the old editor: the rarity list would open with sinners, or sized
                    // for the wrong number of items.
                    slot.Content = null;
                    slot.ContentTemplate = null;
                }
            }

            _editingCell = null;
            _editorHost = null;

            _cellEdit?.Dispose();
            _cellEdit = null;
        }

        /// <summary>
        /// Keys in the cell editor work like in spreadsheets such as Excel. Escape cancels the edit,
        /// Enter commits and moves down, Tab and Shift+Tab move sideways, Up/Down move between
        /// rows, Left/Right move to the next cell when the caret is at the edge of the text.
        /// Handled in the preview phase, otherwise the arrows and Enter would go to the field or
        /// list. While a list is open its arrows and Enter are its own: they pick an option.
        /// </summary>
        private void CellEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_editorHost is null)
            {
                return;
            }

            Control? editor = Tagged<ContentControl>(_editorHost, "CellEditorContent") is ContentControl slot
                ? FirstChild<Control>(slot)
                : null;

            bool listOpen = editor is ComboBox { IsDropDownOpen: true };
            ModifierKeys modifiers = Keyboard.Modifiers;

            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    _cellEdit?.Cancel();
                    CloseEditor();
                    break;

                case Key.Tab when modifiers is ModifierKeys.None or ModifierKeys.Shift:
                    // Nowhere to go from the edge of the table — but focus doesn't leave the table either.
                    e.Handled = true;
                    MoveEdit(0, modifiers == ModifierKeys.Shift ? -1 : 1, wrap: true);
                    break;

                case Key.Enter when !listOpen && modifiers == ModifierKeys.None:
                    e.Handled = true;

                    if (!MoveEdit(1, 0, wrap: false))
                    {
                        CloseEditor();
                    }

                    break;

                case Key.Up or Key.Down when !listOpen && modifiers == ModifierKeys.None:
                    // On a closed list the arrows would change the value; here they only move.
                    e.Handled = true;
                    MoveEdit(e.Key == Key.Down ? 1 : -1, 0, wrap: false);
                    break;

                case Key.Left or Key.Right when !listOpen && modifiers == ModifierKeys.None:
                    bool left = e.Key == Key.Left;

                    if (AtEdge(editor, left))
                    {
                        e.Handled = MoveEdit(0, left ? -1 : 1, wrap: false) || editor is ComboBox;
                    }

                    break;
            }
        }

        /// <summary>
        /// Closes the editor. A hidden editor can't hold focus, so it goes to the rows list,
        /// keeping Ctrl+Z working in the window.
        /// </summary>
        private void CloseEditor()
        {
            if (_editorHost is not FrameworkElement host)
            {
                return;
            }

            EndEdit();

            if (TableOf(host) is TableViewModel table && RowsOf(host, table) is ItemsControl rows)
            {
                rows.Focus();
            }
        }

        /// <summary>The table on the open tab; none on the calculator tab.</summary>
        private TableViewModel? ActiveTable() =>
            Tabs.SelectedItem is TabItem { Content: ContentPresenter { Content: TableViewModel table } }
                ? table
                : null;

        /// <summary>
        /// Ctrl+Z and Ctrl+Y (or Ctrl+Shift+Z) undo and redo table edits. In a text box these
        /// keys have their own undo for the typed text, which is left alone.
        /// </summary>
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || ActiveTable() is not TableViewModel table)
            {
                return;
            }

            ModifierKeys modifiers = Keyboard.Modifiers;

            // Ctrl+F jumps to the open table's search from anywhere.
            if (e.Key == Key.F && modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                FocusSearch();
                return;
            }

            if (e.OriginalSource is TextBox)
            {
                return;
            }
            bool undo = e.Key == Key.Z && modifiers == ModifierKeys.Control;
            bool redo = (e.Key == Key.Y && modifiers == ModifierKeys.Control)
                || (e.Key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift));

            if (!undo && !redo)
            {
                return;
            }

            e.Handled = true;
            Step(table, undo);
        }

        /// <summary>Focuses the open table's search and selects its text, ready to retype.</summary>
        private void FocusSearch()
        {
            if (Tabs.SelectedItem is not TabItem { Content: DependencyObject content }
                || FirstChild<TextBox>(content, box => AutomationProperties.GetAutomationId(box) == "SearchBox")
                    is not TextBox search)
            {
                return;
            }

            EndEdit();
            search.Focus();
            search.SelectAll();
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableViewModel table })
            {
                Step(table, undo: true);
            }
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableViewModel table })
            {
                Step(table, undo: false);
            }
        }

        /// <summary>Closes an open editor first: its edit is a step too and must be undone first.</summary>
        private void Step(TableViewModel table, bool undo)
        {
            if (table.IsLoading)
            {
                return;
            }

            EndEdit();

            if (undo)
            {
                table.Undo();
            }
            else
            {
                table.Redo();
            }
        }

        /// <summary>Finds the editor layer of the table that contains the cell.</summary>
        private static Canvas? EditorLayer(DependencyObject cell)
        {
            for (DependencyObject? node = cell; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is Grid grid && Tagged<Canvas>(grid, "CellEditorLayer") is Canvas layer)
                {
                    return layer;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a tagged element directly under the given one. Deliberately not recursive:
        /// the table rows hold thousands of elements, while the editor layer is right on top.
        /// </summary>
        private static T? Tagged<T>(DependencyObject root, string tag)
            where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(root);

            for (int index = 0; index < count; index++)
            {
                if (VisualTreeHelper.GetChild(root, index) is T found
                    && (string?)found.Tag == tag)
                {
                    return found;
                }
            }

            return null;
        }


        /// <summary>
        /// The editor lost focus and is no longer needed. Decided later: when clicking another
        /// cell the editor moves there, and the old one loses focus only after the new one is
        /// in place. So check where the focus ended up.
        /// </summary>
        private void CellEditor_LostFocus(object sender, RoutedEventArgs e) =>
            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    if (_editorHost is not null && !_editorHost.IsKeyboardFocusWithin)
                    {
                        EndEdit();
                    }
                }));

        /// <summary>Sends the cell's setup back to the calculator and switches to its tab.</summary>
        private void CellToCalculator_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableCell cell } || cell.Setup is null)
            {
                return;
            }

            try
            {
                if (JsonNode.Parse(cell.Setup) is not JsonObject setup)
                {
                    throw new InvalidDataException("The stored setup can't be read.");
                }

                if (!MayReplaceCalculator("this cell"))
                {
                    return;
                }

                SetupFile.FromJson(_viewModel, setup);
                _calculatorKept = CalculatorState();
                Tabs.SelectedIndex = Tabs.Items.Count - 1;
            }
            catch (Exception error)
            {
                Report("Export to ElderCalc failed", error);
            }
        }

        /// <summary>
        /// Switches the cell to manual editing. The setup is dropped: there's no point keeping
        /// it next to a number edited by hand, since it no longer matches what would go back.
        /// </summary>
        private void CellManualEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableCell cell })
            {
                cell.Setup = null;
            }
        }

        /// <summary>Saves the whole calculator setup to a file.</summary>
        private void ExportSetup_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new()
            {
                Title = "Export setup",
                Filter = SetupFile.DialogFilter,
                FileName = "setup",
                DefaultExt = ".json",
                AddExtension = true,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                SetupFile.Save(_viewModel, dialog.FileName);
                _calculatorKept = CalculatorState();
            }
            catch (Exception error)
            {
                Report("Export failed", error);
            }
        }

        /// <summary>Replaces the current calculator setup with the content of a file.</summary>
        private void ImportSetup_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Title = "Import setup",
                Filter = SetupFile.DialogFilter,
                CheckFileExists = true,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            if (!MayReplaceCalculator("the file"))
            {
                return;
            }

            try
            {
                SetupFile.Load(_viewModel, dialog.FileName);
                _calculatorKept = CalculatorState();
            }
            catch (Exception error)
            {
                Report("Import failed", error);
            }
        }

        /// <summary>Exports the table to a file; the user picks where and in which format.</summary>
        private void ExportTable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableViewModel table })
            {
                return;
            }

            SaveFileDialog dialog = new()
            {
                Title = $"Export {table.Title}",
                Filter = TableFile.DialogFilter,
                FileName = Path.GetFileNameWithoutExtension(_tableFiles[table]),
                DefaultExt = ".json",
                AddExtension = true,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                TableFile.Export(table, dialog.FileName);
            }
            catch (Exception error)
            {
                Report("Export failed", error);
            }
        }

        /// <summary>
        /// Loads a table from a file. If the table already has rows, the user chooses to replace
        /// them or to add the file's rows after them. Either way it can be undone.
        /// </summary>
        private void ImportTable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableViewModel table })
            {
                return;
            }

            OpenFileDialog dialog = new()
            {
                Title = $"Import {table.Title}",
                Filter = TableFile.DialogFilter,
                CheckFileExists = true,
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            bool append = false;

            if (table.Rows.Count > 0)
            {
                int choice = ConfirmWindow.Choose(
                    this,
                    $"Import {table.Title}",
                    $"The table already has {table.Rows.Count} rows. Replace them with the rows from "
                        + $"“{Path.GetFileName(dialog.FileName)}”, or add the file's rows after them? "
                        + "Either way, Undo (Ctrl+Z) brings the table back.",
                    "Replace",
                    "Add to table");

                if (choice < 0)
                {
                    return;
                }

                append = choice == 1;
            }

            EndEdit();

            try
            {
                table.Record("Import", () => TableFile.Import(table, dialog.FileName, append));
            }
            catch (Exception error)
            {
                Report("Import failed", error);
            }
        }

        private void Report(string title, Exception error) =>
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        /// <summary>Left click on a header sorts by that column and reverses the order on repeat.</summary>
        private void ColumnHeader_LeftClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableColumn column } element
                && TableOf(element) is TableViewModel table)
            {
                table.SortBy(column);
            }
        }

        /// <summary>
        /// Right click on a skill column header opens the sort priority list: what to compare
        /// cells by first. Other columns hold a single value, so there's nothing to prioritize.
        /// </summary>
        private void ColumnHeader_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableColumn column } element
                || column.Kind != TableCellKind.Integer
                || TableOf(element) is not TableViewModel table)
            {
                return;
            }

            SortPriorityPopup.IsOpen = false;
            SortPriorityPopup.DataContext = table;
            SortPriorityPopup.PlacementTarget = element;
            SortPriorityPopup.IsOpen = true;
        }

        private void PriorityUp_Click(object sender, RoutedEventArgs e) => MovePriority(sender, -1);

        private void PriorityDown_Click(object sender, RoutedEventArgs e) => MovePriority(sender, 1);

        private void MovePriority(object sender, int delta)
        {
            if (sender is FrameworkElement { DataContext: SkillSortOption option }
                && SortPriorityPopup.DataContext is TableViewModel table)
            {
                table.MovePriority(option, delta);
            }
        }

        /// <summary>Damage type of the skill, from the cell's context menu.</summary>
        private void SkillType_Click(object sender, RoutedEventArgs e)
        {
            if (CellOfMenuItem(sender) is TableCell cell
                && sender is FrameworkElement { DataContext: ElementOption option })
            {
                cell.SkillType = option;
            }
        }

        /// <summary>Sin of the skill, from the cell's context menu.</summary>
        private void SkillSin_Click(object sender, RoutedEventArgs e)
        {
            if (CellOfMenuItem(sender) is TableCell cell
                && sender is FrameworkElement { DataContext: ElementOption option })
            {
                cell.SkillSin = option;
            }
        }

        /// <summary>Removes both marks from the cell.</summary>
        private void ClearSkillMarks_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableCell cell })
            {
                return;
            }

            // Type and sin are removed together, so they must come back together in one undo.
            using CellEdit? edit = sender is MenuItem { Parent: ContextMenu { PlacementTarget: UIElement row } }
                && TableOf(row) is TableViewModel table
                    ? table.BeginCellEdit(cell, "Clear type and sin")
                    : null;

            cell.SkillType = null;
            cell.SkillSin = null;
        }

        /// <summary>
        /// The cell a submenu item belongs to. The item itself gets the option as its data;
        /// the cell comes from the parent item.
        /// </summary>
        private static TableCell? CellOfMenuItem(object sender)
        {
            if (sender is MenuItem item
                && ItemsControl.ItemsControlFromItemContainer(item) is MenuItem parent)
            {
                return parent.DataContext as TableCell;
            }

            return null;
        }

        /// <summary>The table that an element of the markup belongs to.</summary>
        private static TableViewModel? TableOf(DependencyObject element)
        {
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is FrameworkElement { DataContext: TableViewModel table })
                {
                    return table;
                }
            }

            return null;
        }

        /// <summary>
        /// The settings window. There's one settings model for the whole app: it also holds the
        /// outline brushes, and its changes apply immediately without reopening the window.
        /// </summary>
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow window = new(_settings) { Owner = this };

            window.ShowDialog();
        }

        private void AddCoin_Click(object sender, RoutedEventArgs e) => _viewModel.AddCoin();

        private void RemoveCoin_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveLastCoin();

        /// <summary>
        /// The horizontal coin scroller swallows the mouse wheel even though it can't scroll
        /// vertically. Forward the event up so the main vertical scroller moves.
        /// </summary>
        private void CoinsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            e.Handled = true;

            MouseWheelEventArgs forwarded = new(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender,
            };

            CoinsVerticalScroll.RaiseEvent(forwarded);
        }

        /// <summary>
        /// Only one subtargets window at a time: values update live, and watching the total
        /// change is more useful than a pile of windows.
        /// </summary>
        private void Subtargets_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: CoinViewModel coin })
            {
                return;
            }

            _subtargetsWindow?.Close();

            _subtargetsWindow = new SubtargetsWindow(coin) { Owner = this };
            _subtargetsWindow.Closed += (_, _) => _subtargetsWindow = null;
            _subtargetsWindow.Show();
        }

        /// <summary>
        /// <summary>Only one damage-by-target window: it updates live along with the calculation.</summary>
        /// </summary>
        private void DamageByTarget_Click(object sender, RoutedEventArgs e)
        {
            _damageByTargetWindow?.Close();

            _damageByTargetWindow = new DamageByTargetWindow(_viewModel) { Owner = this };
            _damageByTargetWindow.Closed += (_, _) => _damageByTargetWindow = null;
            _damageByTargetWindow.Show();
        }

        private void AddFlatBonus_Click(object sender, RoutedEventArgs e) =>
            _viewModel.AddBonus(BonusKind.Flat);

        private void AddPercentBonus_Click(object sender, RoutedEventArgs e) =>
            _viewModel.AddBonus(BonusKind.Percent);

        private void RemoveBonus_Click(object sender, RoutedEventArgs e)
        {
            // The bonus row is the button's DataContext: the template is generated from the collection.
            if (sender is FrameworkElement { DataContext: BonusRowViewModel row })
            {
                _viewModel.RemoveBonus(row);
            }
        }
    }
}
