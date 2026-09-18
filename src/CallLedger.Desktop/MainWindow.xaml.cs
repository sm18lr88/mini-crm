using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CallLedger.Desktop.ViewModels;

namespace CallLedger.Desktop;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private readonly CrmViewModel _viewModel;
    private DispatcherTimer? _timeSensitiveTimer;
    private readonly bool _shutdownOnClosed;
    private bool? _isCompactMode;
    private bool _isClosing;
    private bool _closeApproved;
    private bool _restoringSelection;
    private bool _disposedForSmoke;

    public MainWindow(CrmViewModel viewModel)
        : this(viewModel, shutdownOnClosed: true)
    {
    }

    internal MainWindow(CrmViewModel viewModel, bool shutdownOnClosed)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        _shutdownOnClosed = shutdownOnClosed;
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyWindowPreferences();

        _timeSensitiveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1),
        };
        _timeSensitiveTimer.Tick += OnTimeSensitiveTimerTick;
        _timeSensitiveTimer.Start();
    }

    internal void RenderToPng(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var size = new Size(Width, Height);
        ShellRoot.Measure(size);
        ShellRoot.Arrange(new Rect(size));
        ShellRoot.UpdateLayout();

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(size.Width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(size.Height));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96,
            96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(ShellRoot);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    internal void DisposeForSmoke()
    {
        if (_disposedForSmoke)
        {
            return;
        }

        _disposedForSmoke = true;
        _timeSensitiveTimer?.Stop();
        _timeSensitiveTimer?.Tick -= OnTimeSensitiveTimerTick;
        SourceInitialized -= OnSourceInitialized;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        DataContext = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (!_viewModel.CompactMode)
        {
            FocusSearchBox();
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (!_shutdownOnClosed || _closeApproved)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_isClosing || _viewModel.IsBusy)
        {
            return;
        }

        _isClosing = true;
        try
        {
            if (await _viewModel.CanCloseAsync())
            {
                _closeApproved = true;
                _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Normal);
            }
        }
        finally
        {
            _isClosing = false;
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        DisposeForSmoke();
        if (_shutdownOnClosed)
        {
            Application.Current.Shutdown();
        }
    }

    private async void OnCustomerSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_restoringSelection || sender is not Selector selector || eventArgs.AddedItems.Count != 1)
        {
            return;
        }

        var requestedRow = eventArgs.AddedItems[0] as CustomerRow;
        if (requestedRow is null || ReferenceEquals(requestedRow, _viewModel.SelectedRow))
        {
            return;
        }

        if (await _viewModel.SelectCustomerAsync(requestedRow))
        {
            return;
        }

        _restoringSelection = true;
        try
        {
            selector.SelectedItem = _viewModel.SelectedRow;
        }
        finally
        {
            _restoringSelection = false;
        }
    }

    private async void OnShowSensitiveByDefaultClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is CheckBox { IsChecked: bool value })
        {
            await _viewModel.SetShowSensitiveByDefaultAsync(value);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (eventArgs.Key == Key.N && TryExecute(_viewModel.NewCommand))
            {
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Key.S && TryExecute(_viewModel.SaveCommand))
            {
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Key.F)
            {
                if (_viewModel.CompactMode)
                {
                    ExpandAndFocusSearch();
                }
                else
                {
                    FocusSearchBox();
                }

                eventArgs.Handled = true;
                return;
            }
        }

        if (eventArgs.Key == Key.Escape && TryExecute(_viewModel.CancelCommand))
        {
            eventArgs.Handled = true;
        }
    }

    private void OnTimeSensitiveTimerTick(object? sender, EventArgs eventArgs)
        => _viewModel.RefreshTimeSensitiveState();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(CrmViewModel.AlwaysOnTop))
        {
            Topmost = _viewModel.AlwaysOnTop;
            return;
        }

        if (eventArgs.PropertyName == nameof(CrmViewModel.CompactMode))
        {
            ApplyWindowPreferences();
        }
    }

    private void ApplyWindowPreferences()
    {
        Topmost = _viewModel.AlwaysOnTop;
        if (_isCompactMode == _viewModel.CompactMode)
        {
            return;
        }

        _isCompactMode = _viewModel.CompactMode;
        if (_viewModel.CompactMode)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }

            MinWidth = 360;
            MinHeight = 520;
            Width = 400;
            Height = 600;
            return;
        }

        MinWidth = 960;
        MinHeight = 620;
        Width = 1200;
        Height = 800;
    }

    private void ExpandAndFocusSearch()
    {
        var command = _viewModel.ToggleCompactModeCommand;
        if (!command.CanExecute(null))
        {
            return;
        }

        EventHandler? focusAfterModeChange = null;
        focusAfterModeChange = (_, _) =>
        {
            if (!command.CanExecute(null))
            {
                return;
            }

            command.CanExecuteChanged -= focusAfterModeChange;
            if (!_viewModel.CompactMode)
            {
                Dispatcher.BeginInvoke(new Action(FocusSearchBox), DispatcherPriority.Background);
            }
        };

        command.CanExecuteChanged += focusAfterModeChange;
        command.Execute(null);
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private static bool TryExecute(ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);
}
