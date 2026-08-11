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

        private DamageByTargetWindow? _damageByTargetWindow;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = _viewModel;

            // Восстанавливаем выбор пользователя до показа окна, чтобы тема не мигала.
            AppTheme saved = ThemeSettings.Load();
            ThemeManager.Apply(saved);
            ThemeSwitch.IsChecked = saved == AppTheme.Dark;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Описатель окна появляется только сейчас, раньше заголовок не перекрасить.
            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
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
