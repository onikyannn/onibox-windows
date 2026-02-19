using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Onibox.Commands;
using Onibox.Models;
using Onibox.Services;
using System.Windows.Input;
using WinRT.Interop;
using Windows.Graphics;

namespace Onibox;

public partial class MainWindow : Window
{
    private readonly SettingsStorage _settingsStorage;
    private readonly ConfigManager _configManager;
    private readonly SingBoxService _singBoxService;
    private readonly SystemProxyManager _systemProxyManager;
    private readonly CredentialManager _credentialManager;
    private readonly DispatcherQueue _dispatcherQueue;

    public ICommand TrayToggleCommand { get; }
    public ICommand TrayUpdateConfigCommand { get; }
    public ICommand TrayShowCommand { get; }
    public ICommand TrayExitCommand { get; }
    public ICommand TrayDoubleClickCommand { get; }

    private TaskbarIcon? _trayIcon;
    private MenuFlyoutItem? _trayToggleItem;

    private Settings _settings = new();
    private bool _exitRequested;
    private bool _isTransitioning;
    private bool _isInitializing;
    private bool _isApplyingInboundModeSelection;
    private bool _isLoaded;
    private bool _windowPrepared;

    private AppWindow? _appWindow;

    private const string DefaultConfigUrl = "";
    private static readonly SizeInt32 InitialWindowSize = new(668, 502);
    private const int TrayToolTipMaxLength = 64;

    public MainWindow()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        TrayToggleCommand = new AsyncRelayCommand(ExecuteTrayToggleAsync);
        TrayUpdateConfigCommand = new AsyncRelayCommand(ExecuteTrayUpdateConfigAsync);
        TrayShowCommand = new RelayCommand(() => EnqueueOnUIThread(ShowWindow));
        TrayExitCommand = new AsyncRelayCommand(() => RunOnUIAsync(ExitApplicationAsync));
        TrayDoubleClickCommand = new RelayCommand(() => EnqueueOnUIThread(ShowWindow));

        InitializeComponent();
        ResolveTrayIcon();
        PrepareWindow();

        _settingsStorage = new SettingsStorage();
        _configManager = new ConfigManager(_settingsStorage);
        _singBoxService = new SingBoxService(_settingsStorage);
        _systemProxyManager = new SystemProxyManager();
        _credentialManager = new CredentialManager();

        _singBoxService.Exited += OnSingBoxExited;
        Closed += OnClosed;

        AutoStartToggleSwitch.Toggled += AutoStartToggleSwitch_OnToggled;
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;

        PrepareWindow();
        _trayIcon?.ForceCreate();

        var isFirstRun = !File.Exists(_settingsStorage.SettingsPath);
        _settings = _settingsStorage.Load();
        if (isFirstRun && string.IsNullOrWhiteSpace(_settings.ConfigUrl))
        {
            _settings.ConfigUrl = DefaultConfigUrl;
            _settingsStorage.Save(_settings);
        }

        _isInitializing = true;
        AutoStartToggleSwitch.IsOn = _settings.AutoStart;
        ApplyInboundModeSelection(_settings.InboundMode);
        _isInitializing = false;

        if (_settings.AutoStart && !SetAutoStart(true))
        {
            _isInitializing = true;
            AutoStartToggleSwitch.IsOn = false;
            _isInitializing = false;
            _settings.AutoStart = false;
            _settingsStorage.Save(_settings);
        }

        UpdateAutoStartStateText();
        ConfigUrlTextBox.Text = _settings.ConfigUrl ?? string.Empty;
        UpdateConnectionState(_singBoxService.IsRunning);

