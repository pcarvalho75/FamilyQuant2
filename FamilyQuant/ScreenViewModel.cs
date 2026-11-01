using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FamilyQuant;

public abstract class ScreenViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        Changed(name);
        return true;
    }
    protected void ObserveChild(INotifyPropertyChanged child, string name)
    {
        var weak = new WeakReference<ScreenViewModel>(this);
        PropertyChangedEventHandler? handler = null;
        handler = (_, _) => { if (weak.TryGetTarget(out var model)) model.ChildChanged(name); else child.PropertyChanged -= handler; };
        child.PropertyChanged += handler;
    }
    protected virtual void ChildChanged(string name) => Changed(name);
}

public sealed class ScreenCommand(Action execute, Func<bool>? enabled = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => enabled?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class ScreenProgress(Action<string> report) : IProgress<string>
{
    public void Report(string message) => report(message);
}
