using System.Windows.Input;

namespace PrintTool.Ui.Shared.Mvvm;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}

/// <summary>
/// Variante de <see cref="RelayCommand"/> que recebe o item da linha como parâmetro
/// (ex.: "Revogar" numa lista, onde cada linha passa seu próprio <c>ClientId</c>).
/// </summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Func<T, bool>? _canExecute;

    public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(CastParameter(parameter)) ?? true;

    public void Execute(object? parameter) => _execute(CastParameter(parameter));

    // Usa "is" em vez de cast direto: funciona tanto pra T referência (vira null) quanto pra
    // T valor (ex. Guid, vira default) sem a ambiguidade de "T?" num genérico sem restrição
    // (que não vira Nullable<T> pra tipos valor — foi exatamente isso que quebrou o build).
    private static T CastParameter(object? parameter) => parameter is T value ? value : default!;
}

/// <summary>
/// Comando para operações assíncronas (descoberta, pareamento): fica desabilitado enquanto
/// a operação em andamento não termina, evitando disparo duplo por clique duplo.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute().ConfigureAwait(true);
        }
        finally
        {
            _isRunning = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
