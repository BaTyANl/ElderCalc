using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace LimbusCalc.Behaviors;

/// <summary>
/// Ввод по Enter: значение применяется и поле отпускает фокус. Нужно и тем полям,
/// которые отдают значение только по уходу фокуса, — иначе набранное повисало бы
/// в поле, пока пользователь не щёлкнет мимо.
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

        // Привязка могла ждать ухода фокуса — подталкиваем её сами.
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        // Снимаем и логический фокус, и клавиатурный: первое запускает всё, что
        // навешано на LostFocus, второе убирает курсор из поля. Одного мало —
        // окно вернуло бы фокус обратно в то же поле.
        if (FocusManager.GetFocusScope(box) is DependencyObject focusScope)
        {
            FocusManager.SetFocusedElement(focusScope, null);
        }

        Keyboard.ClearFocus();

        e.Handled = true;
    }
}
