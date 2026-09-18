using CallLedger.Core;
using CallLedger.Desktop.Services;
using CallLedger.Desktop.ViewModels;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace CallLedger.Desktop;

public static class SmokeRunner
{
    private const string SmokeTestArgument = "--smoke-test";
    private const string RenderSmokeArgument = "--render-smoke";
    private static readonly DateTimeOffset SmokeNow = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public static bool IsRequested(IEnumerable<string> arguments)
        => arguments.Any(argument => argument is SmokeTestArgument or RenderSmokeArgument);

    public static async Task RunAsync(string[] arguments)
    {
        var request = ParseRequest(arguments);
        using var viewModel = new CrmViewModel(
            new StoreCoordinator(new SmokeStore(CreateSmokeDocument())),
            new SmokeDialogService(),
            () => SmokeNow);

        WriteRuntimeProof();
        Require(await viewModel.LoadAsync(), "The smoke view model did not load.");
        Require(viewModel.Rows.Count == 3, "The smoke data did not produce all customer rows.");
        Require(viewModel.HasDueCalls && viewModel.DueCount == 1, "The smoke data did not produce a due call.");

        if (request.Mode == SmokeMode.Test)
        {
            await ExerciseWorkflowAsync(viewModel);
            await ExerciseRegressionAsync(viewModel);
            await ExerciseRecoveryAsync();
            Console.WriteLine("PASS --smoke-test");
            return;
        }

        await RenderSmokeAsync(viewModel, request.RenderOutputDirectory);
    }

    private static async Task ExerciseWorkflowAsync(CrmViewModel viewModel)
    {
        viewModel.SearchText = "Upcoming";
        Require(viewModel.Rows.Count == 1 && viewModel.Rows[0].Name == "Upcoming Customer", "Search did not narrow the call list.");

        viewModel.SetFilter(nameof(CallFilter.Due));
        Require(viewModel.Rows.Count == 0, "Due filtering did not combine with search.");

        viewModel.SearchText = string.Empty;
        Require(viewModel.Rows.Count == 1 && viewModel.Rows[0].Name == "Due Customer", "Due filtering did not return the due customer.");

        viewModel.SetFilter(nameof(CallFilter.All));
        await viewModel.NewAsync();
        viewModel.Draft.Name = "Smoke Customer";
        viewModel.Draft.Phone = "555-555-0144";
        viewModel.Draft.FollowUpAt = "2026-09-19 09:00";
        Require(await viewModel.SaveAsync(), "Saving a new smoke customer failed.");
        Require(viewModel.Rows.Any(row => row.Name == "Smoke Customer"), "The saved smoke customer was not displayed.");

        Require(await viewModel.MarkCalledAsync(), "Marking the smoke customer as called failed.");
        Require(viewModel.NextCallSuggested, "Marking a call did not request a new follow-up.");

        await viewModel.ToggleAlwaysOnTopAsync();
        Require(!viewModel.AlwaysOnTop, "Always-on-top preference did not persist.");

        await viewModel.ToggleCompactModeAsync();
        Require(viewModel.CompactMode, "Compact mode preference did not persist.");

        await viewModel.SetShowSensitiveByDefaultAsync(true);
        Require(viewModel.ShowSensitiveByDefault, "Sensitive display preference did not persist.");
    }

    private static async Task ExerciseRegressionAsync(CrmViewModel viewModel)
    {
        ExerciseDraftRegressions();
        await ExerciseSelectionAfterSaveRegressionAsync();

        viewModel.Draft.Name = "Discarded smoke edit";
        await viewModel.ToggleCompactModeAsync();
        Require(viewModel.Draft.Name == "Smoke Customer" && !viewModel.HasUnsavedChanges,
            "Discarding a mode change left the smoke draft dirty.");

        var notifications = new List<string?>();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        await viewModel.SetShowSensitiveByDefaultAsync(false);
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Require(notifications.Contains(nameof(CrmViewModel.IsBusy)), "Busy state did not notify its binding.");

        viewModel.SearchText = "does not match";
        await viewModel.NewAsync();
        viewModel.Draft.Name = "Filtered Smoke Customer";
        Require(await viewModel.SaveAsync(), "Saving a filtered smoke customer failed.");
        viewModel.Draft.Notes = "Unsaved smoke note";
        viewModel.CancelEdit();
        Require(viewModel.Draft.Notes.Length == 0 && !viewModel.HasUnsavedChanges,
            "Cancel did not restore a filtered saved customer.");

        viewModel.SearchText = string.Empty;
        viewModel.Draft.FollowUpAt = "2026-09-20 10:00";
        Require(await viewModel.MarkCalledAsync(), "Marking an edited smoke follow-up failed.");
        Require(viewModel.Draft.FollowUpAt == "2026-09-20 10:00",
            "Marking a call erased the edited smoke follow-up.");

        void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
            => notifications.Add(eventArgs.PropertyName);
    }

