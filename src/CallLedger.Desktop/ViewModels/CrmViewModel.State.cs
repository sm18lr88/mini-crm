using CallLedger.Core;
using CallLedger.Desktop.Services;

namespace CallLedger.Desktop.ViewModels;

public sealed partial class CrmViewModel
{
    public async Task<bool> SelectCustomerAsync(CustomerRow? row)
    {
        if (!_isLoaded || IsBusy)
        {
            return false;
        }

        if (ReferenceEquals(row, _selectedRow))
        {
            return true;
        }

        var desiredCustomerId = row?.Customer.Id;
        if (!await ResolveDraftAsync("select another customer"))
        {
            return false;
        }

        var currentRow = desiredCustomerId is { } id
            ? Rows.FirstOrDefault(candidate => candidate.Customer.Id == id)
            : null;
        var currentCustomer = desiredCustomerId is { } selectedCustomerId
            ? _document.Customers.FirstOrDefault(customer => customer.Id == selectedCustomerId)
            : null;
        SetSelectedRow(currentRow ?? (currentCustomer is null
            ? null
            : new CustomerRow(currentCustomer, _showSensitiveByDefault, _clock())));

        if (currentCustomer is null)
        {
            Draft.Clear();
        }
        else
        {
            Draft.Load(currentCustomer);
        }

        _nextCallSuggested = false;
        OnPropertyChanged(nameof(NextCallSuggested));
        return true;
    }

    public async Task NewAsync()
    {
        if (!CanStartNew())
        {
            return;
        }

        if (!await ResolveDraftAsync("create a new customer"))
        {
            return;
        }

        SetSelectedRow(null);
        Draft.CreateNew();
        _nextCallSuggested = false;
        OnPropertyChanged(nameof(NextCallSuggested));
        SetStatus("New customer draft. Save when it is ready.");
    }

    public void CancelEdit()
    {
        if (!Draft.IsActive)
        {
            return;
        }

        if (Draft.IsNew)
        {
            Draft.Clear();
            SetSelectedRow(Rows.FirstOrDefault());
            if (_selectedRow is { } first)
            {
                Draft.Load(first.Customer);
            }
        }
        else if (_selectedRow is { } selected)
        {
            Draft.Load(selected.Customer);
        }

        _nextCallSuggested = false;
        OnPropertyChanged(nameof(NextCallSuggested));
        SetStatus("Draft changes cancelled.");
    }

    public Task<bool> CanCloseAsync() => ResolveDraftAsync("close Call Ledger");

    public void RefreshTimeSensitiveState()
    {
        if (!_isLoaded || IsBusy)
        {
            return;
        }

        RebuildRows();
    }

    public void CopyField(object? parameter)
    {
        if (!CanUseFieldAction(parameter) || parameter is not string field)
        {
            return;
        }

        var value = Draft.GetActualValue(field);
        if (string.IsNullOrWhiteSpace(value))
        {
            SetStatus("There is no value to copy.");
            return;
        }

        if (_dialogs.TryCopyText(value, out var message))
        {
            SetStatus(CustomerFields.IsSensitive(field)
                ? "Copied the actual sensitive value. Windows Clipboard history may retain it."
                : "Copied field value.");
            return;
        }

        SetError(string.IsNullOrWhiteSpace(message) ? "Windows Clipboard is busy. The value was not copied." : message);
    }

    public void PasteField(object? parameter)
    {
        if (!CanUseFieldAction(parameter) || parameter is not string field)
        {
            return;
        }

        if (_dialogs.TryPasteText(out var value, out var message))
        {
            Draft.SetActualValue(field, value);
            SetStatus("Pasted into the draft. Save to keep the change.");
            return;
        }

        SetError(string.IsNullOrWhiteSpace(message) ? "Windows Clipboard is busy. Nothing was pasted." : message);
    }

    public void ToggleSensitiveField(object? parameter)
    {
        if (CanUseSensitiveField(parameter) && parameter is string field)
        {
            Draft.ToggleReveal(field);
        }
    }

    public void SetFilter(object? parameter)
    {
        if (IsBusy || parameter is not string text || !Enum.TryParse<CallFilter>(text, out var filter))
        {
            return;
        }

        if (!SetProperty(ref _selectedFilter, filter, nameof(SelectedFilter)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsDueFilter));
        OnPropertyChanged(nameof(IsUpcomingFilter));
        OnPropertyChanged(nameof(IsUnscheduledFilter));
        RebuildRows();
    }

