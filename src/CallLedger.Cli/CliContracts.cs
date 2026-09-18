using CallLedger.Core;

namespace CallLedger.Cli;

internal enum CliOperation
{
    Help,
    Schema,
    CustomersList,
    CustomersSearch,
    CustomersGet,
    CustomersAdd,
    CustomersUpdate,
    CustomersDelete,
    CustomersMarkCalled,
    PreferencesGet,
    PreferencesUpdate,
    Backup,
    Restore
}

internal sealed record CliCommand(
    CliOperation Operation,
    Guid? CustomerId = null,
    string? InputPath = null,
    string? OutputPath = null,
    CallFilter Filter = CallFilter.All,
    int Offset = 0,
    int Limit = 50,
    bool Reveal = false,
    bool Redact = false,
    bool Yes = false,
    long? ExpectedRevision = null);

internal sealed class CliException : Exception
{
    public CliException(int exitCode, string code, string message, object? details = null)
        : base(message)
    {
        ExitCode = exitCode;
        Code = code;
        Details = details;
    }

    public int ExitCode { get; }
    public string Code { get; }
    public object? Details { get; }
}

internal readonly record struct OptionalValue<T>(bool IsPresent, T? Value);

internal sealed record CustomerPatch(
    OptionalValue<string> Name,
    OptionalValue<DateOnly?> DateOfBirth,
    OptionalValue<string> Ssn,
    OptionalValue<string> MedicareId,
    OptionalValue<string> Phone,
    OptionalValue<DateTimeOffset?> FollowUpAt,
    OptionalValue<string> CallReason,
    OptionalValue<string> Notes,
    OptionalValue<DateTimeOffset?> LastCalledAt);

internal sealed record MarkCalledPatch(
    OptionalValue<DateTimeOffset?> At,
    OptionalValue<DateTimeOffset?> FollowUpAt,
    OptionalValue<string> Notes,
    OptionalValue<string> CallReason);

internal sealed record PreferencesPatch(
    OptionalValue<bool> ShowSensitiveByDefault,
    OptionalValue<bool> CompactMode,
    OptionalValue<bool> AlwaysOnTop);

internal sealed record CustomerOutput(
    Guid Id,
    string Name,
    string? DateOfBirth,
    string? Ssn,
    string? MedicareId,
    string Phone,
    string? FollowUpAt,
    string CallReason,
    string Notes,
    string? LastCalledAt);

internal sealed record PreferencesOutput(bool ShowSensitiveByDefault, bool CompactMode, bool AlwaysOnTop)
{
    public static PreferencesOutput From(Preferences preferences) => new(
        preferences.ShowSensitiveByDefault,
        preferences.CompactMode,
        preferences.AlwaysOnTop);
}