    private static void ExerciseDraftRegressions()
    {
        SmokeLocaleChecks.Exercise();

        var draft = new CustomerDraft();
        draft.Load(new Customer
        {
            Name = "Draft source",
            Ssn = "000-00-0000",
            FollowUpAt = SmokeNow.AddSeconds(37),
        });
        Require(!draft.HasChanges, "Formatting seconds away marked a loaded smoke draft as dirty.");

        var whitespaceSource = new Customer
        {
            Name = "Synthetic ",
            Notes = "\nKeep intentional spacing\n",
            Phone = " 555-0100 ",
        };
        var whitespaceDraft = new CustomerDraft();
        whitespaceDraft.Load(whitespaceSource);
        Require(!whitespaceDraft.HasChanges, "Loading CLI-authored whitespace marked the smoke draft as dirty.");
        Require(whitespaceDraft.TryBuild(out var whitespaceCustomer, out _) && whitespaceCustomer == whitespaceSource,
            "Building an untouched smoke draft changed CLI-authored whitespace.");

        var notifications = new List<string?>();
        draft.PropertyChanged += OnDraftPropertyChanged;
        draft.SetActualValue(CustomerFields.Name, "Pasted smoke name");
        draft.SetActualValue(CustomerFields.FollowUpAt, "2026-09-20 09:00");
        draft.PropertyChanged -= OnDraftPropertyChanged;
        Require(notifications.Contains(nameof(CustomerDraft.Name)), "Pasted smoke name did not notify its binding.");
        Require(notifications.Contains(nameof(CustomerDraft.FollowUpAt)), "Pasted smoke follow-up did not notify its binding.");

        draft.SetSensitiveDefault(true);
        Require(!draft.IsSsnMasked, "Smoke sensitive default did not reveal SSN.");
        draft.ToggleReveal(CustomerFields.Ssn);
        Require(draft.IsSsnMasked, "Smoke per-field sensitive toggle could not hide SSN.");
        draft.SetSensitiveDefault(false);
        Require(draft.IsSsnMasked, "Smoke global sensitive default did not reset the temporary override.");

        var followUpDraft = new CustomerDraft();
        followUpDraft.Load(new Customer { Name = "Follow-up source" });
        followUpDraft.FollowUpAt = "2026-09-20 09:00";
        Require(followUpDraft.FollowUpEdited, "Adding a follow-up did not mark the draft as edited.");
        followUpDraft.FollowUpAt = string.Empty;
        Require(!followUpDraft.FollowUpEdited, "Restoring the original follow-up left a stale edit marker.");

        if (FindInvalidLocalTime() is { } invalidLocalTime)
        {
            var invalidDraft = new CustomerDraft();
            invalidDraft.Load(new Customer { Name = "Invalid local time" });
            invalidDraft.FollowUpAt = invalidLocalTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            Require(!invalidDraft.TryBuild(out _, out _), "A nonexistent local smoke follow-up was accepted.");
        }

        void OnDraftPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
            => notifications.Add(eventArgs.PropertyName);
    }

    private static async Task ExerciseSelectionAfterSaveRegressionAsync()
    {
        using var viewModel = new CrmViewModel(
            new StoreCoordinator(new SmokeStore(CreateSmokeDocument())),
            new SmokeDialogService(pendingChangesDecision: PendingChangesDecision.Save),
            () => SmokeNow);

        Require(await viewModel.LoadAsync(), "The selection regression smoke view model did not load.");
        var requested = viewModel.Rows.Single(row => row.Name == "Upcoming Customer");
        viewModel.Draft.Notes = "Save before switching customers";
        Require(await viewModel.SelectCustomerAsync(requested), "Saving before a smoke selection failed.");
        var selected = viewModel.SelectedRow
            ?? throw new InvalidOperationException("Saving before selection cleared the smoke selected row.");
        Require(viewModel.Rows.Contains(selected), "Saving before selection left the smoke selected row outside the current collection.");
        Require(selected.Customer.Id == requested.Customer.Id && viewModel.Draft.Notes == selected.Customer.Notes,
            "Saving before selection did not load the current requested smoke customer.");
    }

