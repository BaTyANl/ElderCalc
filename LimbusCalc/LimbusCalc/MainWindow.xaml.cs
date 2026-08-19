using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LimbusCalc.Calculation;
using LimbusCalc.Storage;
using Microsoft.Win32;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel = new();

        /// <summary>Справочные таблицы и файлы, в которых они хранятся.</summary>
        private readonly Dictionary<TableViewModel, string> _tableFiles;

        /// <summary>Таблицы, изменённые с прошлой записи: остальные файлы не трогаем.</summary>
        private readonly HashSet<TableViewModel> _changedTables = [];

        /// <summary>
        /// Откладывает запись таблиц: во время набора правки идут на каждый символ,
        /// и писать файл после каждого незачем.
        /// </summary>
        private readonly DispatcherTimer _tableSaveTimer = new()
        {
            Interval = TimeSpan.FromSeconds(1.5),
        };

        private SubtargetsWindow? _subtargetsWindow;

        private DamageByTargetWindow? _damageByTargetWindow;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = _viewModel;

            // Восстанавливаем выбор пользователя до показа окна, чтобы тема не мигала.
            AppTheme saved = ThemeSettings.Load();
            ThemeManager.Apply(saved);
            ThemeSwitch.IsChecked = saved == AppTheme.Dark;

            _tableFiles = new Dictionary<TableViewModel, string>
            {
                [_viewModel.IdTable] = TableStorage.IdFileName,
                [_viewModel.EgoTable] = TableStorage.EgoFileName,
            };

            foreach ((TableViewModel table, string file) in _tableFiles)
            {
                TableStorage.Load(file, table);
            }

            // Подписываемся после загрузки: сохранять только что прочитанное незачем.
            foreach (TableViewModel table in _tableFiles.Keys)
            {
                table.Changed += OnTableChanged;
            }

            _tableSaveTimer.Tick += (_, _) => SaveTables();
        }

        private void OnTableChanged(object? sender, EventArgs e)
        {
            if (sender is TableViewModel table)
            {
                _changedTables.Add(table);
            }

            _tableSaveTimer.Stop();
            _tableSaveTimer.Start();
        }

        private void SaveTables()
        {
            _tableSaveTimer.Stop();

            foreach (TableViewModel table in _changedTables)
            {
                TableStorage.Save(_tableFiles[table], table);
            }

            _changedTables.Clear();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            // Правку могли не успеть записать по таймеру — дописываем на выходе.
            SaveTables();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Описатель окна появляется только сейчас, раньше заголовок не перекрасить.
            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }

        /// <summary>
        /// Кнопки справочных таблиц. Какую таблицу правим, берём из привязки самой кнопки:
        /// разметка у ID и E.G.O. общая, а модели разные.
        /// </summary>
        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableViewModel table })
            {
                table.AddRow();
            }
        }

        private void RemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableViewModel table })
            {
                table.RemoveLastRow();
            }
        }

        /// <summary>
        /// Шапка таблицы стоит вне прокрутки строк, поэтому вбок её нужно двигать вручную —
        /// иначе при горизонтальной прокрутке подписи разъедутся со своими столбцами.
        /// </summary>
        private void TableScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.HorizontalChange == 0.0 || sender is not DependencyObject body)
            {
                return;
            }

            // Шаблон таблицы разложен дважды, у ID и E.G.O., поэтому ищем не по имени,
            // а рядом с собой: обе прокрутки лежат в одной панели.
            foreach (ScrollViewer viewer in Siblings<ScrollViewer>(body))
            {
                if (viewer.Tag as string == "TableHeader")
                {
                    viewer.ScrollToHorizontalOffset(e.HorizontalOffset);
                    return;
                }
            }
        }

        private static IEnumerable<T> Siblings<T>(DependencyObject element)
            where T : DependencyObject
        {
            DependencyObject? parent = VisualTreeHelper.GetParent(element);

            while (parent is not null and not Panel)
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
        /// Кладёт текущий набор калькулятора в клетку справочника: пользователь выбирает
        /// айди и скилл, в клетку идёт итоговый урон, а набор остаётся при ней.
        /// </summary>
        private void ExportToId_Click(object sender, RoutedEventArgs e)
        {
            ExportToTableViewModel selection = ExportToTableViewModel.Create(_viewModel.IdTable);

            if (selection.Targets.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "The ID table has no named rows yet. Add a row and fill in ID Name first.",
                    "Export to ID",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            ExportToTableWindow window = new(selection) { Owner = this };

            if (window.ShowDialog() != true || selection.TargetCell is not TableCell cell)
            {
                return;
            }

            cell.Value = _viewModel.Total.ToString("0.##", CultureInfo.InvariantCulture);

            // Тип и грех берём из самого набора: в таблице они видны иконками,
            // и назначать их отдельно после выгрузки уже не нужно.
            cell.SkillType = _viewModel.SkillType;
            cell.SkillSin = _viewModel.SkillSin;
            cell.Setup = SetupFile.ToJson(_viewModel).ToJsonString();
        }

        /// <summary>Возвращает набор из клетки в калькулятор и переключает на его вкладку.</summary>
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
                    throw new InvalidDataException("Сохранённый набор не читается.");
                }

                SetupFile.FromJson(_viewModel, setup);
                Tabs.SelectedIndex = Tabs.Items.Count - 1;
            }
            catch (Exception error)
            {
                Report("Export to ElderCalc failed", error);
            }
        }

        /// <summary>
        /// Переводит клетку на ручную правку. Набор при этом выбрасывается: держать его
        /// рядом с числом, которое правят руками, незачем — вернуть в калькулятор
        /// уже нечего.
        /// </summary>
        private void CellManualEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableCell cell })
            {
                cell.Setup = null;
            }
        }

        /// <summary>Сохраняет весь набор калькулятора в файл.</summary>
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
            }
            catch (Exception error)
            {
                Report("Export failed", error);
            }
        }

        /// <summary>Заменяет текущий набор калькулятора содержимым файла.</summary>
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

            try
            {
                SetupFile.Load(_viewModel, dialog.FileName);
            }
            catch (Exception error)
            {
                Report("Import failed", error);
            }
        }

        /// <summary>Выгрузка таблицы в файл: куда и в каком виде — выбирает пользователь.</summary>
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
                DefaultExt = ".xlsx",
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

        /// <summary>Загрузка таблицы из файла. Прежние строки заменяются целиком.</summary>
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

            try
            {
                TableFile.Import(table, dialog.FileName);
            }
            catch (Exception error)
            {
                Report("Import failed", error);
            }
        }

        private void Report(string title, Exception error) =>
            MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        /// <summary>Левая кнопка по заголовку сортирует по столбцу и переворачивает порядок.</summary>
        private void ColumnHeader_LeftClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableColumn column } element
                && TableOf(element) is TableViewModel table)
            {
                table.SortBy(column);
            }
        }

        /// <summary>
        /// Правая кнопка по заголовку скилла открывает список приоритетов: чем сравнивать
        /// клетки в первую очередь. У прочих столбцов сравнивать нечего — там одно значение.
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

        /// <summary>Тип урона скилла из правого меню клетки.</summary>
        private void SkillType_Click(object sender, RoutedEventArgs e)
        {
            if (CellOfMenuItem(sender) is TableCell cell
                && sender is FrameworkElement { DataContext: ElementOption option })
            {
                cell.SkillType = option;
            }
        }

        private void SkillSin_Click(object sender, RoutedEventArgs e)
        {
            if (CellOfMenuItem(sender) is TableCell cell
                && sender is FrameworkElement { DataContext: ElementOption option })
            {
                cell.SkillSin = option;
            }
        }

        private void ClearSkillMarks_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableCell cell })
            {
                cell.SkillType = null;
                cell.SkillSin = null;
            }
        }

        /// <summary>
        /// Клетка, которой принадлежит пункт подменю. Сам пункт получает данными вариант
        /// из списка, а клетка достаётся от пункта-родителя.
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

        /// <summary>Таблица, которой принадлежит элемент разметки.</summary>
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

        private void ThemeSwitch_Changed(object sender, RoutedEventArgs e)
        {
            AppTheme theme = ThemeSwitch.IsChecked == true ? AppTheme.Dark : AppTheme.Light;

            ThemeManager.Apply(theme);
            ThemeSettings.Save(theme);
        }

        private void AddCoin_Click(object sender, RoutedEventArgs e) => _viewModel.AddCoin();

        private void RemoveCoin_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveLastCoin();

        /// <summary>
        /// Горизонтальная прокрутка монет глотает колесо, хотя вертикально не двигается.
        /// Перебрасываем событие наверх, чтобы крутилась общая вертикальная прокрутка.
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
        /// Окно подцелей держим одно: значения правятся вживую, и удобнее видеть,
        /// как меняется итог, чем собирать стопку окон.
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
        /// Окно распределения держим одно: оно обновляется живьём вместе с расчётом.
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
            // Строка бонуса лежит в DataContext кнопки: шаблон строится по коллекции.
            if (sender is FrameworkElement { DataContext: BonusRowViewModel row })
            {
                _viewModel.RemoveBonus(row);
            }
        }
    }
}
