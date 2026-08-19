using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Выбор строки справочника и скилла, куда уходит набор калькулятора.</summary>
    public partial class ExportToTableWindow : Window
    {
        public ExportToTableWindow(ExportToTableViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