    private async Task<bool> ResolveDraftAsync(string action)
    {
        if (!Draft.HasChanges)
        {
            return true;
        }

        var decision = _dialogs.ResolveUnsavedChanges(action);
        if (decision == PendingChangesDecision.Save)
        {
            return await SaveAsync();
        }

        if (decision == PendingChangesDecision.Discard)
        {
            CancelEdit();
            return true;
        }

        return false;
    }

    private bool TryGetValidatedDraft(out Customer customer)
    {
        if (!Draft.TryBuild(out var candidate, out var parsingError) || candidate is null)
        {
            customer = new Customer();
            SetError(parsingError);
            return false;
        }

        var validationErrors = CustomerValidator.Validate(candidate);
        if (validationErrors.Count > 0)
        {
            customer = new Customer();
            SetError(string.Join(Environment.NewLine, validationErrors));
            return false;
        }

        customer = candidate;
        return true;
    }

    private static CrmDocument ReplaceCustomer(CrmDocument document, Customer customer)
    {
        var customers = document.Customers.ToList();
        var index = customers.FindIndex(existing => existing.Id == customer.Id);
        if (index < 0)
        {
            customers.Add(customer);
        }
        else
        {
            customers[index] = customer;
        }

        return document with { Customers = customers };
    }

    private void RebuildRows(Guid? selectedId = null)
    {
        var selectedCustomerId = selectedId ?? _selectedRow?.Customer.Id;
        var now = _clock();
        var customers = CustomerQuery.Search(_document.Customers, _searchText, _selectedFilter, now);
        Rows.Clear();
        foreach (var customer in customers)
        {
            Rows.Add(new CustomerRow(customer, _showSensitiveByDefault, now));
        }

        var nextDue = _document.Customers
            .Where(customer => customer.FollowUpAt is not null)
            .OrderBy(customer => customer.FollowUpAt)
            .FirstOrDefault();
        _dueCount = _document.Customers.Count(customer => customer.FollowUpAt is { } followUp && followUp <= now);
        _nextDueText = nextDue?.FollowUpAt is { } followUpAt
            ? $"Next: {nextDue.Name} · {followUpAt.ToLocalTime():MMM d, h:mm tt}"
            : "No calls scheduled";
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(DueCount));
        OnPropertyChanged(nameof(HasDueCalls));
        OnPropertyChanged(nameof(DueStatus));
        OnPropertyChanged(nameof(NextDueText));

        if (selectedCustomerId is not { } id)
        {
            return;
        }

        if (Rows.FirstOrDefault(row => row.Customer.Id == id) is { } row)
        {
            SetSelectedRow(row);
            return;
        }

        if (_document.Customers.FirstOrDefault(customer => customer.Id == id) is { } selectedCustomer)
        {
            SetSelectedRow(new CustomerRow(selectedCustomer, _showSensitiveByDefault, now));
            return;
        }

        SetSelectedRow(null);
    }

    private void ApplyPreferences(Preferences preferences)
    {
        _showSensitiveByDefault = preferences.ShowSensitiveByDefault;
        _compactMode = preferences.CompactMode;
        _alwaysOnTop = preferences.AlwaysOnTop;
        Draft.SetSensitiveDefault(_showSensitiveByDefault);
        foreach (var row in Rows)
        {
            row.SetShowSensitive(_showSensitiveByDefault);
        }

        OnPropertyChanged(nameof(ShowSensitiveByDefault));
        OnPropertyChanged(nameof(CompactMode));
        OnPropertyChanged(nameof(AlwaysOnTop));
    }

    private void SetSelectedRow(CustomerRow? value)
    {
        if (SetProperty(ref _selectedRow, value, nameof(SelectedRow)))
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedCustomerName));
            RefreshCommandStates();
        }
    }

    private void SetStatus(string value)
    {
        ClearError();
        SetProperty(ref _statusMessage, value, nameof(StatusMessage));
    }

    private void SetError(string value)
    {
        SetProperty(ref _errorMessage, value, nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
        SetProperty(ref _statusMessage, "Action needs attention.", nameof(StatusMessage));
    }

    private void ClearError()
    {
        if (SetProperty(ref _errorMessage, string.Empty, nameof(ErrorMessage)))
        {
            OnPropertyChanged(nameof(HasError));
        }
    }
}
