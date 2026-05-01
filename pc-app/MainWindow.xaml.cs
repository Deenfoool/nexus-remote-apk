using QRCoder;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace NexusRemotePC;

public partial class MainWindow : Window
{
    private readonly CompanionStore _store = new();
    private readonly NexusServer _server;
    private readonly NexusRemotePC.Media.BrowserBridgeServer _browserBridge = NexusRemotePC.Media.BrowserBridgeServer.Shared;
    private readonly DiscoveryService _discovery;
    private Forms.NotifyIcon? _trayIcon;
    private ProgramManagerWindow? _programManager;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer = new();
    private bool _reallyClose;
    private string _updateStatus = "Проверка обновлений ещё не выполнялась.";

    public MainWindow()
    {
        InitializeComponent();
        _server = new NexusServer(_store, OpenProgramManagerOnUi, ConfirmPairing, ConfirmDangerousCommand);
        _discovery = new DiscoveryService(_server.Port);
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SetupTray();
        AutostartCheck.IsChecked = AutostartManager.IsEnabled();
        await _server.StartAsync();
        await _browserBridge.StartAsync();
        _discovery.Start();
        AppLogger.Info("Сервер, browser bridge и окно управления запущены.");
        _refreshTimer.Interval = TimeSpan.FromSeconds(3);
        _refreshTimer.Tick += (_, _) => RenderState();
        _refreshTimer.Start();
        RenderState();
    }

    private void RenderState()
    {
        IpText.Text = string.Join(", ", NetworkUtil.GetLocalIPv4Addresses());
        PortText.Text = _server.Port.ToString();
        TokenText.Text = "QR действует 5 минут";
        VersionText.Text = GetVersionText();
        DevicesList.ItemsSource = _store.LoadDevices()
            .Select(device => new DeviceRow(device.Id, device.Name, device.LastSeenAt.ToLocalTime().ToString("dd.MM HH:mm")))
            .ToArray();
        EventsList.ItemsSource = _store.LoadEvents()
            .Take(8)
            .Select(item => new EventRow(item.CreatedAt.ToLocalTime().ToString("HH:mm:ss"), item.Level, item.Message))
            .ToArray();
        LogsText.Text = AppLogger.ReadTail();
        UpdateStatusText.Text = _updateStatus;
        var deviceCount = _store.LoadDevices().Count;
        StatusText.Text = _server.IsRunning
            ? deviceCount > 0
                ? $"Онлайн. Подключено устройств: {deviceCount}."
                : "Онлайн. Откройте Android-приложение и сканируйте QR."
            : "Сервер остановлен";
        RenderQr();
    }

    private void RenderQr()
    {
        var payload = JsonSerializer.Serialize(new
        {
            app = "nexus-remote",
            mode = "pair",
            host = NetworkUtil.GetPrimaryIPv4Address(),
            port = _server.Port,
            pairingToken = _store.PairingToken,
            expiresAt = _store.PairingTokenCreatedAt.AddMinutes(5)
        });

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(14, new byte[] { 3, 17, 31 }, new byte[] { 247, 251, 255 });
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        QrImage.Source = bitmap;
    }

    private void SetupTray()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Nexus Remote",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add("Открыть", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        _trayIcon.ContextMenuStrip.Items.Add("Менеджер программ", null, (_, _) => Dispatcher.Invoke(OpenProgramManagerOnUi));
        _trayIcon.ContextMenuStrip.Items.Add("Сбросить устройства", null, (_, _) => Dispatcher.Invoke(() =>
        {
            _store.ClearDevices();
            RenderState();
        }));
        _trayIcon.ContextMenuStrip.Items.Add("Выйти", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OpenProgramManagerOnUi()
    {
        if (_programManager is { IsVisible: true })
        {
            _programManager.Activate();
            return;
        }

        _programManager = new ProgramManagerWindow(_store) { Owner = this };
        _programManager.Show();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_reallyClose) return;
        e.Cancel = true;
        Hide();
        _trayIcon?.ShowBalloonTip(1200, "Nexus Remote PC", "Сервер продолжает работать в трее.", Forms.ToolTipIcon.Info);
    }

    private bool ConfirmPairing(string deviceName, string message)
    {
        var result = System.Windows.MessageBox.Show(
            $"{message}\n\nУстройство: {deviceName}\n\nРазрешить доступ?",
            "Новое устройство",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        _store.AddEvent(result == MessageBoxResult.Yes ? "Событие" : "Событие", $"{(result == MessageBoxResult.Yes ? "Разрешено" : "Отклонено")} сопряжение: {deviceName}");
        RenderState();
        return result == MessageBoxResult.Yes;
    }

    private bool ConfirmDangerousCommand(string deviceName, string message)
    {
        var result = System.Windows.MessageBox.Show(
            $"{message}\n\nЗапросило устройство: {deviceName}",
            "Подтверждение команды",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void ResetDevices_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Все телефоны потеряют доступ и должны будут пройти QR-сопряжение заново. Продолжить?",
            "Сброс устройств",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;
        _store.ClearDevices();
        RenderState();
    }

    private void RemoveDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is not DeviceRow row) return;
        _store.RemoveDevice(row.Id);
        RenderState();
    }

    private void Programs_Click(object sender, RoutedEventArgs e) => OpenProgramManagerOnUi();

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _store.RotatePairingToken();
        AppLogger.Info("QR для сопряжения обновлён вручную.");
        RenderState();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApp();

    private void RefreshLogs_Click(object sender, RoutedEventArgs e) => RenderState();

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppLogger.DirectoryPath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppLogger.DirectoryPath)
        {
            UseShellExecute = true
        });
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _updateStatus = "Проверяю обновления...";
            RenderState();
            var result = await UpdateChecker.CheckPcAppAsync(GetCurrentVersion());
            _updateStatus = result.HasUpdate
                ? $"{result.Summary} GitHub: {result.Url}"
                : result.Summary;
            AppLogger.Info($"Результат проверки обновлений: {_updateStatus}");
        }
        catch (Exception ex)
        {
            _updateStatus = $"Не удалось проверить обновления: {ex.Message}";
            AppLogger.Error("Ошибка проверки обновлений.", ex);
        }
        RenderState();
    }

    private void AutostartCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        AutostartManager.SetEnabled(AutostartCheck.IsChecked == true);
    }

    private void ExitApp()
    {
        _reallyClose = true;
        _refreshTimer.Stop();
        _discovery.Dispose();
        _browserBridge.Dispose();
        _server.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        System.Windows.Application.Current.Shutdown();
    }

    private static Version GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
    }

    private static string GetVersionText()
    {
        var version = GetCurrentVersion();
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }
}

public sealed record DeviceRow(string Id, string Name, string LastSeenAt);
public sealed record EventRow(string CreatedAt, string Level, string Message);
