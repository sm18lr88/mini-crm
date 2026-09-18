using CallLedger.Core;
using CallLedger.Vault;

namespace CallLedger.Cli;

public static partial class CliApplication
{
    public static Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        string dataDirectory,
        Func<DateTimeOffset> clock)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
            ArgumentNullException.ThrowIfNull(clock);

            var command = CliArguments.Parse(args);
            if (command.Operation is CliOperation.Help or CliOperation.Schema)
            {
                WriteSuccess(output, null, command.Operation == CliOperation.Help ? CliReference.Help : CliReference.Schema, []);
                return Task.FromResult(0);
            }

            var preparedInput = CliInput.Prepare(command, input);
            using var store = new EncryptedCrmStore(dataDirectory);
            Execute(command, preparedInput, store, output, dataDirectory, clock);
            return Task.FromResult(0);
        }
        catch (CliException exception)
        {
            WriteFailure(error, exception.ExitCode, exception.Code, exception.Message, exception.Details);
            return Task.FromResult(exception.ExitCode);
        }
        catch (CrmStoreException exception)
        {
            var (exitCode, code, message) = MapStoreError(exception);
            WriteFailure(error, exitCode, code, message, null);
            return Task.FromResult(exitCode);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            WriteFailure(error, 6, "SAFE_IO", "The requested file operation could not be completed safely.", null);
            return Task.FromResult(6);
        }
        catch (Exception)
        {
            WriteFailure(error, 10, "INTERNAL_ERROR", "The command could not be completed.", null);
            return Task.FromResult(10);
        }
    }

    private static void Execute(
        CliCommand command,
        PreparedCliInput preparedInput,
        ICrmStore store,
        TextWriter output,
        string dataDirectory,
        Func<DateTimeOffset> clock)
    {
        switch (command.Operation)
        {
            case CliOperation.CustomersList:
                ListCustomers(command, store.Load(), "", output, clock);
                return;
            case CliOperation.CustomersSearch:
                ListCustomers(command, store.Load(), preparedInput.SearchQuery!, output, clock);
                return;
            case CliOperation.CustomersGet:
                GetCustomer(command, store.Load(), output);
                return;
            case CliOperation.CustomersAdd:
                AddCustomer(command, preparedInput.CustomerPatch!, store, output);
                return;
            case CliOperation.CustomersUpdate:
                UpdateCustomer(command, preparedInput.CustomerPatch!, store, output);
                return;
            case CliOperation.CustomersDelete:
                DeleteCustomer(command, store, output);
                return;
            case CliOperation.CustomersMarkCalled:
                MarkCalled(command, preparedInput.MarkCalledPatch, store, output, clock);
                return;
            case CliOperation.PreferencesGet:
                GetPreferences(store.Load(), output);
                return;
            case CliOperation.PreferencesUpdate:
                UpdatePreferences(command, preparedInput.PreferencesPatch!, store, output);
                return;
            case CliOperation.Backup:
                Backup(command, store, output, dataDirectory);
                return;
            case CliOperation.Restore:
                Restore(command, store, output);
                return;
            default:
                throw new InvalidOperationException("Unsupported parsed command.");
        }
    }
}
