using CallLedger.Core;
using CallLedger.Desktop.Presentation;

namespace CallLedger.Desktop.ViewModels;

public sealed class CustomerRow : ObservableObject
{
    private readonly Customer _customer;
    private bool _showSensitive;
    private readonly DateTimeOffset _now;

    public CustomerRow(Customer customer, bool showSensitive, DateTimeOffset now)
    {
        _customer = customer;
        _showSensitive = showSensitive;
        _now = now;
    }

    public Customer Customer => _customer;
    public string Name => _customer.Name;
    public string Phone => _customer.Phone;
    public string Reason => _customer.CallReason;
    public string DateOfBirth => DisplayMasking.ForField(CustomerFields.DateOfBirth, _customer.DateOfBirth?.ToString("yyyy-MM-dd") ?? string.Empty, _showSensitive);
    public string Ssn => DisplayMasking.ForField(CustomerFields.Ssn, _customer.Ssn, _showSensitive);
    public string MedicareId => DisplayMasking.ForField(CustomerFields.MedicareId, _customer.MedicareId, _showSensitive);
    public string FollowUp => _customer.FollowUpAt?.ToLocalTime().ToString("MMM d, h:mm tt") ?? "Unscheduled";
    public string LastCalled => _customer.LastCalledAt?.ToLocalTime().ToString("MMM d, h:mm tt") ?? "Not called";
    public string CallState => GetCallState();
    public bool IsDue => _customer.FollowUpAt is { } followUpAt && followUpAt <= _now;

    public void SetShowSensitive(bool value)
    {
        if (!SetProperty(ref _showSensitive, value))
        {
            return;
        }

        OnPropertyChanged(nameof(DateOfBirth));
        OnPropertyChanged(nameof(Ssn));
        OnPropertyChanged(nameof(MedicareId));
    }

    private string GetCallState()
    {
        if (_customer.FollowUpAt is null)
        {
            return "Unscheduled";
        }

        return _customer.FollowUpAt <= _now ? "Due" : "Upcoming";
    }
}
