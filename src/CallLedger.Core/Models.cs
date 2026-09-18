namespace CallLedger.Core;

public sealed record Customer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public DateOnly? DateOfBirth { get; init; }
    public string Ssn { get; init; } = "";
    public string MedicareId { get; init; } = "";
    public string Phone { get; init; } = "";
    public DateTimeOffset? FollowUpAt { get; init; }
    public string CallReason { get; init; } = "";
    public string Notes { get; init; } = "";
    public DateTimeOffset? LastCalledAt { get; init; }
}

public sealed record Preferences
{
    public bool ShowSensitiveByDefault { get; init; } = false;
    public bool CompactMode { get; init; } = false;
    public bool AlwaysOnTop { get; init; } = true;
}

public sealed record CrmDocument
{
    public int SchemaVersion { get; init; } = 1;
    public long Revision { get; init; } = 0;
    public List<Customer> Customers { get; init; } = [];
    public Preferences Preferences { get; init; } = new();
}

public enum CallFilter
{
    All,
    Due,
    Upcoming,
    Unscheduled
}
