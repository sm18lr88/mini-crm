using CallLedger.Core;

namespace CallLedger.Desktop.ViewModels;

public sealed partial class CrmViewModel
{
    public async Task<bool> LoadAsync()
    {
        return await RunBusyAsync(async () =>
        {
            var loaded = await _store.LoadAsync();
            _document = loaded;
            ApplyPreferences(loaded.Preferences);
            _isLoaded = true;
            RebuildRows();
            if (Rows.FirstOrDefault() is { } first)
            {
                SetSelectedRow(first);
                Draft.Load(first.Customer);
            }
            else
            {
                Draft.Clear();
            }

            OnPropertyChanged(nameof(IsLoaded));
            OnPropertyChanged(nameof(HasCustomers));
            SetStatus(loaded.Customers.Count == 0 ? "No customers yet. Create your first record." : "Call Ledger is ready.");
        }, "Call Ledger could not safely open its existing data. Restore a backup if recovery is needed.");
    }

    public async Task<bool> SaveAsync()
    {
        if (!CanModifyCustomer())
        {
            return false;
        }

        if (!TryGetValidatedDraft(out var customer))
        {
            return false;
        }

        var pending = ReplaceCustomer(_document, customer);
        return await RunBusyAsync(async () =>
        {
            var saved = await _store.SaveAsync(pending);
            _document = saved;
            ApplyPreferences(saved.Preferences);
            RebuildRows(customer.Id);
            Draft.Load(customer);
            _nextCallSuggested = false;
            OnPropertyChanged(nameof(NextCallSuggested));
            OnPropertyChanged(nameof(HasCustomers));
            SetStatus($"Saved {customer.Name}.");
        }, "The record could not be confirmed as saved. Review the open draft before retrying.");
    }

    public async Task<bool> MarkCalledAsync()
    {
        if (!CanMarkCalled() || !TryGetValidatedDraft(out var current))
        {
            return false;
        }

        var keepEditedFollowUp = Draft.FollowUpEdited;
        var marked = current with
        {
            LastCalledAt = _clock(),
            FollowUpAt = keepEditedFollowUp ? current.FollowUpAt : null,
        };
        var pending = ReplaceCustomer(_document, marked);
        return await RunBusyAsync(async () =>
        {
            var saved = await _store.SaveAsync(pending);
            _document = saved;
            ApplyPreferences(saved.Preferences);
            RebuildRows(marked.Id);
            Draft.Load(marked);
            _nextCallSuggested = true;
            OnPropertyChanged(nameof(NextCallSuggested));
            SetStatus(keepEditedFollowUp && marked.FollowUpAt is not null
                ? "Call marked. The edited follow-up was kept."
                : "Call marked. Add a new follow-up if another call is needed, then save.");
        }, "The call could not be confirmed as marked. Review the open draft before retrying.");
    }

    public async Task DeleteAsync()
    {
        if (!CanDelete() || _selectedRow is not { } selected || !_dialogs.ConfirmDelete(selected.Name))
        {
            return;
        }

        var pending = _document with { Customers = _document.Customers.Where(customer => customer.Id != selected.Customer.Id).ToList() };
        await RunBusyAsync(async () =>
        {
            var saved = await _store.SaveAsync(pending);
            _document = saved;
            ApplyPreferences(saved.Preferences);
            RebuildRows();
            SetSelectedRow(Rows.FirstOrDefault());
            if (_selectedRow is { } next)
            {
                Draft.Load(next.Customer);
            }
            else
            {
                Draft.Clear();
            }

            OnPropertyChanged(nameof(HasCustomers));
            SetStatus($"Deleted {selected.Name}.");
        }, "The delete could not be confirmed. Review the current records before retrying.");
    }

    public async Task ExportBackupAsync()
    {
        if (!CanUseStore())
        {
            return;
        }

        var path = _dialogs.SelectBackupExportPath();
        if (string.IsNullOrWhiteSpace(path) || !_dialogs.ConfirmBackupExport(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _store.ExportBackupAsync(path);
            SetStatus("Encrypted backup exported for this Windows account.");
        }, "The backup export could not be confirmed.");
    }

    public async Task RestoreBackupAsync()
    {
        if (!CanRestoreBackup())
        {
            return;
        }

        if (!await ResolveDraftAsync("restore a backup"))
        {
            return;
        }

        var path = _dialogs.SelectBackupRestorePath();
        if (string.IsNullOrWhiteSpace(path) || !_dialogs.ConfirmRestore())
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var restored = await _store.RestoreBackupAsync(path);
            _document = restored;
            var wasLoaded = _isLoaded;
            _isLoaded = true;
            ApplyPreferences(restored.Preferences);
            RebuildRows();
            SetSelectedRow(Rows.FirstOrDefault());
            if (_selectedRow is { } first)
            {
                Draft.Load(first.Customer);
            }
            else
            {
                Draft.Clear();
            }

            _nextCallSuggested = false;
            OnPropertyChanged(nameof(NextCallSuggested));
            if (!wasLoaded)
            {
                OnPropertyChanged(nameof(IsLoaded));
            }

            OnPropertyChanged(nameof(HasCustomers));
            SetStatus("Encrypted backup restored.");
        }, "The backup restore could not be confirmed. Review the current records before retrying.");
    }

    public async Task ToggleCompactModeAsync()
    {
        if (await ResolveDraftAsync("change the window mode"))
        {
            await UpdatePreferencesAsync(_document.Preferences with { CompactMode = !CompactMode });
        }
    }

    public Task ToggleAlwaysOnTopAsync()
        => UpdatePreferencesAsync(_document.Preferences with { AlwaysOnTop = !AlwaysOnTop });

    public Task SetShowSensitiveByDefaultAsync(bool value)
        => UpdatePreferencesAsync(_document.Preferences with { ShowSensitiveByDefault = value });

    private async Task UpdatePreferencesAsync(Preferences preferences)
    {
        if (!_isLoaded || preferences == _document.Preferences || IsBusy)
        {
            return;
        }

        var previous = _document.Preferences;
        ApplyPreferences(preferences);
        await RunBusyAsync(async () =>
        {
            _document = await _store.SaveAsync(_document with { Preferences = preferences });
            SetStatus("Preferences saved.");
        }, "The preference save could not be confirmed. Review display settings before retrying.", () => ApplyPreferences(previous));
    }

    private async Task<bool> RunBusyAsync(Func<Task> operation, string failure, Action? onFailure = null)
    {
        if (IsBusy)
        {
            return false;
        }

        SetBusy(true);
        ClearError();
        try
        {
            await operation();
            return true;
        }
        catch (CrmStoreException exception)
        {
            onFailure?.Invoke();
            SetError(IncludeStoreDetail(failure, exception));
            return false;
        }
        catch (Exception)
        {
            onFailure?.Invoke();
            SetError("Call Ledger could not complete that action. Review the current state before retrying.");
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string IncludeStoreDetail(string failure, CrmStoreException exception)
        => string.IsNullOrWhiteSpace(exception.Message) ? failure : $"{failure} Details: {exception.Message}";
}
