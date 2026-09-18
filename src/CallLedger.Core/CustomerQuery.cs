using System.Globalization;

namespace CallLedger.Core;

public static class CustomerQuery
{
    public static IReadOnlyList<Customer> Search(
        IEnumerable<Customer> customers,
        string query,
        CallFilter filter,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(query);

        var normalizedQuery = NormalizeIdentifier(query);
        return customers
            .Where(customer => MatchesFilter(customer, filter, now))
            .Where(customer => MatchesQuery(customer, query, normalizedQuery))
            .OrderBy(customer => customer.FollowUpAt is null)
            .ThenBy(customer => customer.FollowUpAt)
            .ThenBy(customer => customer.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(customer => customer.Id)
            .ToList();
    }

    private static bool MatchesFilter(Customer customer, CallFilter filter, DateTimeOffset now) => filter switch
    {
        CallFilter.All => true,
        CallFilter.Due => customer.FollowUpAt is { } followUp && followUp <= now,
        CallFilter.Upcoming => customer.FollowUpAt is { } followUp && followUp > now,
        CallFilter.Unscheduled => customer.FollowUpAt is null,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown call filter.")
    };

    private static bool MatchesQuery(Customer customer, string query, string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        var textMatch = customer.Name.Contains(query, comparison)
            || customer.Ssn.Contains(query, comparison)
            || customer.MedicareId.Contains(query, comparison)
            || customer.Phone.Contains(query, comparison)
            || customer.CallReason.Contains(query, comparison)
            || customer.Notes.Contains(query, comparison)
            || customer.Id.ToString().Contains(query, comparison)
            || MatchesDate(customer.DateOfBirth, query)
            || MatchesDateTime(customer.FollowUpAt, query)
            || MatchesDateTime(customer.LastCalledAt, query);

        return textMatch || (normalizedQuery.Length > 0 && (
            NormalizeIdentifier(customer.Ssn).Contains(normalizedQuery, comparison)
            || NormalizeIdentifier(customer.MedicareId).Contains(normalizedQuery, comparison)
            || NormalizeIdentifier(customer.Phone).Contains(normalizedQuery, comparison)));
    }

    private static bool MatchesDate(DateOnly? value, string query) => value is { } date
        && date.ToString("O", CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesDateTime(DateTimeOffset? value, string query) => value is { } dateTime
        && dateTime.ToString("O", CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeIdentifier(string value) => new(value
        .Where(char.IsLetterOrDigit)
        .Select(char.ToUpperInvariant)
        .ToArray());
}
