using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Окно модификаторов одной дополнительной цели.</summary>
    public partial class SubtargetParametersWindow : Window
    {
        public SubtargetParametersWindow(SubtargetViewModel subtarget)
        {
            InitializeComponent();

            DataContext = subtarget;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }
    }
}
