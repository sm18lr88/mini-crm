using CallLedger.Core;

namespace CallLedger.Cli;

public static partial class CliApplication
{
    private static (int ExitCode, string Code, string Message) MapStoreError(CrmStoreException exception)
    {
        if (HasSharingViolation(exception))
        {
            return (4, "STORE_LOCKED", "The CRM store is in use. Close Call Ledger before running this command.");
        }

        if (exception.Message.Contains("changed in another operation", StringComparison.Ordinal))
        {
            return (5, "REVISION_CONFLICT", "The CRM store changed before the command could save.");
        }

        return (6, "STORE_UNREADABLE", "The encrypted CRM store or backup could not be read or safely updated.");
    }

    private static bool HasSharingViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is IOException && (current.HResult & 0xFFFF) is 32 or 33)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteSuccess(TextWriter output, long? revision, object data, string[] redactedFields) =>
        output.WriteLine(CliJson.Serialize(new { protocolVersion = 1, ok = true, revision, data, redactedFields }));

    private static void WriteFailure(TextWriter error, int exitCode, string code, string message, object? details) =>
        error.WriteLine(CliJson.Serialize(new { protocolVersion = 1, ok = false, error = new { code, message, details } }));
}
