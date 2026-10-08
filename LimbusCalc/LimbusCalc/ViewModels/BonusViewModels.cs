using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>
/// A bonus row in the coin table: kind and target are set once on the left,
/// and each coin has its own value (<see cref="CoinBonusViewModel"/>).
/// </summary>
public sealed class BonusRowViewModel : ObservableObject
{
    private ElementOption _target = ElementOptions.For(Element.True);

    public required BonusKind Kind { get; init; }

    /// <summary>Label of the bonus kind in the left column.</summary>
    public string KindLabel => Kind == BonusKind.Flat ? "flat" : "%";

    /// <summary>What the bonus targets: the resistance to this element scales it.</summary>
    public ElementOption Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    public IReadOnlyList<ElementOption> TargetOptions => ElementOptions.BonusTargets;
}

/// <summary>The bonus value of one particular coin.</summary>
public sealed class CoinBonusViewModel : ObservableObject
{
    private double _value;

    public required BonusRowViewModel Row { get; init; }

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}
