using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Настройки приложения: тема и обводка клеток справочника.</summary>
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
        /// Цвет из палитры. Какой обводке он достался, говорит сама кнопка:
        /// в Tag у неё лежит настройка, а в данных — цвет.
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
