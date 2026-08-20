using System.Windows;
using LimbusCalc.Theming;

namespace LimbusCalc
{
    /// <summary>
    /// Подтверждение действия, которое нельзя отменить. Своё окно, а не системное:
    /// системное в тёмной теме выглядит чужеродно.
    /// </summary>
    public partial class ConfirmWindow : Window
    {
        private ConfirmWindow(string title, string message, string confirmText)
        {
            InitializeComponent();

            Title = title;
            HeaderText.Text = title;
            MessageText.Text = message;
            ConfirmButton.Content = confirmText;
        }

        /// <summary>Показывает вопрос; возвращает true, если пользователь согласился.</summary>
        public static bool Ask(Window owner, string title, string message, string confirmText)
        {
            ConfirmWindow window = new(title, message, confirmText) { Owner = owner };

            return window.ShowDialog() == true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
