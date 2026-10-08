using System.Windows;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Modifiers of one extra target.</summary>
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
