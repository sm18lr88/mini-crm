namespace CallLedger.Cli;

internal static class CliReference
{
    private static readonly object CustomerProperties = new
    {
        name = new { type = "string", maxLength = 200 },
        dateOfBirth = new { type = new[] { "string", "null" }, format = "date" },
        ssn = new { type = new[] { "string", "null" }, maxLength = 100 },
        medicareId = new { type = new[] { "string", "null" }, maxLength = 100 },
        phone = new { type = new[] { "string", "null" }, maxLength = 50 },
        followUpAt = new { type = new[] { "string", "null" }, format = "date-time" },
        callReason = new { type = new[] { "string", "null" }, maxLength = 500 },
        notes = new { type = new[] { "string", "null" }, maxLength = 10000 },
        lastCalledAt = new { type = new[] { "string", "null" }, format = "date-time" }
    };

    public static object Help => new
    {
        commands = new[]
        {
            "help | --help", "schema", "customers list [--filter all|due|upcoming|unscheduled] [--offset N] [--limit N] [--reveal|--redact]",
            "customers search --input -|PATH [--filter all|due|upcoming|unscheduled] [--offset N] [--limit N] [--reveal|--redact]",
            "customers get ID [--reveal|--redact]", "customers add --input -|PATH [--expected-revision N] [--reveal|--redact]",
            "customers update ID --input -|PATH [--expected-revision N] [--reveal|--redact]", "customers delete ID --yes [--expected-revision N]",
            "customers mark-called ID [--input -|PATH] [--expected-revision N] [--reveal|--redact]", "preferences get",
            "preferences update --input -|PATH [--expected-revision N]", "backup --output PATH", "restore --input PATH --yes [--expected-revision N]"
        },
        input = new
        {
            customer = new[] { "name", "dateOfBirth", "ssn", "medicareId", "phone", "followUpAt", "callReason", "notes", "lastCalledAt" },
            markCalled = new[] { "at", "followUpAt", "notes", "callReason" },
            preferences = new[] { "showSensitiveByDefault", "compactMode", "alwaysOnTop" },
            search = new[] { "query" }
        },
        limits = new { inputBytes = 1048576, jsonDepth = 8, defaultLimit = 50, maximumLimit = 200 },
        exitCodes = new { success = 0, usage = 2, notFound = 3, storeLocked = 4, revisionConflict = 5, safeIo = 6, internalError = 10 }
    };

    public static object Schema => new Dictionary<string, object>
    {
        ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
        ["protocolVersion"] = 1,
        ["customerCreateRequest"] = new { type = "object", additionalProperties = false, required = new[] { "name" }, properties = CustomerProperties },
        ["customerPatchRequest"] = new { type = "object", additionalProperties = false, minProperties = 1, properties = CustomerProperties },
        ["markCalledRequest"] = new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                at = new { type = new[] { "string", "null" }, format = "date-time" },
                followUpAt = new { type = new[] { "string", "null" }, format = "date-time" },
                notes = new { type = new[] { "string", "null" }, maxLength = 10000 },
                callReason = new { type = new[] { "string", "null" }, maxLength = 500 }
            }
        },
        ["preferencesPatchRequest"] = new
        {
            type = "object",
            additionalProperties = false,
            minProperties = 1,
            properties = new
            {
                showSensitiveByDefault = new { type = "boolean" },
                compactMode = new { type = "boolean" },
                alwaysOnTop = new { type = "boolean" }
            }
        },
        ["searchRequest"] = new
        {
            type = "object",
            additionalProperties = false,
            required = new[] { "query" },
            properties = new { query = new { type = "string" } }
        },
        ["contracts"] = new { inputMaxBytes = 1048576, inputMaxDepth = 8, timestampsRequireOffset = true, outputTimestampsUtc = true, customerUpdateIsPatch = true, sensitiveFields = new[] { "dateOfBirth", "ssn", "medicareId" } },
        ["commands"] = Help
    };
}
