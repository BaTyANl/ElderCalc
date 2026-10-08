using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LimbusCalc.Behaviors;

/// <summary>
/// A numeric text box. Bind to <see cref="ValueProperty"/>, not to Text: a regular two-way
/// Text binding rewrites the content on every key press, which jumps the caret to the start
/// and lets the format swallow a freshly typed dot (12. collapses to 12). Here the text is
/// left alone while the box has focus. Both a dot and a comma work as the decimal separator.
/// </summary>
public static class NumericBox
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.RegisterAttached(
            "Value",
            typeof(double),
            typeof(NumericBox),
            // The default is NaN, not zero: the callback fires only on change, so a box
            // that starts at zero would otherwise never get attached.
            new FrameworkPropertyMetadata(
                double.NaN,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    /// <summary>Allow whole numbers only: the decimal separator can't be typed.</summary>
    public static readonly DependencyProperty IsIntegerProperty =
        DependencyProperty.RegisterAttached(
            "IsInteger",
            typeof(bool),
            typeof(NumericBox),
            new PropertyMetadata(false));

    private static readonly DependencyProperty AttachedProperty =
        DependencyProperty.RegisterAttached(
            "Attached",
            typeof(bool),
            typeof(NumericBox),
            new PropertyMetadata(false));

    public static void SetValue(DependencyObject element, double value) =>
        element.SetValue(ValueProperty, value);

    public static double GetValue(DependencyObject element) =>
        (double)element.GetValue(ValueProperty);

    public static void SetIsInteger(DependencyObject element, bool value) =>
        element.SetValue(IsIntegerProperty, value);

    public static bool GetIsInteger(DependencyObject element) =>
        (bool)element.GetValue(IsIntegerProperty);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        Attach(box);

        // While the user is typing the text is theirs: otherwise the caret would jump
        // and an unfinished number like "12." would be overwritten.
        if (box.IsKeyboardFocusWithin)
        {
            return;
        }

        box.Text = Format((double)e.NewValue, GetIsInteger(box));
    }

    private static void Attach(TextBox box)
    {
        if ((bool)box.GetValue(AttachedProperty))
        {
            return;
        }

        box.SetValue(AttachedProperty, true);
        box.TextChanged += OnTextChanged;
        box.LostFocus += OnLostFocus;
        box.PreviewTextInput += OnPreviewTextInput;
        DataObject.AddPastingHandler(box, OnPaste);
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        TextBox box = (TextBox)sender;
        string text = box.Text.Trim().Replace(',', '.');

        if (text.Length == 0)
        {
            SetValue(box, 0.0);
            return;
        }

        // Skip unfinished input ("-", "12."): the value changes once the text is a number.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            SetValue(box, GetIsInteger(box) ? Math.Truncate(parsed) : parsed);
        }
    }

    /// <summary>On leaving the box the text is normalized: "12." becomes "12".</summary>
    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        TextBox box = (TextBox)sender;
        box.Text = Format(GetValue(box), GetIsInteger(box));
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        TextBox box = (TextBox)sender;
        e.Handled = !IsAcceptable(ResultingText(box, e.Text), GetIsInteger(box));
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        TextBox box = (TextBox)sender;
        string pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;

        if (!IsAcceptable(ResultingText(box, pasted), GetIsInteger(box)))
        {
            e.CancelCommand();
        }
    }

    private static string ResultingText(TextBox box, string input) =>
        box.Text
            .Remove(box.SelectionStart, box.SelectionLength)
            .Insert(box.SelectionStart, input);

    private static bool IsAcceptable(string text, bool integer)
    {
        // Allow an empty string and a lone minus, otherwise a minus could never be typed.
        if (text.Length == 0 || text == "-")
        {
            return true;
        }

        int start = text[0] == '-' ? 1 : 0;

        if (start == text.Length)
        {
            return false;
        }

        bool separatorSeen = false;

        for (int i = start; i < text.Length; i++)
        {
            char symbol = text[i];

            if (char.IsAsciiDigit(symbol))
            {
                continue;
            }

            if (!integer && symbol is '.' or ',' && !separatorSeen)
            {
                separatorSeen = true;
                continue;
            }

            return false;
        }

        return true;
    }

    private static string Format(double value, bool integer) =>
        double.IsNaN(value)
            ? string.Empty
            : value.ToString(integer ? "0" : "0.####", CultureInfo.InvariantCulture);
}
