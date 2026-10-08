using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace LimbusCalc.Behaviors;

/// <summary>
/// Enter commits the value and releases focus. Fields that update their source only on
/// lost focus need this too, otherwise the typed value would hang in the field until
/// the user clicks elsewhere.
/// </summary>
public static class EnterCommits
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(EnterCommits),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(DependencyObject element) =>
        (bool)element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        box.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is true)
        {
            box.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || sender is not TextBox box)
        {
            return;
        }

        // The binding may be waiting for lost focus, so push the value ourselves.
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        // Clear both logical and keyboard focus: the first fires LostFocus handlers, the
        // second removes the caret. Either alone is not enough — the window would put
        // focus straight back into the same field.
        if (FocusManager.GetFocusScope(box) is DependencyObject focusScope)
        {
            FocusManager.SetFocusedElement(focusScope, null);
        }

        Keyboard.ClearFocus();

        e.Handled = true;
    }
}
