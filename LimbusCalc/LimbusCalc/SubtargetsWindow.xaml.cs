using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Resistances of one coin's extra targets.</summary>
    public partial class SubtargetsWindow : Window
    {
        private SubtargetParametersWindow? _parametersWindow;

        public SubtargetsWindow(CoinViewModel coin)
        {
            InitializeComponent();

            DataContext = coin;
        }

        /// <summary>
        /// Only one parameters window at a time: values update live, and watching the total
        /// change is more useful than a pile of windows.
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
