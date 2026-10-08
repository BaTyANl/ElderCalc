using System.Windows;
using System.Windows.Input;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Picks the table row and skill cell that receive the calculator setup.</summary>
    public partial class ExportToTableWindow : Window
    {
        /// <summary>Set while we change the text ourselves, so the suggestions don't pop up.</summary>
        private bool _settingText;

        public ExportToTableWindow(ExportToTableViewModel viewModel)
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        private ExportToTableViewModel Model => (ExportToTableViewModel)DataContext;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            ThemeManager.ApplyTitleBar(this, ThemeManager.Current);

            IdentityBox.Focus();
        }

        /// <summary>A letter was typed: show what matches it.</summary>
        private void Identity_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_settingText)
            {
                return;
            }

            Suggestions.IsOpen = Model.Targets.Count > 0;
        }

        /// <summary>
        /// Arrows move through the suggestions, Enter takes the selected one, Escape closes the
        /// list. Handled before the shared Enter handler, which releases focus from the field,
        /// because the name has to be filled in first.
        /// </summary>
        private void Identity_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                case Key.Up:
                    Move(e.Key == Key.Down ? 1 : -1);
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (Suggestions.IsOpen)
                    {
                        Commit();
                        e.Handled = true;
                    }

                    break;

                case Key.Escape:
                    if (Suggestions.IsOpen)
                    {
                        Suggestions.IsOpen = false;
                        e.Handled = true;
                    }

                    break;
            }
        }

        private void Suggestion_Click(object sender, MouseButtonEventArgs e) => Commit();

        /// <summary>Moves the selection through the list, opening it on the first arrow press.</summary>
        private void Move(int step)
        {
            if (Model.Targets.Count == 0)
            {
                return;
            }

            if (!Suggestions.IsOpen)
            {
                Suggestions.IsOpen = true;
            }

            int next = SuggestionList.SelectedIndex + step;

            SuggestionList.SelectedIndex = Math.Clamp(next, 0, Model.Targets.Count - 1);
            SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
        }

        /// <summary>
        /// Takes the selected suggestion: its name goes into the field and the list closes.
        /// Focus stays in the field so the text can always be corrected.
        /// </summary>
        private void Commit()
        {
            if (SuggestionList.SelectedItem is not ExportTargetViewModel target)
            {
                return;
            }

            _settingText = true;

            try
            {
                Model.Search = target.Display;
                Model.SelectedTarget = target;
            }
            finally
            {
                _settingText = false;
            }

            Suggestions.IsOpen = false;

            IdentityBox.Focus();
            IdentityBox.CaretIndex = IdentityBox.Text.Length;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
