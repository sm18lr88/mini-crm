using CallLedger.Core;

namespace CallLedger.Vault;

public sealed class EncryptedCrmStore : ICrmStore, IDisposable
{
    private const string PrimaryFileName = "call-ledger.vault";
    private const string PreviousFileName = "call-ledger.previous";
    private const string LockFileName = "call-ledger.lock";

    private readonly object gate = new();
    private readonly FileStream lifetimeLock;
    private readonly string primaryPath;
    private readonly string previousPath;
    private readonly string lockPath;
    private bool disposed;

    public EncryptedCrmStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        try
        {
            var fullDirectory = Path.GetFullPath(dataDirectory);
            Directory.CreateDirectory(fullDirectory);
            primaryPath = Path.Combine(fullDirectory, PrimaryFileName);
            previousPath = Path.Combine(fullDirectory, PreviousFileName);
            lockPath = Path.Combine(fullDirectory, LockFileName);
            lifetimeLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CrmStoreException("This CRM data folder is already in use or cannot be opened.", exception);
        }
    }

    public CrmDocument Load()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            return ReadCurrentOrNew();
        }
    }

    public CrmDocument Save(CrmDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (gate)
        {
            ThrowIfDisposed();
            VaultDocumentCodec.ValidateDocument(document);
            var current = ReadCurrentOrNew();
            if (document.Revision != current.Revision)
            {
                throw new CrmStoreException("The CRM data changed in another operation. Reload before saving.");
            }

            var saved = document with
            {
                Revision = checked(current.Revision + 1),
                Customers = new List<Customer>(document.Customers),
                Preferences = document.Preferences
            };
            WritePrimary(VaultDocumentCodec.Encrypt(saved));
            return saved;
        }
    }

    public void ExportBackup(string path)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            var backupPath = ResolveExternalPath(path);
            byte[] encryptedBytes;
            try
            {
                encryptedBytes = ReadEncryptedFile(primaryPath);
            }
            catch (FileNotFoundException exception)
            {
                throw new CrmStoreException("No saved CRM data is available to export.", exception);
            }

            _ = VaultDocumentCodec.DecryptAndValidate(encryptedBytes);
            WriteEncryptedFileAtomically(backupPath, encryptedBytes, preservePrevious: false);
        }
    }

    public CrmDocument RestoreBackup(string path)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            var backupPath = ResolveExternalPath(path);
            CrmDocument restored;
            try
            {
                restored = VaultDocumentCodec.DecryptAndValidate(ReadEncryptedFile(backupPath));
            }
            catch (FileNotFoundException exception)
            {
                throw new CrmStoreException("The selected CRM backup could not be found.", exception);
            }

            var current = TryReadPrimary();
            var persisted = restored with
            {
                Revision = current is null ? restored.Revision : checked(current.Revision + 1),
                Customers = new List<Customer>(restored.Customers),
                Preferences = restored.Preferences
            };
            WritePrimary(VaultDocumentCodec.Encrypt(persisted));
            return persisted;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            lifetimeLock.Dispose();
            disposed = true;
        }
    }

    private CrmDocument ReadCurrentOrNew()
    {
        try
        {
            return ReadPrimary();
        }
        catch (FileNotFoundException)
        {
            if (CanReadFile(previousPath))
            {
                throw new CrmStoreException("The primary CRM data file is missing while a previous generation remains.");
            }

            return new CrmDocument();
        }
    }

    private CrmDocument ReadPrimary() => VaultDocumentCodec.DecryptAndValidate(ReadEncryptedFile(primaryPath));

    private CrmDocument? TryReadPrimary()
    {
        try
        {
            return ReadPrimary();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (CrmStoreException)
        {
            return null;
        }
    }

    private static bool CanReadFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CrmStoreException("The CRM data file could not be accessed.", exception);
        }
    }

    private void WritePrimary(byte[] encryptedBytes) => WriteEncryptedFileAtomically(primaryPath, encryptedBytes, preservePrevious: true);

    private static byte[] ReadEncryptedFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is 0 or > VaultDocumentCodec.MaximumEncryptedFileBytes)
            {
                throw new CrmStoreException("The stored CRM data has an invalid size.");
            }

            var encryptedBytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
            stream.ReadExactly(encryptedBytes);
            return encryptedBytes;
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (CrmStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CrmStoreException("The CRM data file could not be accessed.", exception);
        }
    }

    private void WriteEncryptedFileAtomically(string destinationPath, byte[] encryptedBytes, bool preservePrevious)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new CrmStoreException("The destination path is invalid.");
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(encryptedBytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destinationPath))
            {
                File.Replace(tempPath, destinationPath, preservePrevious ? previousPath : null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, destinationPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CrmStoreException("The CRM data could not be saved safely.", exception);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new CrmStoreException("The CRM data temporary file could not be cleaned up.", exception);
            }
        }
    }

    private string ResolveExternalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (string.Equals(fullPath, primaryPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, previousPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, lockPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmStoreException("A backup path cannot replace CRM store files.");
        }

        return fullPath;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(EncryptedCrmStore));
        }
    }
}
