using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LimbusCalc.Behaviors;

/// <summary>
/// Пропускает в поле только целое число. В отличие от <see cref="NumericBox"/> ничего
/// не подставляет вместо пустой строки: в таблице пустая клетка значит «нет данных»,
/// а не ноль, и превращать её в ноль нельзя.
/// </summary>
public static class IntegerText
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(IntegerText),
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

        box.PreviewTextInput -= OnPreviewTextInput;
        DataObject.RemovePastingHandler(box, OnPaste);

        if (e.NewValue is true)
        {
            box.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(box, OnPaste);
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        TextBox box = (TextBox)sender;
        e.Handled = !IsAcceptable(ResultingText(box, e.Text));
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        TextBox box = (TextBox)sender;
        string pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;

        if (!IsAcceptable(ResultingText(box, pasted)))
        {
            e.CancelCommand();
        }
    }

    private static string ResultingText(TextBox box, string input) =>
        box.Text
            .Remove(box.SelectionStart, box.SelectionLength)
            .Insert(box.SelectionStart, input);

    private static bool IsAcceptable(string text)
    {
        // Пустую строку и одинокий минус пропускаем, иначе минус нельзя было бы набрать.
        if (text.Length == 0 || text == "-")
        {
            return true;
        }

        int start = text[0] == '-' ? 1 : 0;

        for (int i = start; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
