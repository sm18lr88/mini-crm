using System.Text.Json;
using CallLedger.Cli;
using CallLedger.Vault;

namespace CallLedger.Cli.Tests;

internal static class CliInputLifecycleTests
{
    public static void InvalidInputDoesNotOpenStore()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");

        var malformed = Run(data, new StringReader("not JSON"), "customers", "add", "--input", "-");
        AssertEqual(2, malformed.ExitCode, "malformed input exit");
        AssertFalse(Directory.Exists(data), "malformed input does not create data directory");

        var wrongType = Run(data, new StringReader("{\"name\":42}"), "customers", "add", "--input", "-");
        AssertEqual(2, wrongType.ExitCode, "wrong field type exit");
        AssertFalse(Directory.Exists(data), "wrong field type does not create data directory");

        using var probe = new StoreProbeReader(data, "{\"name\":\"Probe\"}");
        var accepted = Run(data, probe, "customers", "add", "--input", "-");
        AssertEqual(0, accepted.ExitCode, "probe add exit");
        AssertTrue(probe.StoreWasAvailable, "stdin is read before exclusive store ownership");

        var bom = Run(data, new StringReader("\uFEFF{\"name\":\"BOM\"}"), "customers", "add", "--input", "-");
        AssertEqual(0, bom.ExitCode, "UTF-8 BOM input exit");
    }

    public static void ListRejectsInputAndSchemaUsesStandardKeyword()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");

        var list = Run(data, new StringReader("{}"), "customers", "list", "--input", "-");
        AssertEqual(2, list.ExitCode, "list rejects input");
        AssertFalse(Directory.Exists(data), "rejected list input does not create data directory");

        var schema = Run(data, TextReader.Null, "schema");
        AssertEqual(0, schema.ExitCode, "schema exit");
        using var json = JsonDocument.Parse(schema.Output);
        AssertTrue(json.RootElement.GetProperty("data").TryGetProperty("$schema", out _), "schema has standard metadata keyword");
    }

    private static RunResult Run(string data, TextReader input, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = CliApplication.RunAsync(args, input, output, error, data, () => DateTimeOffset.Now).GetAwaiter().GetResult();
        return new(exitCode, output.ToString(), error.ToString());
    }

    private static void AssertEqual<T>(T expected, T actual, string message) => AssertTrue(EqualityComparer<T>.Default.Equals(expected, actual), message);
    private static void AssertTrue(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void AssertFalse(bool value, string message) => AssertTrue(!value, message);

    private sealed record RunResult(int ExitCode, string Output, string Error);

    private sealed class StoreProbeReader(string dataDirectory, string content) : StringReader(content)
    {
        public bool StoreWasAvailable { get; private set; }

        public override int Read(char[] buffer, int index, int count)
        {
            using var probe = new EncryptedCrmStore(dataDirectory);
            StoreWasAvailable = true;
            return base.Read(buffer, index, count);
        }
    }

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
