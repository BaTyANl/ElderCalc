using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LimbusCalc.Theming;

namespace LimbusCalc
{
    /// <summary>
    /// A question before an action that changes a lot at once. A custom window rather than
    /// the system message box, which looks out of place in the dark theme.
    /// </summary>
    public partial class ConfirmWindow : Window
    {
        /// <summary>Index of the chosen option; -1 when the user backed out.</summary>
        private int _choice = -1;

        private ConfirmWindow(string title, string message, IReadOnlyList<string> choices)
        {
            InitializeComponent();

            Title = title;
            HeaderText.Text = title;
            MessageText.Text = message;

            // The first option is the main one and gets the accent color; the rest are quieter.
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;

                Button button = new()
                {
                    Content = choices[i],
                    MinWidth = 110,
                    Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0),
                };

                if (i > 0)
                {
                    button.Style = (Style)FindResource("SecondaryButton");
                }

                AutomationProperties.SetAutomationId(button, i == 0 ? "ConfirmButton" : $"ChoiceButton{i}");
                button.Click += (_, _) =>
                {
                    _choice = index;
                    DialogResult = true;
                };

                ChoicePanel.Children.Insert(i, button);
            }
        }

        /// <summary>Shows the question; returns true if the user agreed.</summary>
        public static bool Ask(Window owner, string title, string message, string confirmText) =>
            Choose(owner, title, message, confirmText) == 0;

        /// <summary>A question with several answers. Returns the chosen index, or -1 on cancel.</summary>
        public static int Choose(Window owner, string title, string message, params string[] choices)
        {
            ConfirmWindow window = new(title, message, choices) { Owner = owner };

            return window.ShowDialog() == true ? window._choice : -1;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);
        }
    }
}
