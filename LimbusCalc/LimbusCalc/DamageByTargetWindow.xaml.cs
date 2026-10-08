using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Damage split by target: rows are targets, columns are coins.</summary>
    public partial class DamageByTargetWindow : Window
    {
        public DamageByTargetWindow(MainViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        /// <summary>Clicking a column header sorts the table by that column.</summary>
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
