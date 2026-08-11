using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Окно настройки сопротивлений дополнительных целей одной монеты.</summary>
    public partial class SubtargetsWindow : Window
    {
        private SubtargetParametersWindow? _parametersWindow;

        public SubtargetsWindow(CoinViewModel coin)
        {
            InitializeComponent();

            DataContext = coin;
        }

        /// <summary>
        /// Окно параметров держим одно: значения правятся вживую, и удобнее видеть,
        /// как меняется итог, чем собирать стопку окон.
        /// </summary>
        private void Parameters_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SubtargetViewModel subtarget })
            {
                return;
            }

            _parametersWindow?.Close();

            _parametersWindow = new SubtargetParametersWindow(subtarget) { Owner = this };
            _parametersWindow.Closed += (_, _) => _parametersWindow = null;
            _parametersWindow.Show();
        }

        private void ResetAll_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not CoinViewModel coin)
            {
                return;
            }

            foreach (SubtargetViewModel subtarget in coin.Subtargets)
            {
                subtarget.ResetToMain();
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SubtargetViewModel subtarget })
            {
                subtarget.ResetToMain();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }
    }
}
