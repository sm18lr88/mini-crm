using System.Globalization;
using CallLedger.Core;
using CallLedger.Desktop.Presentation;

namespace CallLedger.Desktop.ViewModels;

public sealed partial class CustomerDraft : ObservableObject
{
    private readonly HashSet<string> _revealedFields = new(StringComparer.Ordinal);
    private Customer? _source;
    private Guid _id = Guid.NewGuid();
    private string _name = string.Empty;
    private string _dateOfBirth = string.Empty;
    private string _ssn = string.Empty;
    private string _medicareId = string.Empty;
    private string _phone = string.Empty;
    private string _followUpAt = string.Empty;
    private string _sourceFollowUpAtText = string.Empty;
    private string _callReason = string.Empty;
    private string _notes = string.Empty;
    private DateTimeOffset? _lastCalledAt;
    private bool _isNew;
    private bool _isActive;
    private bool _followUpEdited;
    private bool _showSensitiveByDefault;

    public event EventHandler? Changed;

    public bool IsNew => _isNew;
    public bool IsActive => _isActive;
    public bool FollowUpEdited => _followUpEdited;
    public bool HasChanges => _isActive && (_isNew ? HasValues() : !MatchesSource());

    public string Name { get => _name; set => Update(ref _name, value, nameof(Name)); }
    public string Phone { get => _phone; set => Update(ref _phone, value, nameof(Phone)); }
    public string FollowUpAt { get => _followUpAt; set => UpdateFollowUp(value); }
    public string CallReason { get => _callReason; set => Update(ref _callReason, value, nameof(CallReason)); }
    public string Notes { get => _notes; set => Update(ref _notes, value, nameof(Notes)); }

    public string DateOfBirthDisplay
    {
        get => DisplayMasking.ForField(CustomerFields.DateOfBirth, _dateOfBirth, CanShow(CustomerFields.DateOfBirth));
        set => UpdateSensitive(CustomerFields.DateOfBirth, value);
    }

    public string SsnDisplay
    {
        get => DisplayMasking.ForField(CustomerFields.Ssn, _ssn, CanShow(CustomerFields.Ssn));
        set => UpdateSensitive(CustomerFields.Ssn, value);
    }

    public string MedicareIdDisplay
    {
        get => DisplayMasking.ForField(CustomerFields.MedicareId, _medicareId, CanShow(CustomerFields.MedicareId));
        set => UpdateSensitive(CustomerFields.MedicareId, value);
    }

    public bool IsDateOfBirthMasked => !CanShow(CustomerFields.DateOfBirth);
    public bool IsSsnMasked => !CanShow(CustomerFields.Ssn);
    public bool IsMedicareIdMasked => !CanShow(CustomerFields.MedicareId);
    public string LastCalledDisplay => _lastCalledAt?.ToLocalTime().ToString("MMM d, yyyy h:mm tt") ?? "No call recorded";

    public void CreateNew()
    {
        _source = null;
        _id = Guid.NewGuid();
        _name = _dateOfBirth = _ssn = _medicareId = _phone = _followUpAt = _callReason = _notes = string.Empty;
        _sourceFollowUpAtText = string.Empty;
        _lastCalledAt = null;
        _isNew = true;
        _isActive = true;
        _followUpEdited = false;
        _revealedFields.Clear();
        NotifyAll();
    }

    public void Load(Customer customer)
    {
        _source = customer;
        _id = customer.Id;
        _name = customer.Name;
        _dateOfBirth = customer.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        _ssn = customer.Ssn;
        _medicareId = customer.MedicareId;
        _phone = customer.Phone;
        _sourceFollowUpAtText = FormatFollowUp(customer.FollowUpAt);
        _followUpAt = _sourceFollowUpAtText;
        _callReason = customer.CallReason;
        _notes = customer.Notes;
        _lastCalledAt = customer.LastCalledAt;
        _isNew = false;
        _isActive = true;
        _followUpEdited = false;
        _revealedFields.Clear();
        NotifyAll();
    }

    public void Clear()
    {
        _source = null;
        _id = Guid.NewGuid();
        _name = _dateOfBirth = _ssn = _medicareId = _phone = _followUpAt = _callReason = _notes = string.Empty;
        _sourceFollowUpAtText = string.Empty;
        _lastCalledAt = null;
        _isNew = false;
        _isActive = false;
        _followUpEdited = false;
        _revealedFields.Clear();
        NotifyAll();
    }

    public void SetSensitiveDefault(bool showSensitiveByDefault)
    {
        if (!SetProperty(ref _showSensitiveByDefault, showSensitiveByDefault))
        {
            return;
        }

        _revealedFields.Clear();
        NotifySensitiveDisplays();
    }

