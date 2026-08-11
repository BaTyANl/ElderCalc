using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Таблица распределения урона: строки — цели, столбцы — монеты.</summary>
    public partial class DamageByTargetWindow : Window
    {
        public DamageByTargetWindow(MainViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        /// <summary>Нажатие на подпись столбца сортирует таблицу по нему.</summary>
        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TargetColumnViewModel column }
                && DataContext is MainViewModel viewModel)
            {
                viewModel.SortDamageByTarget(column);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }
    }
}