    private static async Task ExerciseRecoveryAsync()
    {
        using var viewModel = new CrmViewModel(
            new StoreCoordinator(new RecoverySmokeStore(CreateSmokeDocument())),
            new SmokeDialogService("synthetic-backup"),
            () => SmokeNow);

        Require(!await viewModel.LoadAsync(), "The recovery smoke store unexpectedly loaded.");
        Require(!viewModel.IsLoaded, "A failed load enabled normal mutations.");
        Require(!viewModel.NewCommand.CanExecute(null), "New remained enabled after a failed load.");
        Require(viewModel.RestoreBackupCommand.CanExecute(null), "Restore was unavailable after a failed load.");
        Require(viewModel.ErrorMessage.Contains("Synthetic corrupted store", StringComparison.Ordinal),
            "The store failure detail was not shown.");

        await viewModel.RestoreBackupAsync();
        Require(viewModel.IsLoaded, "Successful smoke restore did not mark the ledger as loaded.");
        Require(viewModel.NewCommand.CanExecute(null), "Normal actions did not return after smoke restore.");
    }

    private static async Task RenderSmokeAsync(CrmViewModel viewModel, string? requestedOutputDirectory)
    {
        var outputDirectory = requestedOutputDirectory ?? Path.Combine(Path.GetTempPath(), "CallLedger-render-smoke");
        var expandedMaskedPath = Path.Combine(outputDirectory, "expanded-masked.png");
        var expandedUnmaskedPath = Path.Combine(outputDirectory, "expanded-unmasked.png");
        var emptyPath = Path.Combine(outputDirectory, "empty.png");
        var compactPath = Path.Combine(outputDirectory, "compact-default.png");

        Directory.CreateDirectory(outputDirectory);
        Require(viewModel.Draft.IsSsnMasked, "The initial render smoke draft was not masked.");
        RenderWindow(viewModel, expandedMaskedPath);

        await viewModel.SetShowSensitiveByDefaultAsync(true);
        Require(!viewModel.Draft.IsSsnMasked, "The unmasked render smoke draft remained masked.");
        RenderWindow(viewModel, expandedUnmaskedPath);

        viewModel.SearchText = "no matching smoke record";
        Require(viewModel.IsEmpty, "The empty render smoke state did not filter all rows.");
        RenderWindow(viewModel, emptyPath);

        viewModel.SearchText = string.Empty;
        await viewModel.SetShowSensitiveByDefaultAsync(false);
        await viewModel.ToggleCompactModeAsync();
        Require(viewModel.CompactMode, "Compact mode was not available for render smoke.");
        RenderWindow(viewModel, compactPath);

        foreach (var path in new[] { expandedMaskedPath, expandedUnmaskedPath, emptyPath, compactPath })
        {
            Require(File.Exists(path) && new FileInfo(path).Length > 0, $"Render smoke output was not written: {path}");
        }

        Console.WriteLine($"PASS --render-smoke {outputDirectory}");
    }

    private static void RenderWindow(CrmViewModel viewModel, string path)
    {
        var window = new MainWindow(viewModel, shutdownOnClosed: false);
        try
        {
            window.RenderToPng(path);
        }
        finally
        {
            window.DisposeForSmoke();
        }
    }

    private static SmokeRequest ParseRequest(string[] arguments)
    {
        if (arguments.Length == 1 && arguments[0] == SmokeTestArgument)
        {
            return new SmokeRequest(SmokeMode.Test, null);
        }

        if (arguments.Length == 1 && arguments[0] == RenderSmokeArgument)
        {
            return new SmokeRequest(SmokeMode.Render, null);
        }

        if (arguments.Length == 2 && arguments[0] == RenderSmokeArgument && !string.IsNullOrWhiteSpace(arguments[1]))
        {
            return new SmokeRequest(SmokeMode.Render, Path.GetFullPath(arguments[1]));
        }

        throw new InvalidOperationException("Smoke mode requires --smoke-test or --render-smoke [output-directory].");
    }

    private static DateTime? FindInvalidLocalTime()
    {
        var candidate = new DateTime(SmokeNow.Year, 1, 1);
        var end = candidate.AddYears(2);
        while (candidate < end)
        {
            if (TimeZoneInfo.Local.IsInvalidTime(candidate))
            {
                return candidate;
            }

            candidate = candidate.AddMinutes(15);
        }

        return null;
    }

