namespace LimbusCalc.ViewModels;

/// <summary>A target's resistance to one damage type or sin. 1.0 means no effect.</summary>
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
