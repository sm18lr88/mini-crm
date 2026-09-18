using System.Text.Json;
using CallLedger.Cli;
using CallLedger.Vault;

namespace CallLedger.Cli.Tests;

internal static class Program
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            (nameof(HelpAndSchemaDoNotOpenStore), HelpAndSchemaDoNotOpenStore),
            (nameof(ListMissingStoreDoesNotCreateVault), ListMissingStoreDoesNotCreateVault),
            (nameof(CliInputLifecycleTests.InvalidInputDoesNotOpenStore), CliInputLifecycleTests.InvalidInputDoesNotOpenStore),
            (nameof(CliInputLifecycleTests.ListRejectsInputAndSchemaUsesStandardKeyword), CliInputLifecycleTests.ListRejectsInputAndSchemaUsesStandardKeyword),
            (nameof(CrudPersistsAndPatchDistinguishesNullFromMissing), CrudPersistsAndPatchDistinguishesNullFromMissing),
            (nameof(QueryPagingAndDueOrderingMatchCore), QueryPagingAndDueOrderingMatchCore),
            (nameof(SensitiveOutputHonorsPreferenceRevealAndRedact), SensitiveOutputHonorsPreferenceRevealAndRedact),
            (nameof(ValidationAndRevisionConflictLeaveBytesUnchanged), ValidationAndRevisionConflictLeaveBytesUnchanged),
            (nameof(MarkCalledClearsOldScheduleAndKeepsExplicitNewSchedule), MarkCalledClearsOldScheduleAndKeepsExplicitNewSchedule),
            (nameof(DeleteAndRestoreRequireConfirmation), DeleteAndRestoreRequireConfirmation),
            (nameof(StoreLifetimeLockMapsToLockedExit), StoreLifetimeLockMapsToLockedExit),
            (nameof(BackupRestoreAndNoOverwriteProtectData), BackupRestoreAndNoOverwriteProtectData)
        };

        var failures = 0;
        foreach (var (name, run) in tests)
        {
            try
            {
                run();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void HelpAndSchemaDoNotOpenStore()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "missing");
        var help = Run(data, "help");
        var schema = Run(data, "schema");
        AssertEqual(0, help.ExitCode, "help exit");
        AssertEqual(0, schema.ExitCode, "schema exit");
        AssertFalse(Directory.Exists(data), "help/schema do not create data directory");
        AssertJsonSuccess(help.Output, "help");
        AssertJsonSuccess(schema.Output, "schema");
    }

    private static void ListMissingStoreDoesNotCreateVault()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        var result = Run(data, "customers", "list");
        AssertEqual(0, result.ExitCode, "list exit");
        AssertFalse(File.Exists(Path.Combine(data, "call-ledger.vault")), "list does not create vault");
        AssertJsonSuccess(result.Output, "list");
    }

    private static void CrudPersistsAndPatchDistinguishesNullFromMissing()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        var added = RunWithInput(data, """{"name":"Ada","phone":"555","notes":"private","dateOfBirth":"1985-06-01"}""", "customers", "add", "--input", "-");
        var id = CustomerId(added.Output);
        AssertEqual(1L, Revision(added.Output), "add revision");

        var updated = RunWithInput(data, """{"notes":null}""", "customers", "update", id.ToString(), "--input", "-");
        AssertEqual(2L, Revision(updated.Output), "update revision");
        var loaded = Run(data, "customers", "get", id.ToString(), "--reveal");
        var customer = Root(loaded.Output).GetProperty("data").GetProperty("customer");
        AssertEqual("555", customer.GetProperty("phone").GetString(), "missing patch field preserved");
        AssertEqual("", customer.GetProperty("notes").GetString(), "null text clears field");
        AssertEqual("1985-06-01", customer.GetProperty("dateOfBirth").GetString(), "restart persistence");

        var fileInput = Path.Combine(directory.Path, "customer.json");
        File.WriteAllText(fileInput, """{"name":"File Input"}""");
        var fromFile = Run(data, "customers", "add", "--input", fileInput);
        AssertEqual(0, fromFile.ExitCode, "file input add");
    }

    private static void QueryPagingAndDueOrderingMatchCore()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        Add(data, "Later", "2030-01-01T01:00:00Z", "");
        Add(data, "Due", "2026-09-18T11:00:00Z", "555-12-3456");
        Add(data, "Unscheduled", null, "");
        var list = Run(data, "customers", "list", "--limit", "1", "--reveal");
        AssertEqual("Due", Root(list.Output).GetProperty("data").GetProperty("customers")[0].GetProperty("name").GetString(), "due record sorts first");
        var search = RunWithInput(data, """{"query":"555123456"}""", "customers", "search", "--input", "-");
        AssertEqual("Due", Root(search.Output).GetProperty("data").GetProperty("customers")[0].GetProperty("name").GetString(), "identifier search");
        var due = Run(data, "customers", "list", "--filter", "due");
        AssertEqual(1, Root(due.Output).GetProperty("data").GetProperty("total").GetInt32(), "due filter");
    }

    private static void SensitiveOutputHonorsPreferenceRevealAndRedact()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        var add = RunWithInput(data, """{"name":"Sensitive","dateOfBirth":"1985-06-01","ssn":"123-45-6789","medicareId":"M-1"}""", "customers", "add", "--input", "-");
        var id = CustomerId(add.Output);
        AssertTrue(Root(add.Output).GetProperty("data").GetProperty("customer").GetProperty("ssn").ValueKind == JsonValueKind.Null, "default masks SSN");
        AssertEqual(3, Root(add.Output).GetProperty("redactedFields").GetArrayLength(), "redaction metadata");
        var preferences = RunWithInput(data, """{"showSensitiveByDefault":true}""", "preferences", "update", "--input", "-");
        AssertEqual(2L, Revision(preferences.Output), "preference revision");
        var defaultGet = Run(data, "customers", "get", id.ToString());
        AssertEqual("123-45-6789", Root(defaultGet.Output).GetProperty("data").GetProperty("customer").GetProperty("ssn").GetString(), "stored preference reveals");
        var redacted = Run(data, "customers", "get", id.ToString(), "--redact");
        AssertTrue(Root(redacted.Output).GetProperty("data").GetProperty("customer").GetProperty("ssn").ValueKind == JsonValueKind.Null, "redact overrides preference");
        var revealed = Run(data, "customers", "get", id.ToString(), "--reveal");
        AssertEqual("1985-06-01", Root(revealed.Output).GetProperty("data").GetProperty("customer").GetProperty("dateOfBirth").GetString(), "reveal produces DOB");
    }

    private static void ValidationAndRevisionConflictLeaveBytesUnchanged()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        var id = Add(data, "Stable", null, "");
        var vault = Path.Combine(data, "call-ledger.vault");
        var original = File.ReadAllBytes(vault);
        var futureDate = DateOnly.FromDateTime(DateTime.Today).AddDays(1).ToString("yyyy-MM-dd");
        var invalid = RunWithInput(data, $$"""{"dateOfBirth":"{{futureDate}}"}""", "customers", "update", id.ToString(), "--input", "-");
        AssertEqual(2, invalid.ExitCode, "future DOB rejected");
        AssertTrue(original.SequenceEqual(File.ReadAllBytes(vault)), "invalid input does not save");
        var noOffset = RunWithInput(data, """{"followUpAt":"2026-09-18T12:00:00"}""", "customers", "update", id.ToString(), "--input", "-");
        AssertEqual(2, noOffset.ExitCode, "timestamp without offset rejected");
        var conflict = RunWithInput(data, """{"notes":"changed"}""", "customers", "update", id.ToString(), "--expected-revision", "0", "--input", "-");
        AssertEqual(5, conflict.ExitCode, "revision conflict exit");
        AssertTrue(original.SequenceEqual(File.ReadAllBytes(vault)), "revision conflict does not save");
    }

    private static void MarkCalledClearsOldScheduleAndKeepsExplicitNewSchedule()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        var id = Add(data, "Caller", "2026-09-19T12:00:00Z", "");
        var cleared = Run(data, "customers", "mark-called", id.ToString(), "--reveal");
        var clearedCustomer = Root(cleared.Output).GetProperty("data").GetProperty("customer");
        AssertTrue(clearedCustomer.GetProperty("followUpAt").ValueKind == JsonValueKind.Null, "mark-called clears old schedule");
        AssertEqual("2026-09-18T12:00:00.0000000+00:00", clearedCustomer.GetProperty("lastCalledAt").GetString(), "injected clock used");
        var retained = RunWithInput(data, """{"followUpAt":"2026-09-20T12:00:00Z"}""", "customers", "mark-called", id.ToString(), "--input", "-", "--reveal");
        AssertEqual("2026-09-20T12:00:00.0000000+00:00", Root(retained.Output).GetProperty("data").GetProperty("customer").GetProperty("followUpAt").GetString(), "explicit schedule kept");
    }

    private static void DeleteAndRestoreRequireConfirmation()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        var id = Add(data, "Delete Me", null, "");
        AssertEqual(2, Run(data, "customers", "delete", id.ToString()).ExitCode, "delete confirmation");
        AssertEqual(2, Run(data, "restore", "--input", "missing").ExitCode, "restore confirmation before store open");
        AssertEqual(0, Run(data, "customers", "delete", id.ToString(), "--yes").ExitCode, "confirmed delete");
    }

    private static void StoreLifetimeLockMapsToLockedExit()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        using var store = new EncryptedCrmStore(data);
        var result = Run(data, "customers", "list");
        AssertEqual(4, result.ExitCode, "second store gets locked exit");
        AssertEqual("STORE_LOCKED", Root(result.Error).GetProperty("error").GetProperty("code").GetString(), "lock error code");
    }

    private static void BackupRestoreAndNoOverwriteProtectData()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        var id = Add(data, "Backed Up", null, "");
        var backup = Path.Combine(directory.Path, "backup.clb");
        AssertEqual(0, Run(data, "backup", "--output", backup).ExitCode, "backup exit");
        AssertEqual(6, Run(data, "backup", "--output", backup).ExitCode, "backup does not overwrite");
        var vault = Path.Combine(data, "call-ledger.vault");
        var corrupt = File.ReadAllBytes(vault);
        corrupt[corrupt.Length / 2] ^= 0x5A;
        File.WriteAllBytes(vault, corrupt);
        var restored = Run(data, "restore", "--input", backup, "--yes");
        AssertEqual(0, restored.ExitCode, "restore over corrupt primary");
        AssertEqual(id, CustomerId(Run(data, "customers", "get", id.ToString()).Output), "restored customer exists");
        var beforeInvalid = File.ReadAllBytes(vault);
        var invalid = Path.Combine(directory.Path, "invalid.clb");
        File.WriteAllText(invalid, "not encrypted");
        AssertEqual(6, Run(data, "restore", "--input", invalid, "--yes").ExitCode, "invalid backup rejected");
        AssertTrue(beforeInvalid.SequenceEqual(File.ReadAllBytes(vault)), "invalid restore preserves bytes");
    }

    private static Guid Add(string data, string name, string? followUpAt, string ssn)
    {
        var followUp = followUpAt is null ? "" : $"\"followUpAt\":\"{followUpAt}\",";
        var result = RunWithInput(data, $"{{\"name\":\"{name}\",{followUp}\"ssn\":\"{ssn}\"}}", "customers", "add", "--input", "-");
        AssertEqual(0, result.ExitCode, $"add {name}");
        return CustomerId(result.Output);
    }

    private static RunResult Run(string data, params string[] args) => RunWithInput(data, "", args);

    private static RunResult RunWithInput(string data, string input, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApplication.RunAsync(args, new StringReader(input), output, error, data, () => Now).GetAwaiter().GetResult();
        return new(exitCode, output.ToString().Trim(), error.ToString().Trim());
    }

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static long Revision(string json) => Root(json).GetProperty("revision").GetInt64();
    private static Guid CustomerId(string json) => Root(json).GetProperty("data").GetProperty("customer").GetProperty("id").GetGuid();
    private static void AssertJsonSuccess(string json, string message) => AssertTrue(Root(json).GetProperty("ok").GetBoolean(), message);
    private static void AssertEqual<T>(T expected, T actual, string message) => AssertTrue(EqualityComparer<T>.Default.Equals(expected, actual), message);
    private static void AssertTrue(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void AssertFalse(bool value, string message) => AssertTrue(!value, message);

    private sealed record RunResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CallLedger.Cli.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