    private static void WriteRuntimeProof()
    {
        using var process = Process.GetCurrentProcess();
        var coreClrPath = process.Modules
            .Cast<ProcessModule>()
            .FirstOrDefault(module => string.Equals(module.ModuleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase))?.FileName
            ?? throw new InvalidOperationException("The smoke process did not load coreclr.dll.");

        Console.WriteLine($"Runtime core assembly: {typeof(object).Assembly.Location}");
        Console.WriteLine($"WPF assembly: {typeof(Application).Assembly.Location}");
        Console.WriteLine($"Runtime description: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"CoreCLR module: {coreClrPath}");
    }

    private static CrmDocument CreateSmokeDocument() => new()
    {
        Customers =
        [
            new Customer
            {
                Id = Guid.Parse("0ed99a12-bbf0-46dc-a72d-eac5bb1c49b3"),
                Name = "Due Customer",
                DateOfBirth = new DateOnly(1985, 6, 1),
                Ssn = "123-45-6789",
                MedicareId = "1EG4-TE5-MK73",
                Phone = "555-555-0101",
                FollowUpAt = SmokeNow.AddMinutes(-15),
                CallReason = "Confirm paperwork",
                Notes = "Synthetic smoke record.",
            },
            new Customer
            {
                Id = Guid.Parse("a4d09147-7c88-4948-aeec-d2d98cedc9c9"),
                Name = "Upcoming Customer",
                Phone = "555-555-0102",
                FollowUpAt = SmokeNow.AddHours(2),
                CallReason = "Review callback",
                Notes = "Synthetic smoke record.",
            },
            new Customer
            {
                Id = Guid.Parse("b839efd2-79d0-457a-b6a5-4a10bdf34e85"),
                Name = "Unscheduled Customer",
                Phone = "555-555-0103",
                CallReason = "No follow-up scheduled",
                Notes = "Synthetic smoke record.",
            },
        ],
    };

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private enum SmokeMode
    {
        Test,
        Render,
    }

    private sealed record SmokeRequest(SmokeMode Mode, string? RenderOutputDirectory);

    private sealed class RecoverySmokeStore : ICrmStore
    {
        private readonly CrmDocument _restored;

        public RecoverySmokeStore(CrmDocument restored)
        {
            _restored = restored;
        }

        public CrmDocument Load() => throw new CrmStoreException("Synthetic corrupted store.");

        public CrmDocument Save(CrmDocument document)
            => throw new InvalidOperationException("Recovery smoke mutations must wait for restore.");

        public void ExportBackup(string path)
            => throw new InvalidOperationException("Recovery smoke exports must wait for restore.");

        public CrmDocument RestoreBackup(string path) => _restored;
    }

    private sealed class SmokeStore : ICrmStore
    {
        private CrmDocument _document;
        private CrmDocument? _backup;

        public SmokeStore(CrmDocument document)
        {
            _document = document;
        }

        public CrmDocument Load() => _document;

        public CrmDocument Save(CrmDocument document)
        {
            if (document.Revision != _document.Revision)
            {
                throw new InvalidOperationException("Smoke store received a stale revision.");
            }

            _document = document with
            {
                Revision = document.Revision + 1,
                Customers = document.Customers.ToList(),
            };
            return _document;
        }

        public void ExportBackup(string path)
        {
            _backup = _document;
        }

        public CrmDocument RestoreBackup(string path)
        {
            if (_backup is null)
            {
                throw new InvalidOperationException("Smoke store has no backup to restore.");
            }

            _document = _backup with { Revision = _document.Revision + 1 };
            return _document;
        }
    }

    private sealed class SmokeDialogService : IUserDialogService
    {
        private readonly string? _restorePath;
        private readonly PendingChangesDecision _pendingChangesDecision;

        public SmokeDialogService(
            string? restorePath = null,
            PendingChangesDecision pendingChangesDecision = PendingChangesDecision.Discard)
        {
            _restorePath = restorePath;
            _pendingChangesDecision = pendingChangesDecision;
        }

        public PendingChangesDecision ResolveUnsavedChanges(string action) => _pendingChangesDecision;

        public bool ConfirmDelete(string customerName) => true;

        public bool ConfirmBackupExport(string path) => true;

        public bool ConfirmRestore() => true;

        public string? SelectBackupExportPath() => null;

        public string? SelectBackupRestorePath() => _restorePath;

        public bool TryCopyText(string value, out string message)
        {
            message = "Smoke mode does not access the Windows Clipboard.";
            return false;
        }

        public bool TryPasteText(out string value, out string message)
        {
            value = string.Empty;
            message = "Smoke mode does not access the Windows Clipboard.";
            return false;
        }
    }
}
