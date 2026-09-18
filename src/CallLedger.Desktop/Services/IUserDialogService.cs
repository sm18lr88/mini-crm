namespace CallLedger.Desktop.Services;

public enum PendingChangesDecision
{
    Save,
    Discard,
    Cancel
}

public interface IUserDialogService
{
    PendingChangesDecision ResolveUnsavedChanges(string action);

    bool ConfirmDelete(string customerName);

    bool ConfirmBackupExport(string path);

    bool ConfirmRestore();

    string? SelectBackupExportPath();

    string? SelectBackupRestorePath();

    bool TryCopyText(string value, out string message);

    bool TryPasteText(out string value, out string message);
}
