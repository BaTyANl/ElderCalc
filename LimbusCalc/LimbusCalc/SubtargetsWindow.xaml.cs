using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Окно настройки сопротивлений дополнительных целей одной монеты.</summary>
    public partial class SubtargetsWindow : Window
    {
        public SubtargetsWindow(CoinViewModel coin)
        {
            InitializeComponent();

            DataContext = coin;
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