        var importHandled = await HandleImportActivationAsync();
        if (!importHandled && ShouldStartInTray())
        {
            HideWindow();
        }
    }

    private void InitializeWindow()
    {
        if (_appWindow is not null)
        {
            return;
        }

        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += OnAppWindowClosing;
    }

    private void PrepareWindow()
    {
        if (_windowPrepared)
        {
            return;
        }

        InitializeWindow();
        ConfigureTitleBar();
        TrySetWindowIcon();
        ConfigureWindowPresenter();
        SetInitialSize();
        CenterWindow();
        _windowPrepared = _appWindow is not null;
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (_appWindow?.TitleBar is not null)
        {
            _appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    private void ConfigureWindowPresenter()
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }
    }

    private void CenterWindow()
    {
        if (_appWindow is null)
        {
            return;
        }

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
        {
            return;
        }

        var workArea = displayArea.WorkArea;
        var size = _appWindow.Size;
        var centeredX = workArea.X + (workArea.Width - size.Width) / 2;
        var centeredY = workArea.Y + (workArea.Height - size.Height) / 2;
        _appWindow.Move(new PointInt32(centeredX, centeredY));
    }

    private void SetInitialSize()
    {
        if (_appWindow is null)
        {
            return;
        }

        var scale = GetRasterizationScale();
        var width = (int)Math.Round(InitialWindowSize.Width * scale);
        var height = (int)Math.Round(InitialWindowSize.Height * scale);
        _appWindow.Resize(new SizeInt32(width, height));
    }

    private double GetRasterizationScale()
    {
        var xamlScale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale;
        if (xamlScale is not null && xamlScale.Value > 0)
        {
            return xamlScale.Value;
        }

        var windowHandle = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(windowHandle);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    private void EnqueueOnUIThread(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    private void TrySetWindowIcon()
    {
        if (_appWindow is null)
        {
            return;
        }

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (!File.Exists(iconPath))
            {
                return;
            }

            _appWindow.SetIcon(iconPath);
        }
        catch
        {
            // ignore icon failures
        }
    }

    private async void UpdateConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ConfigUrlTextBox.Text.Trim();
        await UpdateConfigFromUrlAsync(url);
    }

    private async Task UpdateConfigFromUrlAsync(string url, bool showCompletionDialog = true)
    {
        if (TryGetImportUrlFromArgument(url, out var resolvedUrl) && !string.IsNullOrWhiteSpace(resolvedUrl))
        {
            url = resolvedUrl;
            ConfigUrlTextBox.Text = url;
        }

        SetControlsEnabled(false);
        var wasRunning = _singBoxService.IsRunning;

        try
        {
            if (wasRunning)
            {
                await DisconnectAsync(false);
            }

            SetIntermediateStatus(GetString("Status.Updating"));

            var configPath = await DownloadConfigWithAuthAsync(url);
            if (string.IsNullOrWhiteSpace(configPath))
            {
                if (wasRunning)
                {
                    await ConnectAsync(false);
                }

                return;
            }

            _settings.ConfigUrl = url;
            _settings.LastConfigPath = configPath;
            _settings.LastUpdatedAt = DateTimeOffset.Now;
            _settingsStorage.Save(_settings);

            if (showCompletionDialog)
            {
                await ShowMessageAsync(GetString("App.Title"), GetString("Message.ConfigUpdated"));
            }

            if (wasRunning)
            {
                await ConnectAsync(false);
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync(GetString("Dialog.Error.Title"), ex.Message);

            if (wasRunning)
            {
                await ConnectAsync(false);
            }
        }
        finally
        {
            UpdateConnectionState(_singBoxService.IsRunning);
            SetControlsEnabled(true);
        }
    }

    private async Task<bool> HandleImportActivationAsync()
    {
        if (!TryGetImportUrlFromCommandLine(out var importUrl))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(importUrl))
        {
            await ShowMessageAsync(GetString("Dialog.Error.Title"), GetString("Error.ConfigUrl.Required"));
            return true;
        }

        ConfigUrlTextBox.Text = importUrl;
        await UpdateConfigFromUrlAsync(importUrl);
        return true;
    }

    private static bool TryGetImportUrlFromCommandLine(out string? url)
    {
        url = null;
        try
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (TryGetImportUrlFromArgument(arg, out var candidate))
                {
                    url = candidate;
                    return true;
                }
            }
        }
        catch
        {
            // ignore args errors
        }

        return false;
    }

    private static bool TryGetImportUrlFromArgument(string argument, out string? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var trimmed = argument.Trim().Trim('"');
        const string prefix = "onibox://import/";
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var candidate = trimmed[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            url = TryUnescape(candidate);
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, "onibox", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "import", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped).Trim('/');
        if (!string.IsNullOrWhiteSpace(path))
        {
            url = path;
            return true;
        }

        var fromQuery = TryGetImportUrlFromQuery(uri.Query);
        if (!string.IsNullOrWhiteSpace(fromQuery))
        {
            url = fromQuery;
            return true;
        }

        return false;
    }

    private static string? TryGetImportUrlFromQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var trimmed = query;
        if (trimmed.StartsWith("?", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!string.Equals(parts[0], "url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return TryUnescape(parts[1]);
        }

        return null;
    }

    private static string TryUnescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch
        {
            return value;
        }
    }

    private async Task<string?> DownloadConfigWithAuthAsync(string url)
    {
        var authTarget = GetBasicAuthTarget(url);
        if (!string.IsNullOrWhiteSpace(authTarget) &&
            _credentialManager.TryRead(authTarget, out var cachedCredentials))
        {
            try
            {
                return await _configManager.DownloadAsync(url, cachedCredentials);
            }
            catch (BasicAuthInvalidException)
            {
                _credentialManager.Delete(authTarget);
                return await DownloadConfigWithPromptAsync(url, authTarget);
            }
            catch (BasicAuthRequiredException)
            {
                _credentialManager.Delete(authTarget);
                return await DownloadConfigWithPromptAsync(url, authTarget);
            }
        }

        try
        {
            return await _configManager.DownloadAsync(url);
        }
        catch (BasicAuthRequiredException)
        {
            return await DownloadConfigWithPromptAsync(url, authTarget);
        }
    }

    private async Task<string?> DownloadConfigWithPromptAsync(string url, string? authTarget)
    {
        while (true)
        {
            var credentials = await ShowBasicAuthDialogAsync();
            if (credentials is null)
            {
                return null;
            }

            try
            {
                var configPath = await _configManager.DownloadAsync(url, credentials);
                if (!string.IsNullOrWhiteSpace(authTarget))
                {
                    _credentialManager.Write(authTarget, credentials);
                }

                return configPath;
            }
            catch (BasicAuthInvalidException)
            {
                await ShowMessageAsync(GetString("Dialog.Error.Title"), GetString("Error.BasicAuth.Invalid"));
            }
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_singBoxService.IsRunning)
        {
            await DisconnectAsync(true);
        }
        else
        {
            await ConnectAsync(true);
        }
    }

    private async Task ConnectAsync(bool manageButtons)
    {
        if (_isTransitioning)
        {
            return;
        }

        _isTransitioning = true;
        if (manageButtons)
        {
            SetControlsEnabled(false);
        }

        SetIntermediateStatus(GetString("Status.Connecting"));

        try
        {
            var sourceConfigPath = _settings.LastConfigPath;
            if (string.IsNullOrWhiteSpace(sourceConfigPath))
            {
                sourceConfigPath = _settingsStorage.ConfigPath;
            }

            if (string.IsNullOrWhiteSpace(sourceConfigPath) || !File.Exists(sourceConfigPath))
            {
                await ShowMessageAsync(GetString("App.Title"), GetString("Message.DownloadConfigFirst"));
                UpdateConnectionState(false);
                return;
            }

            var runtimeConfigPath = PrepareRuntimeConfig(sourceConfigPath);
            await EnsureSystemProxyForConfigAsync(runtimeConfigPath);
            await Task.Run(() => _singBoxService.Start(runtimeConfigPath));
            UpdateConnectionState(true);
        }
        catch (Exception ex)
        {
            await RestoreSystemProxyAsync();
            var details = _singBoxService.LogPath is null
                ? ex.Message
                : FormatString("Message.LogPathWithErrorFormat", ex.Message, _singBoxService.LogPath);
            await ShowMessageAsync(GetString("Dialog.Error.Title"), details);
            UpdateConnectionState(false);
        }
        finally
        {
            _isTransitioning = false;
            if (manageButtons)
            {
                SetControlsEnabled(true);
            }
        }
    }

    private async Task DisconnectAsync(bool manageButtons)
    {
        if (_isTransitioning)
        {
            return;
        }

        _isTransitioning = true;
        if (manageButtons)
        {
            SetControlsEnabled(false);
        }

        SetIntermediateStatus(GetString("Status.Disconnecting"));

        try
        {
            await Task.Run(() => _singBoxService.Stop());
            await RestoreSystemProxyAsync();
            UpdateConnectionState(false);
        }
        finally
        {
            _isTransitioning = false;
            if (manageButtons)
            {
                SetControlsEnabled(true);
            }
        }
    }

    private void SetControlsEnabled(bool isEnabled)
    {
        UpdateConfigButton.IsEnabled = isEnabled;
        ConfigUrlTextBox.IsEnabled = isEnabled;
        ConnectButton.IsEnabled = isEnabled;
        InboundModeComboBox.IsEnabled = isEnabled;
    }

    private void UpdateConnectionState(bool isConnected)
    {
        var status = isConnected ? GetString("Status.Connected") : GetString("Status.Disconnected");
        StatusInfoBar!.Message = status;
        StatusInfoBar.Severity = isConnected ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        StatusInfoBar.IsOpen = true;

        ConnectButtonText!.Text = isConnected ? GetString("Action.Disconnect") : GetString("Action.Connect");
        ConnectButtonIcon!.Symbol = isConnected ? Symbol.Stop : Symbol.Play;

        ConnectionProgressRing!.IsActive = false;
        ConnectionProgressRing.Visibility = Visibility.Collapsed;

        UpdateTrayState(isConnected, status);
    }

    private void SetIntermediateStatus(string status)
    {
        StatusInfoBar!.Message = status;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.IsOpen = true;

        ConnectionProgressRing!.IsActive = true;
        ConnectionProgressRing.Visibility = Visibility.Visible;

        if (_trayToggleItem is not null)
        {
            _trayToggleItem.Text = status;
            _trayToggleItem.IsEnabled = false;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.ToolTipText = BuildTrayToolTip(status);
        }
    }

    private async Task EnsureSystemProxyForConfigAsync(string configPath)
    {
        if (_settings.InboundMode != InboundMode.Proxy)
        {
            await RestoreSystemProxyAsync();
            return;
        }

        if (ConfigInspector.TryGetMixedInboundProxy(configPath, out var proxy, out var error))
        {
            if (proxy is not null &&
                !_systemProxyManager.TryEnable(proxy.Host, proxy.Port, out var proxyError))
            {
                await ShowMessageAsync(GetString("App.Title"),
                    FormatString("Message.ProxyEnableFailed", proxyError ?? string.Empty));
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            await ShowMessageAsync(GetString("App.Title"),
                FormatString("Message.ProxyDetectFailed", error ?? string.Empty));
        }

        await RestoreSystemProxyAsync();
    }

    private string PrepareRuntimeConfig(string sourceConfigPath)
    {
        _settingsStorage.EnsureLogsDirectory();
        ConfigInspector.BuildRuntimeConfig(sourceConfigPath, _settings.InboundMode, _settingsStorage.RuntimeConfigPath);
        return _settingsStorage.RuntimeConfigPath;
    }

    private void ApplyInboundModeSelection(InboundMode mode)
    {
        _isApplyingInboundModeSelection = true;
        try
        {
            InboundModeComboBox.SelectedIndex = mode == InboundMode.Tun ? 1 : 0;
        }
        finally
        {
            _isApplyingInboundModeSelection = false;
        }
    }

    private InboundMode GetSelectedInboundMode()
    {
        if (InboundModeComboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            string.Equals(tag, "tun", StringComparison.OrdinalIgnoreCase))
        {
            return InboundMode.Tun;
        }

        return InboundMode.Proxy;
    }

    private async void InboundModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isApplyingInboundModeSelection || !_isLoaded)
        {
            return;
        }

        var selectedMode = GetSelectedInboundMode();
        if (_settings.InboundMode == selectedMode)
        {
            return;
        }

        if (_isTransitioning)
        {
            ApplyInboundModeSelection(_settings.InboundMode);
            return;
        }

        _settings.InboundMode = selectedMode;
        _settingsStorage.Save(_settings);

        if (_singBoxService.IsRunning)
        {
            await DisconnectAsync(false);
            await ConnectAsync(false);
            return;
        }

        if (selectedMode == InboundMode.Tun)
        {
            await RestoreSystemProxyAsync();
        }
    }

    private async Task RestoreSystemProxyAsync()
    {
        if (!_systemProxyManager.IsManaging)
        {
            return;
        }

        if (_systemProxyManager.TryRestore(out var error))
        {
            return;
        }

        await ShowMessageAsync(GetString("App.Title"),
            FormatString("Message.ProxyRestoreFailed", error ?? string.Empty));
    }

    private void UpdateAutoStartStateText()
    {
        AutoStartStateText!.Text = AutoStartToggleSwitch.IsOn
            ? GetString("Toggle.AutoStart.On")
            : GetString("Toggle.AutoStart.Off");
    }

    private async void AutoStartToggleSwitch_OnToggled(object sender, RoutedEventArgs e)
    {
        UpdateAutoStartStateText();
        if (_isInitializing)
        {
            return;
        }

        var enable = AutoStartToggleSwitch.IsOn;
        if (!SetAutoStart(enable))
        {
            _isInitializing = true;
            AutoStartToggleSwitch.IsOn = !enable;
            _isInitializing = false;
            await ShowMessageAsync(GetString("App.Title"), GetString("Message.AutoStartFailed"));
            return;
        }

        _settings.AutoStart = enable;
        _settingsStorage.Save(_settings);
    }

    private bool SetAutoStart(bool enable)
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                          ?? Path.Combine(AppContext.BaseDirectory, "Onibox.exe");
            if (enable)
            {
                return AutoStartScheduler.Enable(exePath);
            }
            else
            {
                return AutoStartScheduler.Disable();
            }
        }
        catch
        {
            // ignore scheduler errors
            return false;
        }
    }

    private void OnSingBoxExited(object? sender, EventArgs e)
    {
        EnqueueOnUIThread(() => _ = HandleSingBoxExitedAsync());
    }

    private async Task HandleSingBoxExitedAsync()
    {
        await RestoreSystemProxyAsync();
        UpdateConnectionState(false);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _singBoxService.Exited -= OnSingBoxExited;
        _singBoxService.Stop();
        _ = _systemProxyManager.TryRestore(out _);
        _trayIcon?.Dispose();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested)
        {
            return;
        }

        args.Cancel = true;
        HideWindow();
    }

    private void ShowWindow()
    {
        WindowExtensions.Show(this, disableEfficiencyMode: true);
        Activate();
        BringToFront();
    }

    private void HideWindow()
    {
        WindowExtensions.Hide(this, enableEfficiencyMode: true);
    }

    private void BringToFront()
    {
        try
        {
            _appWindow?.MoveInZOrderAtTop();
            var hwnd = WindowNative.GetWindowHandle(this);
            ShowWindowNative(hwnd, SwRestore);
            SetForegroundWindow(hwnd);
        }
        catch
        {
            // ignore focus errors
        }
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool ShowWindowNative(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static string BuildTrayToolTip(string status)
    {
        var text = FormatString("Tray.Tooltip.Format", status);
        return text.Length > TrayToolTipMaxLength ? GetString("Tray.Tooltip.Short") : text;
    }

    private void ResolveTrayIcon()
    {
        _trayIcon = TrayIcon;

        if (_trayIcon?.ContextFlyout is MenuFlyout menu && menu.Items.Count > 0)
        {
            _trayToggleItem = menu.Items[0] as MenuFlyoutItem;
        }
    }

    private void UpdateTrayState(bool isConnected, string status)
    {
        if (_trayToggleItem is not null)
        {
            _trayToggleItem.Text = isConnected ? GetString("Action.Disconnect") : GetString("Action.Connect");
            _trayToggleItem.IsEnabled = true;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.ToolTipText = BuildTrayToolTip(status);
        }
    }

    private Task ExecuteTrayToggleAsync()
    {
        return RunOnUIAsync(() => _singBoxService.IsRunning
            ? DisconnectAsync(true)
            : ConnectAsync(true));
    }

    private Task ExecuteTrayUpdateConfigAsync()
    {
        return RunOnUIAsync(() =>
        {
            var url = (ConfigUrlTextBox.Text ?? string.Empty).Trim();
            return UpdateConfigFromUrlAsync(url, showCompletionDialog: false);
        });
    }

    private Task RunOnUIAsync(Func<Task> action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            tcs.TrySetException(new InvalidOperationException(GetString("Error.Ui.DispatchFailed")));
        }

        return tcs.Task;
    }

    private async Task ExitApplicationAsync()
    {
        _exitRequested = true;
        _singBoxService.Exited -= OnSingBoxExited;
        _singBoxService.Stop();
        await RestoreSystemProxyAsync();
        _trayIcon?.Dispose();
        Close();
    }

    private static bool ShouldStartInTray()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (string.Equals(arg, AutoStartScheduler.AutoStartArg, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore args errors
        }

        return false;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var root = (Content as FrameworkElement)?.XamlRoot;
        if (root is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = GetString("Dialog.OkButton"),
            XamlRoot = root
        };

        await dialog.ShowAsync();
    }

    private async Task<NetworkCredential?> ShowBasicAuthDialogAsync()
    {
        var root = (Content as FrameworkElement)?.XamlRoot;
        if (root is null)
        {
            return null;
        }

        var usernameBox = new TextBox
        {
            Header = GetString("Dialog.Auth.Username"),
            MinWidth = 320
        };

        var passwordBox = new PasswordBox
        {
            Header = GetString("Dialog.Auth.Password")
        };

        var panel = new StackPanel
        {
            Spacing = 12
        };

        panel.Children.Add(usernameBox);
        panel.Children.Add(passwordBox);

        var dialog = new ContentDialog
        {
            Title = GetString("Dialog.Auth.Title"),
            Content = panel,
            PrimaryButtonText = GetString("Dialog.OkButton"),
            CloseButtonText = GetString("Dialog.CancelButton"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root
        };

        dialog.IsPrimaryButtonEnabled = false;
        void UpdatePrimaryState()
        {
            dialog.IsPrimaryButtonEnabled =
                !string.IsNullOrWhiteSpace(usernameBox.Text) &&
                !string.IsNullOrWhiteSpace(passwordBox.Password);
        }

        usernameBox.TextChanged += (_, _) => UpdatePrimaryState();
        passwordBox.PasswordChanged += (_, _) => UpdatePrimaryState();

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return new NetworkCredential(usernameBox.Text.Trim(), passwordBox.Password);
    }

    private static string? GetBasicAuthTarget(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return CredentialManager.BuildBasicAuthTarget(uri);
    }

    private static string GetString(string key) => Localization.GetString(key);

    private static string FormatString(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, GetString(key), args);

}
