namespace CallLedger.Desktop.ViewModels;

internal static class DisplayMasking
{
    public static string ForField(string field, string value, bool reveal)
    {
        if (string.IsNullOrWhiteSpace(value) || reveal || !CustomerFields.IsSensitive(field))
        {
            return value;
        }

        return field switch
        {
            CustomerFields.DateOfBirth => "••••-••-••",
            CustomerFields.Ssn => "•••-••-••••",
            CustomerFields.MedicareId => "•••••••••••",
            _ => "••••••",
        };
    }
}
