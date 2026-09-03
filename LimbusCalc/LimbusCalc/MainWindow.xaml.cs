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

        /// <summary>Настройки приложения: тема и обводка клеток.</summary>
        private readonly SettingsViewModel _settings;

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

            // Восстанавливаем выбор пользователя до показа окна, чтобы тема не мигала,
            // и сразу ставим кисти обводки — иначе клетки нарисуются без них.
            ThemeManager.Apply(AppSettings.LoadTheme());
            _settings = new SettingsViewModel();
            _settings.Apply();

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

        /// <summary>
        /// Очистка таблицы целиком. Отменить это нечем, поэтому сначала спрашиваем
        /// и называем, сколько строк уйдёт.
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
                $"All {table.Rows.Count} rows will be removed, together with the setups stored in their cells. This cannot be undone.",
                "Clear table");

            if (confirmed)
            {
                table.Clear();
            }
        }

        /// <summary>
        /// Меню строки открывается по правой кнопке над клетками, которые её описывают.
        /// Строку и таблицу берём из дерева: у клетки в привязке только она сама.
        /// </summary>
        private void Row_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not FrameworkElement element
                || DataOf<TableRowViewModel>(element) is not TableRowViewModel row
                || DataOf<TableViewModel>(element) is not TableViewModel table)
            {
                return;
            }

            ContextMenu menu = (ContextMenu)FindResource("RowMenu");

            menu.DataContext = row;
            menu.Tag = table;
            menu.PlacementTarget = element;
            menu.IsOpen = true;

            e.Handled = true;
        }

        /// <summary>Ближайшая вверх по дереву привязка нужного вида.</summary>
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
        /// Удаление строки. Вместе с ней уходят наборы, лежавшие в её клетках,
        /// и вернуть их нечем — поэтому сначала спрашиваем и называем, что удаляем.
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
                $"{label} will be removed, together with the setups stored in its cells. This cannot be undone.",
                "Delete row");

            if (confirmed)
            {
                table.Remove(row);
            }
        }

        /// <summary>
        /// Шапка таблицы стоит вне прокрутки строк, поэтому вбок её нужно двигать вручную —
        /// иначе при горизонтальной прокрутке подписи разъедутся со своими столбцами.
        /// </summary>
        /// <summary>
        /// Ширина видимой области поменялась — пересчитываем растяжимый столбец.
        /// Одного ScrollChanged мало: при первой раскладке он приходит раньше,
        /// чем становится известна настоящая ширина.
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
        /// Сколько ширины достаётся столбцам. ViewportWidth у прокрутки с виртуализацией
        /// показывает не всю доступную ширину, поэтому берём размер самой области
        /// и вычитаем полосу прокрутки, когда она есть.
        /// </summary>
        private static double VisibleWidth(ScrollViewer viewer)
        {
            double scrollbar = viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible
                ? SystemParameters.VerticalScrollBarWidth
                : 0.0;

            return viewer.ActualWidth - scrollbar;
        }

        private void TableScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not DependencyObject body)
            {
                return;
            }

            // Ширину растяжимого столбца считаем от видимой области строк: у шапки
            // и строки средних она своя, и по ней столбцы разъехались бы на ширину
            // вертикальной полосы прокрутки.
            if (e.ViewportWidthChange != 0.0
                && sender is ScrollViewer { DataContext: TableViewModel table } viewer)
            {
                table.UpdateColumnWidths(VisibleWidth(viewer));
            }

            // Поле ввода стоит поверх клетки и вместе с ней не едет: при прокрутке
            // оно оказалось бы над чужой. Закрываем — значение уже записано.
            if (e.VerticalChange != 0.0 || e.HorizontalChange != 0.0)
            {
                EndEdit();
            }

            if (e.HorizontalChange == 0.0)
            {
                return;
            }

            // Шаблон таблицы разложен дважды, у ID и E.G.O., поэтому ищем не по имени,
            // а рядом с собой: шапка и строка средних лежат в одной панели со строками.
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
            // Ищем именно панель самой таблицы: над строками есть и свои обёртки,
            // а шапка со строкой средних лежат уровнем выше, в общем доке.
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
        /// Открывает список фильтра. Какой именно — говорит сама кнопка: список лежит
        /// у неё в Tag, поэтому окошко на все три одно.
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

        private void FilterReset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TableFilterViewModel filter })
            {
                filter.Reset();
            }
        }

        /// <summary>
        /// Кладёт текущий набор калькулятора в клетку справочника: пользователь выбирает
        /// айди и скилл, в клетку идёт итоговый урон, а набор остаётся при ней.
        /// </summary>
        private void ExportToId_Click(object sender, RoutedEventArgs e) =>
            ExportToTable(_viewModel.IdTable);

        private void ExportToEgo_Click(object sender, RoutedEventArgs e) =>
            ExportToTable(_viewModel.EgoTable);

        /// <summary>
        /// Кладёт текущий набор в клетку справочника: пользователь выбирает строку
        /// по названию и столбец, куда это уходит.
        /// </summary>
        private void ExportToTable(TableViewModel table)
        {
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

            cell.Value = _viewModel.Total.ToString("0.##", CultureInfo.InvariantCulture);

            // Тип и грех берём из самого набора: в таблице они видны иконками,
            // и назначать их отдельно после выгрузки уже не нужно.
            cell.SkillType = _viewModel.SkillType;
            cell.SkillSin = _viewModel.SkillSin;
            cell.Setup = SetupFile.ToJson(_viewModel).ToJsonString();
        }

        /// <summary>
        /// Меню клетки открываем сами и одно на всех: в шаблоне клетки оно заводилось
        /// для каждой клетки отдельно вместе с подменю и картинками, и на прокрутке
        /// это было самой дорогой частью строки.
        /// </summary>
        private void Cell_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableCell cell } element)
            {
                return;
            }

            // Клетке вроде Sin Cost предложить нечего: ни набора, ни меток. Показываем
            // меню строки — пустое меню под правой кнопкой выглядело бы поломкой.
            if (!cell.HasSetup && !cell.CanEditMarks)
            {
                Row_ContextMenuOpening(sender, e);
                return;
            }

            ContextMenu menu = (ContextMenu)FindResource("CellMenu");

            menu.DataContext = cell;
            menu.PlacementTarget = element;
            menu.IsOpen = true;

            e.Handled = true;
        }

        /// <summary>Клетка, которую сейчас правят, и слой с её полем ввода.</summary>
        private TableCell? _editingCell;

        private Border? _editorHost;

        /// <summary>
        /// По клику над клеткой встаёт поле ввода. Раньше поле лежало в каждой клетке,
        /// и прокрутка упиралась именно в них: на экране их под три сотни, а нужно одно.
        /// </summary>
        private void Cell_BeginEdit(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableCell cell } element)
            {
                return;
            }

            if (ReferenceEquals(_editingCell, cell))
            {
                return;
            }

            EndEdit();

            if (EditorLayer(element) is not Canvas layer
                || Tagged<Border>(layer, "CellEditorHost") is not Border host
                || Tagged<ContentControl>(host, "CellEditorContent") is not ContentControl slot)
            {
                return;
            }

            string template = cell.Column.Kind switch
            {
                TableCellKind.Integer => "IntegerEditorTemplate",
                TableCellKind.Options => "OptionsEditorTemplate",
                _ => "TextEditorTemplate",
            };

            Rect box = element.TransformToVisual(layer)
                .TransformBounds(new Rect(element.RenderSize));

            Canvas.SetLeft(host, box.X);
            Canvas.SetTop(host, box.Y);
            host.Width = box.Width;
            host.Height = box.Height;

            slot.ContentTemplate = (DataTemplate)FindResource(template);
            slot.Content = cell;
            host.Visibility = Visibility.Visible;

            _editingCell = cell;
            _editorHost = host;

            // Поле появится, когда пройдёт раскладка; тогда же отдаём ему ввод.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FocusEditor(slot)));
        }

        /// <summary>Отдаёт ввод только что поставленному полю.</summary>
        private void FocusEditor(DependencyObject slot)
        {
            switch (FirstChild<Control>(slot))
            {
                case TextBox box:
                    box.Focus();
                    box.SelectAll();
                    break;

                case ComboBox chooser:
                    chooser.Focus();

                    // Раскрывать до того, как список встал на место, нельзя: всплывашка
                    // цепляет мышь и тут же закрывается обратно.
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        new Action(() => chooser.IsDropDownOpen = true));
                    break;
            }
        }

        /// <summary>Первый подходящий элемент вглубь дерева.</summary>
        private static T? FirstChild<T>(DependencyObject root)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);

            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);

                if (child is T found)
                {
                    return found;
                }

                if (FirstChild<T>(child) is T deeper)
                {
                    return deeper;
                }
            }

            return null;
        }

        /// <summary>Убирает поле ввода: правка закончилась.</summary>
        private void EndEdit()
        {
            if (_editorHost is not null)
            {
                _editorHost.Visibility = Visibility.Collapsed;

                if (Tagged<ContentControl>(_editorHost, "CellEditorContent") is ContentControl slot)
                {
                    // Разметку убираем вместе со значением. Иначе на клетку того же вида
                    // поле достанется прежнее: список редкости открывался бы с грешниками
                    // или с высотой не под своё число строк.
                    slot.Content = null;
                    slot.ContentTemplate = null;
                }
            }

            _editingCell = null;
            _editorHost = null;
        }

        /// <summary>Ищет слой правки той таблицы, в которой лежит клетка.</summary>
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
        /// Ищет помеченный элемент прямо под указанным. Вглубь не идём нарочно:
        /// под строками таблицы лежат тысячи элементов, а слой правки — на виду.
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
        /// Ушли из поля ввода — оно больше не нужно. Решаем не сразу: при переходе
        /// щелчком на соседнюю клетку поле переезжает туда, и старое теряет ввод уже
        /// после того, как новое встало на место. Смотрим, где ввод оказался в итоге.
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

        /// <summary>
        /// Окно настроек. Модель одна на всё приложение: она же держит кисти обводки,
        /// и её правки видны сразу, без повторного открытия окна.
        /// </summary>
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow window = new(_settings) { Owner = this };

            window.ShowDialog();
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
