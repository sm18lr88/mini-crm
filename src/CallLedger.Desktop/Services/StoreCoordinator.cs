using CallLedger.Core;

namespace CallLedger.Desktop.Services;

public sealed class StoreCoordinator : IDisposable
{
    private readonly ICrmStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StoreCoordinator(ICrmStore store)
    {
        _store = store;
    }

    public Task<CrmDocument> LoadAsync() => ExecuteAsync(store => store.Load());

    public Task<CrmDocument> SaveAsync(CrmDocument document) => ExecuteAsync(store => store.Save(document));

    public Task ExportBackupAsync(string path) => ExecuteAsync(store =>
    {
        store.ExportBackup(path);
        return true;
    });

    public Task<CrmDocument> RestoreBackupAsync(string path) => ExecuteAsync(store => store.RestoreBackup(path));

    public void Dispose()
    {
        _gate.Dispose();
        if (_store is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async Task<T> ExecuteAsync<T>(Func<ICrmStore, T> operation)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() => operation(_store));
        }
        finally
        {
            _gate.Release();
        }
    }
}
