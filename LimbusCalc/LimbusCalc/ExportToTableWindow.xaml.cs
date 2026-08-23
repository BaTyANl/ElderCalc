using System.Windows;
using System.Windows.Input;
using LimbusCalc.Theming;
using LimbusCalc.ViewModels;

namespace LimbusCalc
{
    /// <summary>Выбор строки справочника и скилла, куда уходит набор калькулятора.</summary>
    public partial class ExportToTableWindow : Window
    {
        /// <summary>Правим поле сами — на такую правку подсказки открывать не нужно.</summary>
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

        /// <summary>Набрали букву — показываем, что под неё подходит.</summary>
        private void Identity_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_settingText)
            {
                return;
            }

            Suggestions.IsOpen = Model.Targets.Count > 0;
        }

        /// <summary>
        /// Стрелки водят по подсказкам, Enter берёт выбранную, Escape закрывает список.
        /// Перехватываем до общего обработчика Enter: он снимает ввод с поля, а нам
        /// нужно сначала подставить название.
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

        /// <summary>Переставляет выбор по списку, открывая его при первой стрелке.</summary>
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
        /// Берёт выбранную подсказку: её название встаёт в поле, а список закрывается.
        /// Ввод остаётся в поле — набранное всегда можно поправить.
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
