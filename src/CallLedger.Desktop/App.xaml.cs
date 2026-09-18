using System.IO;
using System.Windows;
using CallLedger.Core;
using CallLedger.Desktop.Services;
using CallLedger.Desktop.ViewModels;
using CallLedger.Vault;

namespace CallLedger.Desktop;

public partial class App : Application
{
    private CrmViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        if (SmokeRunner.IsRequested(eventArgs.Args))
        {
            await RunSmokeAsync(eventArgs.Args);
            return;
        }

        try
        {
            var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
            var store = await Task.Run(() => new EncryptedCrmStore(dataDirectory));
            var viewModel = new CrmViewModel(new StoreCoordinator(store), new DesktopDialogService());
            _viewModel = viewModel;
            var window = new MainWindow(viewModel);
            MainWindow = window;
            if (!await viewModel.LoadAsync())
            {
                window.Show();
                return;
            }

            window.Show();
        }
        catch (CrmStoreException)
        {
            ShowStartupFailure();
            Shutdown(1);
        }
        catch (Exception)
        {
            ShowStartupFailure();
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _viewModel?.Dispose();
        base.OnExit(eventArgs);
    }

    private async Task RunSmokeAsync(string[] arguments)
    {
        try
        {
            await SmokeRunner.RunAsync(arguments);
            Shutdown(0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL smoke: {exception}");
            Shutdown(1);
        }
    }

    private static void ShowStartupFailure()
    {
        const string message = "Call Ledger could not access its data folder. The data was not changed.\n\nMove Call Ledger to a writable local folder, close any other copy of the app, and confirm that you are signed in to the same Windows profile that created the encrypted data.";
        var owner = Application.Current?.MainWindow;
        if (owner is { IsVisible: true })
        {
            MessageBox.Show(owner, message, "Call Ledger could not open data", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(message, "Call Ledger could not open data", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
