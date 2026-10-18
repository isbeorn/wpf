using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace WindowsReference;

internal sealed class FixtureModel : INotifyPropertyChanged, ICommand
{
    private int _value = 7;
    private bool _enabled = true;

    public int Value
    {
        get => _value;
        set
        {
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public WriteableBitmap? Image { get; init; }
    public ICommand IncrementCommand => this;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => Enabled;
    public void Execute(object? parameter) => Value++;
}
