using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace CallLedger.Desktop.Services;

public sealed class DesktopDialogService : IUserDialogService
{
    public PendingChangesDecision ResolveUnsavedChanges(string action)
    {
        var result = Show(
            $"Save changes before you {action}?\n\nYes saves, No discards the draft, and Cancel keeps editing.",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => PendingChangesDecision.Save,
            MessageBoxResult.No => PendingChangesDecision.Discard,
            _ => PendingChangesDecision.Cancel,
        };
    }

    public bool ConfirmDelete(string customerName) => Show(
        $"Delete {customerName}?\n\nThis removes the customer from Call Ledger. Restore an encrypted backup if you need it later.",
        "Delete customer",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning) is MessageBoxResult.Yes;

    public bool ConfirmBackupExport(string path) => Show(
        $"Create an encrypted backup at:\n{path}\n\nThis backup is bound to this Windows account. Restore it only under the same Windows profile on this computer.",
        "Export encrypted backup",
        MessageBoxButton.YesNo,
        MessageBoxImage.Information) is MessageBoxResult.Yes;

    public bool ConfirmRestore() => Show(
        "Restore replaces the current CRM data. The backup must have been created under this same Windows account on this computer. Continue?",
        "Restore encrypted backup",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning) is MessageBoxResult.Yes;

    public string? SelectBackupExportPath()
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".clb",
            FileName = $"call-ledger-{DateTime.Now:yyyyMMdd}.clb",
            Filter = "Call Ledger backup (*.clb)|*.clb|All files (*.*)|*.*",
            Title = "Export encrypted Call Ledger backup",
        };
        return ShowDialog(dialog) is true ? dialog.FileName : null;
    }

    public string? SelectBackupRestorePath()
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Call Ledger backup (*.clb)|*.clb|All files (*.*)|*.*",
            Title = "Restore encrypted Call Ledger backup",
        };
        return ShowDialog(dialog) is true ? dialog.FileName : null;
    }

    public bool TryCopyText(string value, out string message)
    {
        try
        {
            Clipboard.SetText(value);
            message = string.Empty;
            return true;
        }
        catch (ExternalException)
        {
            message = "Windows Clipboard is busy. The value was not copied.";
            return false;
        }
    }

    public bool TryPasteText(out string value, out string message)
    {
        try
        {
            value = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            message = value.Length == 0 ? "Windows Clipboard does not contain text." : string.Empty;
            return value.Length > 0;
        }
        catch (ExternalException)
        {
            value = string.Empty;
            message = "Windows Clipboard is busy. Nothing was pasted.";
            return false;
        }
    }

    private static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = GetVisibleMainWindow();
        return owner is null
            ? MessageBox.Show(message, caption, buttons, image)
            : MessageBox.Show(owner, message, caption, buttons, image);
    }

    private static bool? ShowDialog(FileDialog dialog)
    {
        var owner = GetVisibleMainWindow();
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private static Window? GetVisibleMainWindow()
    {
        var owner = Application.Current?.MainWindow;
        return owner is { IsVisible: true } ? owner : null;
    }
}
