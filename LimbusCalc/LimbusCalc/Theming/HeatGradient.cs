using System.Windows.Media;

namespace LimbusCalc.Theming;

/// <summary>
/// The damage color scale: from the lowest value's color and opacity to the highest's. Brushes
/// are prepared once for a few dozen steps, so drawing thousands of cells while scrolling
/// creates none — and the eye can't tell more steps apart anyway.
/// </summary>
public sealed class HeatGradient
{
    private const int Steps = 32;

    private readonly SolidColorBrush[] _brushes = new SolidColorBrush[Steps + 1];

    public HeatGradient(ScaleStop low, ScaleStop high)
    {
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(high);

        for (int i = 0; i <= Steps; i++)
        {
            double share = (double)i / Steps;

            Color color = Color.FromArgb(
                Mix(low.Opacity * 255, high.Opacity * 255, share),
                Mix(low.Color.R, high.Color.R, share),
                Mix(low.Color.G, high.Color.G, share),
                Mix(low.Color.B, high.Color.B, share));

            SolidColorBrush brush = new(color);
            brush.Freeze();
            _brushes[i] = brush;
        }
    }

    /// <summary>The fill for a value at this place on the scale: 0 is the lowest, 1 the highest.</summary>
    public Brush BrushAt(double share) => _brushes[(int)Math.Round(Math.Clamp(share, 0.0, 1.0) * Steps)];

    private static byte Mix(double from, double to, double share) =>
        (byte)Math.Round(from + ((to - from) * share));
}
