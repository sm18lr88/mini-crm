namespace CallLedger.Desktop.ViewModels;

public sealed partial class CustomerDraft
{
    private void NotifyAll()
    {
        foreach (var property in new[]
        {
            nameof(Name), nameof(Phone), nameof(FollowUpAt), nameof(CallReason), nameof(Notes),
            nameof(DateOfBirthDisplay), nameof(SsnDisplay), nameof(MedicareIdDisplay),
            nameof(IsDateOfBirthMasked), nameof(IsSsnMasked), nameof(IsMedicareIdMasked),
            nameof(LastCalledDisplay), nameof(IsNew), nameof(IsActive), nameof(FollowUpEdited), nameof(HasChanges),
        })
        {
            OnPropertyChanged(property);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void NotifySensitiveDisplays()
    {
        OnPropertyChanged(nameof(DateOfBirthDisplay));
        OnPropertyChanged(nameof(SsnDisplay));
        OnPropertyChanged(nameof(MedicareIdDisplay));
        OnPropertyChanged(nameof(IsDateOfBirthMasked));
        OnPropertyChanged(nameof(IsSsnMasked));
        OnPropertyChanged(nameof(IsMedicareIdMasked));
    }
}
