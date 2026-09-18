namespace CallLedger.Cli;

internal sealed record PreparedCliInput(
    CustomerPatch? CustomerPatch = null,
    MarkCalledPatch? MarkCalledPatch = null,
    PreferencesPatch? PreferencesPatch = null,
    string? SearchQuery = null);

internal static class CliInput
{
    public static PreparedCliInput Prepare(CliCommand command, TextReader input) => command.Operation switch
    {
        CliOperation.CustomersSearch => new(SearchQuery: CliJson.ReadSearchQuery(command.InputPath!, input)),
        CliOperation.CustomersAdd or CliOperation.CustomersUpdate => new(CustomerPatch: CliJson.ReadCustomerPatch(command.InputPath!, input, allowEmpty: false)),
        CliOperation.CustomersMarkCalled when command.InputPath is not null => new(MarkCalledPatch: CliJson.ReadMarkCalledPatch(command.InputPath, input)),
        CliOperation.PreferencesUpdate => new(PreferencesPatch: CliJson.ReadPreferencesPatch(command.InputPath!, input)),
        _ => new()
    };
}