    public void ToggleReveal(string field)
    {
        if (!CustomerFields.IsSensitive(field))
        {
            return;
        }

        if (!_revealedFields.Add(field))
        {
            _revealedFields.Remove(field);
        }

        NotifySensitiveDisplays();
    }

    public string GetActualValue(string field) => field switch
    {
        CustomerFields.Name => _name,
        CustomerFields.DateOfBirth => _dateOfBirth,
        CustomerFields.Ssn => _ssn,
        CustomerFields.MedicareId => _medicareId,
        CustomerFields.Phone => _phone,
        CustomerFields.FollowUpAt => _followUpAt,
        CustomerFields.CallReason => _callReason,
        CustomerFields.Notes => _notes,
        _ => string.Empty,
    };

    public void SetActualValue(string field, string value)
    {
        switch (field)
        {
            case CustomerFields.Name: Name = value; break;
            case CustomerFields.DateOfBirth: Update(ref _dateOfBirth, value, nameof(DateOfBirthDisplay)); break;
            case CustomerFields.Ssn: Update(ref _ssn, value, nameof(SsnDisplay)); break;
            case CustomerFields.MedicareId: Update(ref _medicareId, value, nameof(MedicareIdDisplay)); break;
            case CustomerFields.Phone: Phone = value; break;
            case CustomerFields.FollowUpAt: FollowUpAt = value; break;
            case CustomerFields.CallReason: CallReason = value; break;
            case CustomerFields.Notes: Notes = value; break;
        }
    }

    public bool TryBuild(out Customer? customer, out string error)
    {
        if (!TryParseDate(_dateOfBirth, out var dateOfBirth))
        {
            customer = null;
            error = "Date of birth must be a valid date, for example 1960-04-28.";
            return false;
        }

        DateTimeOffset? followUpAt;
        if (_source is { } source && string.Equals(_followUpAt, _sourceFollowUpAtText, StringComparison.Ordinal))
        {
            followUpAt = source.FollowUpAt;
        }
        else if (!TryParseFollowUp(_followUpAt, out followUpAt))
        {
            customer = null;
            error = "When to call must be a valid local date and time, for example 2026-09-18 09:00.";
            return false;
        }

        customer = new Customer
        {
            Id = _id,
            Name = _name,
            DateOfBirth = dateOfBirth,
            Ssn = _ssn,
            MedicareId = _medicareId,
            Phone = _phone,
            FollowUpAt = followUpAt,
            CallReason = _callReason,
            Notes = _notes,
            LastCalledAt = _lastCalledAt,
        };
        error = string.Empty;
        return true;
    }

    private bool CanShow(string field) => _showSensitiveByDefault != _revealedFields.Contains(field);

    private void UpdateSensitive(string field, string value)
    {
        if (CanShow(field))
        {
            SetActualValue(field, value);
        }
    }

    private void UpdateFollowUp(string value)
    {
        if (SetProperty(ref _followUpAt, value, nameof(FollowUpAt)))
        {
            UpdateFollowUpEdited();
            OnPropertyChanged(nameof(HasChanges));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Update(ref string storage, string value, string propertyName, string? displayProperty = null)
    {
        if (SetProperty(ref storage, value, propertyName))
        {
            if (displayProperty is not null)
            {
                OnPropertyChanged(displayProperty);
            }

            OnPropertyChanged(nameof(HasChanges));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateFollowUpEdited()
    {
        var edited = _source is not { } source
            ? !string.IsNullOrWhiteSpace(_followUpAt)
            : !string.Equals(_followUpAt, _sourceFollowUpAtText, StringComparison.Ordinal)
                && (!TryParseFollowUp(_followUpAt, out var followUpAt) || followUpAt != source.FollowUpAt);
        if (_followUpEdited == edited)
        {
            return;
        }

        _followUpEdited = edited;
        OnPropertyChanged(nameof(FollowUpEdited));
    }

    private static string FormatFollowUp(DateTimeOffset? followUpAt)
        => followUpAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    private bool MatchesSource()
    {
        if (_source is null || !TryBuild(out var candidate, out _))
        {
            return false;
        }

        return candidate == _source;
    }

    private bool HasValues()
        => !string.IsNullOrWhiteSpace(_name)
            || !string.IsNullOrWhiteSpace(_dateOfBirth)
            || !string.IsNullOrWhiteSpace(_ssn)
            || !string.IsNullOrWhiteSpace(_medicareId)
            || !string.IsNullOrWhiteSpace(_phone)
            || !string.IsNullOrWhiteSpace(_followUpAt)
            || !string.IsNullOrWhiteSpace(_callReason)
            || !string.IsNullOrWhiteSpace(_notes);

}
