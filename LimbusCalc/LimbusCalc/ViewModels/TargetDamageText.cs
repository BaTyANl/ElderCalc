using System.Globalization;
using System.Text;

namespace LimbusCalc.ViewModels;

/// <summary>Разбивка урона по целям для всплывающей подсказки.</summary>
internal static class TargetDamageText
{
    public static string Format(IReadOnlyList<double> perTarget)
    {
        if (perTarget.Count == 0)
        {
            return "No targets";
        }

        StringBuilder text = new();

        for (int i = 0; i < perTarget.Count; i++)
        {
            if (i > 0)
            {
                text.AppendLine();
            }

            // Нулевая цель — основная, дальше подцели с их собственными сопротивлениями.
            text.Append(i == 0 ? "MT" : $"ST{i}")
                .Append(": ")
                .Append(perTarget[i].ToString("0.##", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}
