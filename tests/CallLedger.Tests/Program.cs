using System.Text;
using CallLedger.Core;
using CallLedger.Vault;
using static CallLedger.Tests.TestAssert;

namespace CallLedger.Tests;

internal static class Program
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            (nameof(SnapshotPersistsAcrossRestart), SnapshotPersistsAcrossRestart),
            (nameof(SaveRejectsRevisionConflict), SaveRejectsRevisionConflict),
            (nameof(CorruptStoreDoesNotReset), CorruptStoreDoesNotReset),
            (nameof(EncryptedSnapshotDoesNotExposePlaintext), EncryptedSnapshotDoesNotExposePlaintext),
            (nameof(BackupRestorePreservesDataAndIncrementsRevision), BackupRestorePreservesDataAndIncrementsRevision),
            (nameof(InvalidRestoreLeavesLiveStoreUnchanged), InvalidRestoreLeavesLiveStoreUnchanged),
            (nameof(StoreLifetimeIsExclusivelyLocked), StoreLifetimeIsExclusivelyLocked),
            (nameof(QueryAndValidationHonorContracts), QueryAndValidationHonorContracts)
        }
        .Concat(VaultFailureBoundaryTests.Cases)
        .Concat(VaultStateProtectionTests.Cases)
        .Concat(VaultRestoreValidationTests.Cases)
        .ToArray();

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

    private static void SnapshotPersistsAcrossRestart()
    {
        using var directory = new ScopedTemporaryDirectory();
        var customer = SampleCustomer("Ada Lovelace");
        CrmDocument saved;
        using (var store = new EncryptedCrmStore(directory.Path))
        {
            var initial = store.Load();
            AssertEqual(0L, initial.Revision, "new store revision");
            saved = store.Save(initial with { Customers = [customer] });
            AssertEqual(1L, saved.Revision, "saved revision");
        }

        using var reopened = new EncryptedCrmStore(directory.Path);
        var loaded = reopened.Load();
        AssertSnapshotEqual(saved, loaded, "reopened document");
    }

    private static void SaveRejectsRevisionConflict()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var store = new EncryptedCrmStore(directory.Path);
        var original = store.Load() with { Customers = [SampleCustomer("Revision Test")] };
        _ = store.Save(original);
        AssertThrows<CrmStoreException>(() => store.Save(original), "stale save");
        AssertEqual(0L, original.Revision, "failed save does not mutate caller");
        AssertEqual(1L, store.Load().Revision, "failed save does not mutate store");
    }

    private static void CorruptStoreDoesNotReset()
    {
        using var directory = new ScopedTemporaryDirectory();
        using (var store = new EncryptedCrmStore(directory.Path))
        {
            _ = store.Save(store.Load() with { Customers = [SampleCustomer("Integrity Test")] });
        }

        var path = Path.Combine(directory.Path, "call-ledger.vault");
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0x5A;
        File.WriteAllBytes(path, bytes);

        using var reopened = new EncryptedCrmStore(directory.Path);
        AssertThrows<CrmStoreException>(() => reopened.Load(), "corrupt load");
        AssertTrue(File.Exists(path), "corrupt store must remain present");
        AssertThrows<CrmStoreException>(() => reopened.Save(new CrmDocument()), "corrupt save");
    }

    private static void EncryptedSnapshotDoesNotExposePlaintext()
    {
        using var directory = new ScopedTemporaryDirectory();
        const string secretName = "Sensitive Person 87";
        const string secretSsn = "987-65-4321";
        const string secretNote = "confidential appointment detail";
        using (var store = new EncryptedCrmStore(directory.Path))
        {
            _ = store.Save(store.Load() with
            {
                Customers = [SampleCustomer(secretName) with { Ssn = secretSsn, Notes = secretNote }]
            });
        }

        var bytes = File.ReadAllBytes(Path.Combine(directory.Path, "call-ledger.vault"));
        AssertFalse(ContainsUtf8(bytes, secretName), "name in encrypted file");
        AssertFalse(ContainsUtf8(bytes, secretSsn), "SSN in encrypted file");
        AssertFalse(ContainsUtf8(bytes, secretNote), "notes in encrypted file");
    }

    private static void BackupRestorePreservesDataAndIncrementsRevision()
    {
        using var directory = new ScopedTemporaryDirectory();
        var backupPath = Path.Combine(directory.Path, "backup.clb");
        using var store = new EncryptedCrmStore(directory.Path);
        var first = store.Save(store.Load() with { Customers = [SampleCustomer("Before Backup")] });
        store.ExportBackup(backupPath);
        _ = store.Save(first with { Customers = [SampleCustomer("After Backup")] });
        var restored = store.RestoreBackup(backupPath);

        AssertEqual(3L, restored.Revision, "restore revision");
        AssertEqual("Before Backup", restored.Customers.Single().Name, "restored customer");
        AssertTrue(File.Exists(Path.Combine(directory.Path, "call-ledger.previous")), "previous generation");
    }

    private static void InvalidRestoreLeavesLiveStoreUnchanged()
    {
        using var directory = new ScopedTemporaryDirectory();
        var backupPath = Path.Combine(directory.Path, "invalid.clb");
        File.WriteAllText(backupPath, "not an encrypted backup");
        using var store = new EncryptedCrmStore(directory.Path);
        var saved = store.Save(store.Load() with { Customers = [SampleCustomer("Live Record")] });

        AssertThrows<CrmStoreException>(() => store.RestoreBackup(backupPath), "invalid restore");
        AssertSnapshotEqual(saved, store.Load(), "live data after invalid restore");
    }

    private static void StoreLifetimeIsExclusivelyLocked()
    {
        using var directory = new ScopedTemporaryDirectory();
        using var first = new EncryptedCrmStore(directory.Path);
        AssertThrows<CrmStoreException>(() => new EncryptedCrmStore(directory.Path), "exclusive lock");
    }

    private static void QueryAndValidationHonorContracts()
    {
        var due = SampleCustomer("Due Customer") with { FollowUpAt = Now.AddMinutes(-1), Phone = "(555) 123-4567" };
        var upcoming = SampleCustomer("Upcoming Customer") with { FollowUpAt = Now.AddMinutes(5), MedicareId = "1EG4-TE5-MK73" };
        var unscheduled = SampleCustomer("Unscheduled Customer");
        var all = new[] { unscheduled, upcoming, due };

        AssertEqual("Due Customer", CustomerQuery.Search(all, "5551234567", CallFilter.All, Now).Single().Name, "normalized phone search");
        AssertEqual("Upcoming Customer", CustomerQuery.Search(all, "1eg4 te5 mk73", CallFilter.All, Now).Single().Name, "normalized identifier search");
        AssertEqual("Due Customer", CustomerQuery.Search(all, "", CallFilter.Due, Now).Single().Name, "due filter");
        AssertEqual("Upcoming Customer", CustomerQuery.Search(all, "", CallFilter.Upcoming, Now).Single().Name, "upcoming filter");
        AssertEqual("Unscheduled Customer", CustomerQuery.Search(all, "", CallFilter.Unscheduled, Now).Single().Name, "unscheduled filter");
        AssertEqual("Due Customer", CustomerQuery.Search(all, "", CallFilter.All, Now).First().Name, "scheduled ordering");

        var invalid = SampleCustomer("") with
        {
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            Notes = new string('x', 10_001)
        };
        var errors = CustomerValidator.Validate(invalid);
        AssertTrue(errors.Any(error => error.Contains("required", StringComparison.OrdinalIgnoreCase)), "name validation");
        AssertTrue(errors.Any(error => error.Contains("future", StringComparison.OrdinalIgnoreCase)), "DOB validation");
        AssertTrue(errors.Any(error => error.Contains("characters", StringComparison.OrdinalIgnoreCase)), "length validation");
    }

    private static Customer SampleCustomer(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DateOfBirth = new DateOnly(1985, 6, 1),
        Phone = "555-555-0199",
        CallReason = "Follow-up",
        Notes = "Synthetic test record"
    };

    private static bool ContainsUtf8(byte[] haystack, string needle)
    {
        var bytes = Encoding.UTF8.GetBytes(needle);
        return haystack.AsSpan().IndexOf(bytes) >= 0;
    }

}
