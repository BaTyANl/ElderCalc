namespace LimbusCalc.ViewModels;

/// <summary>Сопротивление цели к одному типу урона или греху. Единица — без эффекта.</summary>
public sealed class ResistanceViewModel : ObservableObject
{
    private double _value = 1.0;

    public required ElementOption Option { get; init; }

    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}
