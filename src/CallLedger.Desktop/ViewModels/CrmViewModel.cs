using System.Collections.ObjectModel;
using System.Windows.Input;
using CallLedger.Core;
using CallLedger.Desktop.Presentation;
using CallLedger.Desktop.Services;

namespace CallLedger.Desktop.ViewModels;

public sealed partial class CrmViewModel : ObservableObject, IDisposable
{
    private readonly StoreCoordinator _store;
    private readonly IUserDialogService _dialogs;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<ICanExecuteNotifier> _commands = [];
    private CrmDocument _document = new();
    private CustomerRow? _selectedRow;
    private string _searchText = string.Empty;
    private CallFilter _selectedFilter;
    private string _statusMessage = "Loading Call Ledger...";
    private string _errorMessage = string.Empty;
    private string _nextDueText = "No calls scheduled";
    private int _dueCount;
    private bool _isBusy;
    private bool _isLoaded;
    private bool _showSensitiveByDefault;
    private bool _compactMode;
    private bool _alwaysOnTop = true;
    private bool _nextCallSuggested;
    private bool _disposed;

    public CrmViewModel(StoreCoordinator store, IUserDialogService dialogs, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _dialogs = dialogs;
        _clock = clock ?? (() => DateTimeOffset.Now);
        Draft.Changed += OnDraftChanged;

        SaveCommand = Track(new AsyncRelayCommand(async () => { await SaveAsync(); }, CanModifyCustomer));
        NewCommand = Track(new AsyncRelayCommand(async () => { await NewAsync(); }, CanStartNew));
        MarkCalledCommand = Track(new AsyncRelayCommand(async () => { await MarkCalledAsync(); }, CanMarkCalled));
        DeleteCommand = Track(new AsyncRelayCommand(async () => { await DeleteAsync(); }, CanDelete));
        ExportBackupCommand = Track(new AsyncRelayCommand(async () => { await ExportBackupAsync(); }, CanUseStore));
        RestoreBackupCommand = Track(new AsyncRelayCommand(async () => { await RestoreBackupAsync(); }, CanRestoreBackup));
        ToggleCompactModeCommand = Track(new AsyncRelayCommand(async () => { await ToggleCompactModeAsync(); }, CanUseStore));
        ToggleAlwaysOnTopCommand = Track(new AsyncRelayCommand(async () => { await ToggleAlwaysOnTopAsync(); }, CanUseStore));
        CancelCommand = Track(new RelayCommand(CancelEdit, CanModifyCustomer));
        CopyFieldCommand = Track(new RelayCommand(CopyField, CanUseFieldAction));
        PasteFieldCommand = Track(new RelayCommand(PasteField, CanUseFieldAction));
        ToggleSensitiveCommand = Track(new RelayCommand(ToggleSensitiveField, CanUseSensitiveField));
        SetFilterCommand = Track(new RelayCommand(SetFilter, parameter => !IsBusy && parameter is string));
    }

    public ObservableCollection<CustomerRow> Rows { get; } = [];
    public CustomerDraft Draft { get; } = new();
    public ICommand SaveCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand MarkCalledCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ExportBackupCommand { get; }
    public ICommand RestoreBackupCommand { get; }
    public ICommand ToggleCompactModeCommand { get; }
    public ICommand ToggleAlwaysOnTopCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CopyFieldCommand { get; }
    public ICommand PasteFieldCommand { get; }
    public ICommand ToggleSensitiveCommand { get; }
    public ICommand SetFilterCommand { get; }

    public CustomerRow? SelectedRow => _selectedRow;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value) && _isLoaded)
            {
                RebuildRows();
            }
        }
    }

    public CallFilter SelectedFilter => _selectedFilter;
    public bool IsAllFilter => _selectedFilter is CallFilter.All;
    public bool IsDueFilter => _selectedFilter is CallFilter.Due;
    public bool IsUpcomingFilter => _selectedFilter is CallFilter.Upcoming;
    public bool IsUnscheduledFilter => _selectedFilter is CallFilter.Unscheduled;
    public bool ShowSensitiveByDefault => _showSensitiveByDefault;
    public bool CompactMode => _compactMode;
    public bool AlwaysOnTop => _alwaysOnTop;
    public bool IsBusy => _isBusy;
    public bool IsNotBusy => !_isBusy;
    public bool IsLoaded => _isLoaded;
    public bool IsEditorActive => Draft.IsActive;
    public bool HasUnsavedChanges => Draft.HasChanges;
    public bool HasSelection => _selectedRow is not null;
    public bool IsEmpty => _isLoaded && Rows.Count == 0;
    public bool HasCustomers => _document.Customers.Count > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);
    public string ErrorMessage => _errorMessage;
    public string StatusMessage => _statusMessage;
    public string SelectedCustomerName => _selectedRow?.Name ?? "No customer selected";
    public string NextDueText => _nextDueText;
    public int DueCount => _dueCount;
    public bool HasDueCalls => _dueCount > 0;
    public bool NextCallSuggested => _nextCallSuggested;
    public string DueStatus => _dueCount > 0 ? $"Due now: {_dueCount}" : _nextDueText == "No calls scheduled" ? "No calls due" : "Due soon";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Draft.Changed -= OnDraftChanged;
        _store.Dispose();
        _disposed = true;
    }

    private T Track<T>(T command) where T : ICanExecuteNotifier
    {
        _commands.Add(command);
        return command;
    }

    private bool CanUseStore() => _isLoaded && !IsBusy;
    private bool CanRestoreBackup() => !IsBusy;
    private bool CanStartNew() => _isLoaded && !IsBusy;
    private bool CanModifyCustomer() => _isLoaded && !IsBusy && Draft.IsActive;
    private bool CanMarkCalled() => CanModifyCustomer() && !Draft.IsNew;
    private bool CanDelete() => CanUseStore() && _selectedRow is not null && !Draft.IsNew;
    private bool CanUseFieldAction(object? parameter) => CanModifyCustomer() && parameter is string;
    private bool CanUseSensitiveField(object? parameter) => CanModifyCustomer()
        && parameter is string field && CustomerFields.IsSensitive(field);

    private void SetBusy(bool value)
    {
        if (SetProperty(ref _isBusy, value, nameof(IsBusy)))
        {
            OnPropertyChanged(nameof(IsNotBusy));
            RefreshCommandStates();
        }
    }

    private void OnDraftChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(IsEditorActive));
        RefreshCommandStates();
    }

    private void RefreshCommandStates()
    {
        foreach (var command in _commands)
        {
            command.RaiseCanExecuteChanged();
        }
    }
}
