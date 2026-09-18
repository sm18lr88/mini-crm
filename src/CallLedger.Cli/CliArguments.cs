using CallLedger.Core;

namespace CallLedger.Cli;

internal static class CliArguments
{
    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0 || args is ["help"] or ["--help"])
        {
            return new(CliOperation.Help);
        }

        if (args is ["schema"])
        {
            return new(CliOperation.Schema);
        }

        if (args[0] == "customers")
        {
            return ParseCustomers(args[1..]);
        }

        if (args[0] == "preferences")
        {
            return ParsePreferences(args[1..]);
        }

        return args[0] switch
        {
            "backup" => ParseBackup(args[1..]),
            "restore" => ParseRestore(args[1..]),
            _ => Usage()
        };
    }

    private static CliCommand ParseCustomers(string[] args)
    {
        if (args.Length == 0)
        {
            return Usage();
        }

        return args[0] switch
        {
            "list" => ParseCustomerResults(CliOperation.CustomersList, args[1..], false),
            "search" => ParseCustomerResults(CliOperation.CustomersSearch, args[1..], true),
            "get" => ParseCustomerGet(args[1..]),
            "add" => ParseCustomerMutation(CliOperation.CustomersAdd, null, args[1..], true),
            "update" => ParseCustomerMutation(CliOperation.CustomersUpdate, ReadGuid(args, 1), args[2..], true),
            "delete" => ParseDelete(args[1..]),
            "mark-called" => ParseCustomerMutation(CliOperation.CustomersMarkCalled, ReadGuid(args, 1), args[2..], false),
            _ => Usage()
        };
    }

    private static CliCommand ParseCustomerGet(string[] args)
    {
        var id = ReadGuid(args, 0);
        var flags = new FlagReader(args[1..]);
        flags.ReadCustomerDisplay();
        flags.EnsureComplete();
        return new(CliOperation.CustomersGet, id, Reveal: flags.Reveal, Redact: flags.Redact);
    }

    private static CliCommand ParseCustomerResults(CliOperation operation, string[] args, bool requiresInput)
    {
        var flags = new FlagReader(args);
        if (requiresInput)
        {
            flags.ReadInput(required: true);
        }

        flags.ReadListOptions();
        flags.ReadCustomerDisplay();
        flags.EnsureComplete();
        return new(operation, InputPath: flags.InputPath, Filter: flags.Filter, Offset: flags.Offset, Limit: flags.Limit, Reveal: flags.Reveal, Redact: flags.Redact);
    }

    private static CliCommand ParseCustomerMutation(CliOperation operation, Guid? id, string[] args, bool requiresInput)
    {
        var flags = new FlagReader(args);
        flags.ReadInput(required: requiresInput);
        flags.ReadExpectedRevision();
        flags.ReadCustomerDisplay();
        flags.EnsureComplete();
        return new(operation, id, flags.InputPath, Reveal: flags.Reveal, Redact: flags.Redact, ExpectedRevision: flags.ExpectedRevision);
    }

    private static CliCommand ParseDelete(string[] args)
    {
        var id = ReadGuid(args, 0);
        var flags = new FlagReader(args[1..]);
        flags.ReadYes();
        flags.ReadExpectedRevision();
        flags.EnsureComplete();
        if (!flags.Yes)
        {
            return Confirmation();
        }

        return new(CliOperation.CustomersDelete, id, Yes: true, ExpectedRevision: flags.ExpectedRevision);
    }

    private static CliCommand ParsePreferences(string[] args)
    {
        if (args.Length == 1 && args[0] == "get")
        {
            return new(CliOperation.PreferencesGet);
        }

        if (args.Length > 0 && args[0] == "update")
        {
            var flags = new FlagReader(args[1..]);
            flags.ReadInput(required: true);
            flags.ReadExpectedRevision();
            flags.EnsureComplete();
            return new(CliOperation.PreferencesUpdate, InputPath: flags.InputPath, ExpectedRevision: flags.ExpectedRevision);
        }

        return Usage();
    }

    private static CliCommand ParseBackup(string[] args)
    {
        var flags = new FlagReader(args);
        flags.ReadOutput();
        flags.EnsureComplete();
        return new(CliOperation.Backup, OutputPath: flags.OutputPath);
    }

    private static CliCommand ParseRestore(string[] args)
    {
        var flags = new FlagReader(args);
        flags.ReadInput(required: true);
        flags.ReadYes();
        flags.ReadExpectedRevision();
        flags.EnsureComplete();
        if (!flags.Yes)
        {
            return Confirmation();
        }

        return new(CliOperation.Restore, InputPath: flags.InputPath, Yes: true, ExpectedRevision: flags.ExpectedRevision);
    }

    private static Guid ReadGuid(string[] args, int index)
    {
        if (args.Length <= index || !Guid.TryParse(args[index], out var id) || id == Guid.Empty)
        {
            throw new CliException(2, "USAGE", "A non-empty customer ID is required.", new { fields = new[] { "id" } });
        }

        return id;
    }

    private static CliCommand Usage() => throw new CliException(2, "USAGE", "Command arguments are invalid. Use help for the command contract.");
    private static CliCommand Confirmation() => throw new CliException(2, "CONFIRMATION_REQUIRED", "This destructive operation requires --yes.");
}
