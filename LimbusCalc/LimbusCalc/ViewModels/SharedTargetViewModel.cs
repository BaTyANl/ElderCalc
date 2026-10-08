namespace LimbusCalc.ViewModels;

/// <summary>
/// The shared part of an extra target: a subtarget with a given number is one enemy, so its
/// resistances and Time Moratorium are the same for every coin of the skill. Everything else
/// (dyn mod, crit, level difference) is per coin.
/// </summary>
public sealed class SharedTargetViewModel : ObservableObject
{
    private bool _timeMoratorium;
    private int _timeMoratoriumStacks = 1;

    /// <summary>Resistances in <see cref="ElementOptions.ResistanceOrder"/> order.</summary>
    public required IReadOnlyList<ResistanceViewModel> Resistances { get; init; }

    public bool TimeMoratorium
    {
        get => _timeMoratorium;
        set => SetProperty(ref _timeMoratorium, value);
    }

    /// <summary>Number of stacks; only 1 and 2 are allowed.</summary>
    public int TimeMoratoriumStacks
    {
        get => _timeMoratoriumStacks;
        set
        {
            int clamped = Math.Clamp(value, 1, 2);

            if (_timeMoratoriumStacks != clamped)
            {
                _timeMoratoriumStacks = clamped;
                OnPropertyChanged();
            }
            else if (value != clamped)
            {
                // Already at the limit but the input went past it: snap the field back.
                OnPropertyChanged();
            }
        }
    }
}
