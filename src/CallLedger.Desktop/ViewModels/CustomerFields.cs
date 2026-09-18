namespace CallLedger.Desktop.ViewModels;

public static class CustomerFields
{
    public const string Name = nameof(Name);
    public const string DateOfBirth = nameof(DateOfBirth);
    public const string Ssn = nameof(Ssn);
    public const string MedicareId = nameof(MedicareId);
    public const string Phone = nameof(Phone);
    public const string FollowUpAt = nameof(FollowUpAt);
    public const string CallReason = nameof(CallReason);
    public const string Notes = nameof(Notes);

    public static bool IsSensitive(string field)
        => field is DateOfBirth or Ssn or MedicareId;
}
