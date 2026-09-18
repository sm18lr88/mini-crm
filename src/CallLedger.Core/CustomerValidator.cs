namespace CallLedger.Core;

public static class CustomerValidator
{
    public static IReadOnlyList<string> Validate(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var errors = new List<string>();
        Require(customer.Name, 200, "Name", errors, required: true);
        Require(customer.Ssn, 100, "SSN", errors, required: false);
        Require(customer.MedicareId, 100, "Medicare ID", errors, required: false);
        Require(customer.Phone, 50, "Phone", errors, required: false);
        Require(customer.CallReason, 500, "Call reason", errors, required: false);
        Require(customer.Notes, 10_000, "Notes", errors, required: false);

        if (customer.DateOfBirth is { } dateOfBirth
            && dateOfBirth > DateOnly.FromDateTime(DateTime.Now))
        {
            errors.Add("Date of birth cannot be in the future.");
        }

        return errors;
    }

    private static void Require(string value, int maximumLength, string label, List<string> errors, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} is required.");
        }

        if (value.Length > maximumLength)
        {
            errors.Add($"{label} must be {maximumLength} characters or fewer.");
        }
    }
}
