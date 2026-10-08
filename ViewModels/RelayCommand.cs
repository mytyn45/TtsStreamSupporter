using System;
using System.Windows.Input;

namespace TtsStreamSupporter.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        _execute();
    }
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter)
    {
        if (_canExecute == null) return true;
        if (parameter is T t) return _canExecute(t);
        if (parameter == null && default(T) == null) return _canExecute(default);
        return true;
    }

    public void Execute(object? parameter)
    {
        if (parameter is T t)
        {
            _execute(t);
        }
        else if (parameter == null && default(T) == null)
        {
            _execute(default);
        }
    }
}