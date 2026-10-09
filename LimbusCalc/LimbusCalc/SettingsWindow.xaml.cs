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

            // No taller than the screen's free area; the content scrolls instead.
            MaxHeight = SystemParameters.WorkArea.Height;
            DataContext = viewModel;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }

        /// <summary>
        /// A palette color. The button itself tells which setting it belongs to — an outline or
        /// an end of the damage scale: the setting is in its Tag and the color is its data.
        /// </summary>
        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement
                {
                    Tag: IColorSetting setting,
                    DataContext: string hex,
                })
            {
                setting.Hex = hex;
            }
        }

        private void ResetDamageScale_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel settings)
            {
                settings.ResetDamageScale();
            }
        }
    }
}
