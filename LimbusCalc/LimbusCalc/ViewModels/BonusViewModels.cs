using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>
/// Строка бонуса в таблице монет: вид и цель задаются один раз слева,
/// а значение у каждой монеты своё (<see cref="CoinBonusViewModel"/>).
/// </summary>
public sealed class BonusRowViewModel : ObservableObject
{
    private ElementOption _target = ElementOptions.For(Element.True);

    public required BonusKind Kind { get; init; }

    /// <summary>Подпись вида бонуса в колонке слева.</summary>
    public string KindLabel => Kind == BonusKind.Flat ? "flat" : "%";

    /// <summary>На что бонус целится; в расчёте пока не участвует.</summary>
    public ElementOption Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    public IReadOnlyList<ElementOption> TargetOptions => ElementOptions.BonusTargets;
}

/// <summary>Значение бонуса у конкретной монеты.</summary>
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
