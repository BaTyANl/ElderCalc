using System.Windows;
using LimbusCalc.Storage;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>
    /// Export choices: format, and whether to take every row and column or only what's shown.
    /// The choices that make no difference right now (no filter set, no column hidden) are off.
    /// </summary>
    public partial class ExportTableWindow : Window
    {
        public ExportTableWindow(TableViewModel table)
        {
            InitializeComponent();

            Title = $"Export {table.Title}";
            HeaderText.Text = $"Export {table.Title}";

            int shownRows = table.Rows.Count(row => row.IsVisible);
            int hiddenColumns = table.Columns.Count(column => column.IsHidden);

            ShownRows.Content = $"Only rows shown by the filters ({shownRows} of {table.Rows.Count})";
            ShownRows.IsEnabled = table.Filter.IsActive;

            ShownColumns.Content = hiddenColumns == 0
                ? "Only shown columns (none are hidden)"
                : $"Only shown columns ({hiddenColumns} hidden)";
            ShownColumns.IsEnabled = hiddenColumns > 0;

            // When something is filtered or hidden, the shown part is the likelier choice.
            ShownRows.IsChecked = ShownRows.IsEnabled;
            AllRows.IsChecked = !ShownRows.IsEnabled;
            ShownColumns.IsChecked = ShownColumns.IsEnabled;
            AllColumns.IsChecked = !ShownColumns.IsEnabled;
        }

        /// <summary>The choices made; null until the user confirms.</summary>
        public TableExportOptions? Options { get; private set; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            Options = new TableExportOptions(
                ShownRows.IsChecked == true,
                ShownColumns.IsChecked == true,
                ExcelFormat.IsChecked == true ? TableExportFormat.Excel : TableExportFormat.Json);

            DialogResult = true;
        }
    }
}
