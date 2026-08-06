using System.Windows;
using System.Windows.Input;
using LimbusCalc.Calculation;
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

        private SubtargetsWindow? _subtargetsWindow;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = _viewModel;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Описатель окна появляется только сейчас, раньше заголовок не перекрасить.
            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }

        private void ThemeSwitch_Changed(object sender, RoutedEventArgs e)
        {
            ThemeManager.Apply(ThemeSwitch.IsChecked == true ? AppTheme.Dark : AppTheme.Light);
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
