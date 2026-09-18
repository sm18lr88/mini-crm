using CallLedger.Core;
using CallLedger.Vault;
using static CallLedger.Tests.TestAssert;

namespace CallLedger.Tests;

internal static class VaultStateProtectionTests
{
    public static IEnumerable<(string Name, Action Run)> Cases =>
    [
        (nameof(BlockedReplacementPreservesCommittedBytes), BlockedReplacementPreservesCommittedBytes),
        (nameof(RelocatedFolderRetainsRecordsAndPreferences), RelocatedFolderRetainsRecordsAndPreferences),
        (nameof(BackupExportRejectsStoreFileAliases), BackupExportRejectsStoreFileAliases)
    ];

    private static void BlockedReplacementPreservesCommittedBytes()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        var saved = store.Save(new CrmDocument { Customers = [Customer("Committed Record")] });
        var primaryPath = Path.Combine(directory.Path, "call-ledger.vault");
        var original = File.ReadAllBytes(primaryPath);

        using (var handle = new FileStream(primaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            AssertThrows<CrmStoreException>(() => store.Save(saved with { Customers = [] }), "blocked replacement");
        }

        AssertTrue(original.SequenceEqual(File.ReadAllBytes(primaryPath)), "committed ciphertext preserved");
        AssertEqual("Committed Record", store.Load().Customers.Single().Name, "committed record preserved");
    }

    private static void RelocatedFolderRetainsRecordsAndPreferences()
    {
        using var directory = new ScopedTemporaryDirectory();
        var source = Path.Combine(directory.Path, "source");
        var target = Path.Combine(directory.Path, "moved");
        using (var store = new EncryptedCrmStore(source))
        {
            _ = store.Save(new CrmDocument
            {
                Customers = [Customer("Moved Record")],
                Preferences = new Preferences { ShowSensitiveByDefault = true }
            });
        }

        Directory.Move(source, target);
        using var moved = new EncryptedCrmStore(target);
        var loaded = moved.Load();
        AssertTrue(loaded.Preferences.ShowSensitiveByDefault, "relocated preference");
        AssertEqual("Moved Record", loaded.Customers.Single().Name, "relocated record");
    }

    private static void BackupExportRejectsStoreFileAliases()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        var first = store.Save(new CrmDocument { Customers = [Customer("First Record")] });
        _ = store.Save(first with { Customers = [Customer("Second Record")] });
        var primaryPath = Path.Combine(directory.Path, "call-ledger.vault");
        var previousPath = Path.Combine(directory.Path, "call-ledger.previous");
        var lockPath = Path.Combine(directory.Path, "call-ledger.lock");
        var primary = File.ReadAllBytes(primaryPath);
        var previous = File.ReadAllBytes(previousPath);

        foreach (var aliasPath in new[] { primaryPath, previousPath, lockPath })
        {
            AssertThrows<CrmStoreException>(() => store.ExportBackup(aliasPath), "store file backup alias");
        }

        AssertTrue(primary.SequenceEqual(File.ReadAllBytes(primaryPath)), "primary alias protection");
        AssertTrue(previous.SequenceEqual(File.ReadAllBytes(previousPath)), "previous alias protection");
        AssertTrue(File.Exists(lockPath), "lock alias protection");
    }

    private static Customer Customer(string name) => new() { Name = name };
}
