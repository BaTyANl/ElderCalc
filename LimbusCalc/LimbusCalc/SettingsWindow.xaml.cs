using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>App settings: theme, table cell look and outlines.</summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow(SettingsViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }

        /// <summary>
        /// A palette color. The button itself tells which outline it belongs to:
        /// the outline setting is in its Tag and the color is its data.
        /// </summary>
        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement
                {
                    Tag: OutlineSettingsViewModel outline,
                    DataContext: string hex,
                })
            {
                outline.Hex = hex;
            }
        }
    }
}
