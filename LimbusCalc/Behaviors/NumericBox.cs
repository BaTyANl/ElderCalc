using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LimbusCalc.Behaviors;

/// <summary>
/// Числовое поле ввода. Привязываться нужно к <see cref="ValueProperty"/>, а не к Text:
/// обычная двусторонняя привязка Text переписывает содержимое на каждом нажатии,
/// из-за чего курсор прыгает в начало, а формат съедает только что набранную точку
/// (12.0 сворачивается в 12). Здесь текст не трогается, пока поле в фокусе.
/// Разделителем принимается и точка, и запятая — раскладка значения не имеет.
/// </summary>
public static class NumericBox
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.RegisterAttached(
            "Value",
            typeof(double),
            typeof(NumericBox),
            // Значение по умолчанию — NaN, а не ноль: обработчик вызывается только при
            // изменении, и поле со стартовым нулём иначе не подключилось бы вовсе.
            new FrameworkPropertyMetadata(
                double.NaN,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    /// <summary>Разрешать только целые: дробный разделитель набрать нельзя.</summary>
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

        // Пока пользователь печатает, текст остаётся его: иначе курсор уедет,
        // а незавершённое число вроде "12." будет затёрто.
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

        // Недонабранное ("-", "12.") просто пропускаем: значение изменится,
        // когда строка станет числом.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            SetValue(box, GetIsInteger(box) ? Math.Truncate(parsed) : parsed);
        }
    }

    /// <summary>По выходу из поля приводим текст к нормальному виду: "12." станет "12".</summary>
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
        // Пустую строку и одинокий минус пропускаем, иначе минус нельзя было бы набрать.
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
