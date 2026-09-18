using System.Text;
using CallLedger.Core;
using CallLedger.Vault;
using static CallLedger.Tests.TestAssert;

namespace CallLedger.Tests;

internal static class VaultFailureBoundaryTests
{
    public static IEnumerable<(string Name, Action Run)> Cases =>
    [
        (nameof(UnusableDirectoryHasTypedError), UnusableDirectoryHasTypedError),
        (nameof(DirectoryPrimaryDoesNotLoadAsNew), DirectoryPrimaryDoesNotLoadAsNew),
        (nameof(MissingPrimaryWithPreviousDoesNotLoadAsNew), MissingPrimaryWithPreviousDoesNotLoadAsNew),
        (nameof(RestoreOverCorruptPrimaryPreservesBytes), RestoreOverCorruptPrimaryPreservesBytes)
    ];

    private static void UnusableDirectoryHasTypedError()
    {
        using var directory = new ScopedTemporaryDirectory();
        var path = Path.Combine(directory.Path, "not-a-directory");
        File.WriteAllText(path, "synthetic blocker");

        AssertThrows<CrmStoreException>(() => new EncryptedCrmStore(path), "unusable data directory");
    }

    private static void DirectoryPrimaryDoesNotLoadAsNew()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        Directory.CreateDirectory(Path.Combine(directory.Path, "call-ledger.vault"));

        AssertThrows<CrmStoreException>(() => store.Load(), "primary path directory");
    }

    private static void MissingPrimaryWithPreviousDoesNotLoadAsNew()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        var saved = store.Save(new CrmDocument { Customers = [SampleCustomer()] });
        _ = store.Save(saved);
        File.Delete(Path.Combine(directory.Path, "call-ledger.vault"));

        AssertThrows<CrmStoreException>(() => store.Load(), "missing primary with previous generation");
    }

    private static void RestoreOverCorruptPrimaryPreservesBytes()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        var saved = store.Save(new CrmDocument { Customers = [SampleCustomer()] });
        var backupPath = Path.Combine(directory.Path, "recovery.clb");
        var primaryPath = Path.Combine(directory.Path, "call-ledger.vault");
        var corruptBytes = Encoding.UTF8.GetBytes("corrupt synthetic fixture");
        store.ExportBackup(backupPath);
        File.WriteAllBytes(primaryPath, corruptBytes);

        var restored = store.RestoreBackup(backupPath);

        AssertEqual(saved.Revision, restored.Revision, "recovery revision baseline");
        AssertEqual("Recovery Test", restored.Customers.Single().Name, "recovered customer");
        AssertTrue(corruptBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(directory.Path, "call-ledger.previous"))), "corrupt primary preserved");
    }

    private static Customer SampleCustomer() => new() { Name = "Recovery Test" };
}
