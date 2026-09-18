using CallLedger.Core;

namespace CallLedger.Tests;

internal static class TestAssert
{
    public static void AssertEqual<T>(T expected, T actual, string message) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {message} to be '{expected}', but was '{actual}'.");
        }
    }

    public static void AssertSnapshotEqual(CrmDocument expected, CrmDocument actual, string message)
    {
        AssertEqual(expected.SchemaVersion, actual.SchemaVersion, $"{message} schema");
        AssertEqual(expected.Revision, actual.Revision, $"{message} revision");
        AssertEqual(expected.Preferences, actual.Preferences, $"{message} preferences");
        AssertEqual(expected.Customers.Count, actual.Customers.Count, $"{message} customer count");
        AssertTrue(expected.Customers.SequenceEqual(actual.Customers), $"{message} customers");
    }

    public static void AssertTrue(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException($"Expected true: {message}.");
        }
    }

    public static void AssertFalse(bool value, string message) => AssertTrue(!value, message);

    public static void AssertThrows<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}: {message}.");
    }
}

internal sealed class ScopedTemporaryDirectory : IDisposable
{
    public ScopedTemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CallLedger.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
