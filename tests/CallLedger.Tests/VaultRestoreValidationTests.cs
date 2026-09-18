using System.Reflection;
using System.Text;
using System.Text.Json;
using CallLedger.Core;
using CallLedger.Vault;
using static CallLedger.Tests.TestAssert;

namespace CallLedger.Tests;

internal static class VaultRestoreValidationTests
{
    public static IEnumerable<(string Name, Action Run)> Cases =>
    [
        (nameof(CiphertextTamperingInsideEnvelopeIsRejected), CiphertextTamperingInsideEnvelopeIsRejected),
        (nameof(MissingRestoreBackupHasTypedError), MissingRestoreBackupHasTypedError),
        (nameof(UnsupportedRestoreLeavesLiveBytesUnchanged), UnsupportedRestoreLeavesLiveBytesUnchanged),
        (nameof(SchemaInvalidRestoreLeavesLiveBytesUnchanged), SchemaInvalidRestoreLeavesLiveBytesUnchanged)
    ];

    private static void CiphertextTamperingInsideEnvelopeIsRejected()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        _ = store.Save(new CrmDocument { Customers = [new Customer { Name = "Tamper Test" }] });
        var primaryPath = Path.Combine(directory.Path, "call-ledger.vault");
        using var envelope = JsonDocument.Parse(File.ReadAllBytes(primaryPath));
        var payload = Convert.FromBase64String(envelope.RootElement.GetProperty("Payload").GetString()!);
        payload[payload.Length / 2] ^= 0x5A;
        File.WriteAllBytes(primaryPath, SerializeEnvelope(envelope.RootElement.GetProperty("Version").GetInt32(), payload));

        AssertThrows<CrmStoreException>(() => store.Load(), "tampered envelope payload");
    }

    private static void MissingRestoreBackupHasTypedError()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        var backupPath = Path.Combine(directory.Path, "removed.clb");
        _ = store.Save(new CrmDocument { Customers = [new Customer { Name = "Backup Test" }] });
        store.ExportBackup(backupPath);
        File.Delete(backupPath);

        AssertThrows<CrmStoreException>(() => store.RestoreBackup(backupPath), "removed backup");
    }

    private static void UnsupportedRestoreLeavesLiveBytesUnchanged()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        var live = store.Save(new CrmDocument { Customers = [new Customer { Name = "Live Record" }] });
        var primaryPath = Path.Combine(directory.Path, "call-ledger.vault");
        var backupPath = Path.Combine(directory.Path, "unsupported.clb");
        var original = File.ReadAllBytes(primaryPath);
        store.ExportBackup(backupPath);
        using var envelope = JsonDocument.Parse(File.ReadAllBytes(backupPath));
        File.WriteAllBytes(backupPath, SerializeEnvelope(2, Convert.FromBase64String(envelope.RootElement.GetProperty("Payload").GetString()!)));

        AssertThrows<CrmStoreException>(() => store.RestoreBackup(backupPath), "unsupported backup");
        AssertTrue(original.SequenceEqual(File.ReadAllBytes(primaryPath)), "unsupported restore preserves live bytes");
        AssertSnapshotEqual(live, store.Load(), "unsupported restore preserves live document");
    }

    private static void SchemaInvalidRestoreLeavesLiveBytesUnchanged()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        _ = store.Save(new CrmDocument { Customers = [new Customer { Name = "Live Record" }] });
        var primaryPath = Path.Combine(directory.Path, "call-ledger.vault");
        var backupPath = Path.Combine(directory.Path, "invalid-schema.clb");
        var original = File.ReadAllBytes(primaryPath);
        File.WriteAllBytes(backupPath, EncryptSchemaInvalidDocument());

        AssertThrows<CrmStoreException>(() => store.RestoreBackup(backupPath), "schema-invalid backup");
        AssertTrue(original.SequenceEqual(File.ReadAllBytes(primaryPath)), "schema-invalid restore preserves live bytes");
    }

    private static byte[] EncryptSchemaInvalidDocument()
    {
        var protect = typeof(EncryptedCrmStore).Assembly
            .GetType("CallLedger.Vault.NativeDpapi")!
            .GetMethod("Protect", BindingFlags.Public | BindingFlags.Static)!;
        var plaintext = Encoding.UTF8.GetBytes("{\"SchemaVersion\":2,\"Revision\":0,\"Customers\":[],\"Preferences\":{\"ShowSensitiveByDefault\":false,\"CompactMode\":false,\"AlwaysOnTop\":true}}");
        var protectedBytes = (byte[])protect.Invoke(null, [plaintext])!;
        return SerializeEnvelope(1, protectedBytes);
    }

    private static byte[] SerializeEnvelope(int version, byte[] payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new { Version = version, Payload = Convert.ToBase64String(payload) });
}
