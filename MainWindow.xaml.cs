using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using Serilog;
using Wincent;
using Application = System.Windows.Application;
using NotifyIcon = System.Windows.Forms.NotifyIcon;


namespace ScourgifyMini
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        private static Mutex _mutex = null;
        private const string MutexName = "ScourgifyMini_SingleInstance_Mutex";

        private NotifyIcon trayIcon;
        private Config config;
        private AboutWindow aboutWindow;

        private ToolStripMenuItem autoStartItem;
        private ToolStripMenuItem languageMenu;
        private ToolStripMenuItem noTraceModeItem;
        private ToolStripMenuItem aboutItem;
        private ToolStripMenuItem exitItem;
        private readonly Dictionary<string, ToolStripMenuItem> languageItems = new Dictionary<string, ToolStripMenuItem>();

        private QuickAccessManager _quickAccessManager;
        private QuickAccessLock _quickAccessLock;

        private string _logPath;
        private readonly object _shutdownLock = new object();
        private readonly SemaphoreSlim _noTraceModeSemaphore = new SemaphoreSlim(1, 1);
        private bool _ownsMutex = false;
        private bool _shutdownStarted = false;
        private bool _shutdownCompleted = false;
        private bool _disposed = false;

        public MainWindow()
        {
            try
            {
                InitializeLogger();

                bool createdNew;
                _mutex = new Mutex(true, MutexName, out createdNew);
                _ownsMutex = createdNew;

                if (!createdNew)
                {
                    Log.Warning("Another instance is already running");
                    System.Windows.MessageBox.Show(
                        Properties.Resources.AlreadyRunning,
                        Properties.Resources.Warning,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    _mutex.Dispose();
                    _mutex = null;
                    Application.Current.Shutdown();
                    return;
                }

                config = Config.Load();
                InitializeLanguage();
                LogStartupContext();
                InitializeComponent();
                Closing += OnWindowClosing;
                Closed += OnWindowClosed;

                _quickAccessManager = new QuickAccessManager(new QuickAccessManagerOptions
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    RetryPolicy = RetryPolicy.Standard
                });

                SynchronizeAutoStartOnStartup();
                InitializeTrayIcon();

                if (config.NoTraceMode)
                {
                    StartNoTraceModeFromStartup();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during initialization");
                System.Windows.MessageBox.Show(
                    Properties.Resources.InitializationFailed,
                    Properties.Resources.Warning,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ShutdownApplication("InitializationFailure");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || _disposed)
                return;

            ShutdownApplication("Dispose");
        }

        private void InitializeLogger()
        {
            _logPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "logs", "ScourgifyMini-.log");
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath));

            var logConfig = new LoggerConfiguration()
                .MinimumLevel.Debug();

#if DEBUG
            logConfig = logConfig.WriteTo.Console();
#endif
            logConfig = logConfig.WriteTo.File(
                _logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 3,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

            Log.Logger = logConfig.CreateLogger();
            Log.Information("ScourgifyMini Started");
        }

        private void InitializeLanguage()
        {
            config.Language = Config.NormalizeLanguage(config.Language);
            var culture = CultureInfo.GetCultureInfo(config.Language);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            Properties.Resources.Culture = culture;
        }

        private void LogStartupContext()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            Log.Information(
                "Startup context: Version={Version}, ExecutablePath={ExecutablePath}, ConfigPath={ConfigPath}, LogPath={LogPath}, Language={Language}, AutoStart={AutoStart}, NoTraceMode={NoTraceMode}, CleanupNewRecentLinksOnUnlock={CleanupNewRecentLinksOnUnlock}",
                assembly.GetName().Version,
                assembly.Location,
                Config.FilePath,
                _logPath,
                config.Language,
                config.AutoStart,
                config.NoTraceMode,
                config.CleanupNewRecentLinksOnUnlock);
        }

        private void SynchronizeAutoStartOnStartup()
        {
            Exception error;
            if (TryUpdateAutoStart(config.AutoStart, out error))
                return;

            Log.Warning(error, "Failed to synchronize auto-start registration during startup");
            if (config.AutoStart)
            {
                config.AutoStart = false;
                Config.Save(config);
            }
        }

        private void InitializeTrayIcon()
        {
            trayIcon = new NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();

            autoStartItem = new ToolStripMenuItem(
                Properties.Resources.AutoStart,
                null, OnAutoStartClick)
            {
                Checked = config.AutoStart,
                CheckOnClick = true
            };

            noTraceModeItem = new ToolStripMenuItem(
                Properties.Resources.IncognitoMode,
                null, OnNoTraceModeClick)
            {
                Checked = config.NoTraceMode,
                CheckOnClick = true,
                Enabled = true
            };

            languageMenu = new ToolStripMenuItem(Properties.Resources.Language);
            foreach (var lang in Config.SupportedLanguages)
            {
                var langItem = new ToolStripMenuItem(lang.DisplayName)
                {
                    Tag = lang.Code,
                    Checked = lang.Code == config.Language,
                    CheckOnClick = false
                };
                langItem.Click += OnLanguageItemClick;
                languageMenu.DropDownItems.Add(langItem);
                languageItems[lang.Code] = langItem;
            }

            aboutItem = new ToolStripMenuItem(
                Properties.Resources.About,
                null, OnAboutClick);

            exitItem = new ToolStripMenuItem(
                Properties.Resources.Exit,
                null, OnExitClick);

            contextMenu.Items.AddRange(new ToolStripItem[]
            {
                autoStartItem,
                noTraceModeItem,
                new ToolStripSeparator(),
                languageMenu,
                aboutItem,
                new ToolStripSeparator(),
                exitItem
            });

            trayIcon.ContextMenuStrip = contextMenu;
        }

        private void OnLanguageItemClick(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem == null || !(menuItem.Tag is string langCode))
                return;

            foreach (var item in languageItems.Values)
            {
                item.Checked = false;
            }
            menuItem.Checked = true;

            string previousLanguage = config.Language;
            config.Language = langCode;
            Config.Save(config);

            InitializeLanguage();
            RefreshMenuTexts();
            Log.Information(
                "Language changed: PreviousLanguage={PreviousLanguage}, Language={Language}",
                previousLanguage,
                config.Language);
        }

        private void RefreshMenuTexts()
        {
            if (autoStartItem != null)
                autoStartItem.Text = Properties.Resources.AutoStart;

            if (noTraceModeItem != null)
                noTraceModeItem.Text = Properties.Resources.IncognitoMode;

            if (languageMenu != null)
                languageMenu.Text = Properties.Resources.Language;

            if (aboutItem != null)
                aboutItem.Text = Properties.Resources.About;

            if (exitItem != null)
                exitItem.Text = Properties.Resources.Exit;

            foreach (var languageItem in languageItems)
            {
                languageItem.Value.Checked = languageItem.Key == config.Language;
            }

            if (aboutWindow != null)
            {
                aboutWindow.RefreshLocalizedText();
            }
        }

        private void OnAutoStartClick(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem == null)
                return;

            bool previousAutoStart = config.AutoStart;
            bool requestedAutoStart = menuItem.Checked;

            Exception error;
            if (TryUpdateAutoStart(requestedAutoStart, out error))
            {
                config.AutoStart = requestedAutoStart;
                Config.Save(config);
                Log.Information(
                    "Auto-start changed: RequestedAutoStart={RequestedAutoStart}, AutoStart={AutoStart}",
                    requestedAutoStart,
                    config.AutoStart);
                return;
            }

            Log.Warning(
                error,
                "Failed to update auto-start registration from tray menu: RequestedAutoStart={RequestedAutoStart}, PreviousAutoStart={PreviousAutoStart}",
                requestedAutoStart,
                previousAutoStart);
            config.AutoStart = previousAutoStart;
            menuItem.Checked = previousAutoStart;
            Config.Save(config);

            System.Windows.MessageBox.Show(
                Properties.Resources.AutoStartUpdateFailed,
                Properties.Resources.Warning,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private async void OnNoTraceModeClick(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem == null)
                return;

            bool previousNoTraceMode = config.NoTraceMode;
            bool requestedNoTraceMode = menuItem.Checked;
            Log.Information(
                "No-trace mode change requested: RequestedNoTraceMode={RequestedNoTraceMode}, PreviousNoTraceMode={PreviousNoTraceMode}",
                requestedNoTraceMode,
                previousNoTraceMode);

            menuItem.Enabled = false;

            try
            {
                if (requestedNoTraceMode)
                {
                    await EnterNoTraceModeAsync();
                    if (_shutdownStarted)
                        return;

                    config.NoTraceMode = true;
                }
                else
                {
                    await ExitNoTraceModeAsync();
                    if (_shutdownStarted)
                        return;

                    config.NoTraceMode = false;
                }

                Config.Save(config);
                Log.Information("No-trace mode config changed: NoTraceMode={NoTraceMode}", config.NoTraceMode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to change no-trace mode");
                config.NoTraceMode = previousNoTraceMode;
                menuItem.Checked = previousNoTraceMode;
                Config.Save(config);

                System.Windows.MessageBox.Show(
                    ex.Message,
                    Properties.Resources.Warning,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                if (!_shutdownStarted)
                {
                    menuItem.Enabled = true;
                }
            }
        }

        private async void StartNoTraceModeFromStartup()
        {
            if (noTraceModeItem != null)
                noTraceModeItem.Enabled = false;

            Log.Information("Starting no-trace mode from saved config");
            try
            {
                await EnterNoTraceModeAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start no-trace mode from saved config");
                config.NoTraceMode = false;
                Config.Save(config);

                if (noTraceModeItem != null)
                {
                    noTraceModeItem.Checked = false;
                }
            }
            finally
            {
                if (noTraceModeItem != null && !_shutdownStarted)
                    noTraceModeItem.Enabled = true;
            }
        }

        private async Task EnterNoTraceModeAsync()
        {
            await _noTraceModeSemaphore.WaitAsync();
            try
            {
                await Task.Run(() => EnterNoTraceModeUnsafe());
            }
            finally
            {
                _noTraceModeSemaphore.Release();
            }
        }

        private async Task ExitNoTraceModeAsync()
        {
            await _noTraceModeSemaphore.WaitAsync();
            try
            {
                await Task.Run(() => ExitNoTraceModeUnsafe());
            }
            finally
            {
                _noTraceModeSemaphore.Release();
            }
        }

        private void EnterNoTraceModeUnsafe()
        {
            if (_quickAccessLock != null)
                return;

            if (_quickAccessManager == null)
                throw new ObjectDisposedException(nameof(_quickAccessManager));

            _quickAccessLock = _quickAccessManager.LockQuickAccess();
            Log.Information(
                "No-trace mode started: Target={Target}, LockedFileCount={LockedFileCount}, InitialShortcutCount={InitialShortcutCount}",
                _quickAccessLock.Target,
                _quickAccessLock.LockedFileCount,
                _quickAccessLock.InitialShortcutPaths.Count);
        }

        private void ExitNoTraceModeUnsafe()
        {
            var quickAccessLock = _quickAccessLock;
            if (quickAccessLock == null)
                return;

            try
            {
                var report = quickAccessLock.Unlock(new QuickAccessUnlockOptions
                {
                    CleanupNewRecentLinks = config == null || config.CleanupNewRecentLinksOnUnlock
                });

                Log.Information(
                    "No-trace mode stopped: CurrentShortcutCount={CurrentShortcutCount}, NewShortcutCount={NewShortcutCount}, DeletedShortcutCount={DeletedShortcutCount}, FailedShortcutDeletionCount={FailedShortcutDeletionCount}",
                    report.CurrentShortcutPaths.Count,
                    report.NewShortcutPaths.Count,
                    report.DeletedShortcutPaths.Count,
                    report.FailedShortcutDeletions.Count);

                foreach (var failure in report.FailedShortcutDeletions)
                {
                    Log.Warning(
                        failure.Error,
                        "Failed to delete new Recent shortcut during no-trace unlock: {Path}",
                        failure.Path);
                }
            }
            finally
            {
                _quickAccessLock = null;
            }
        }

        private void OnAboutClick(object sender, EventArgs e)
        {
            if (aboutWindow != null)
            {
                aboutWindow.Activate();
                return;
            }

            aboutWindow = new AboutWindow();
            aboutWindow.Closed += OnAboutWindowClosed;
            aboutWindow.Show();
        }

        private void OnAboutWindowClosed(object sender, EventArgs e)
        {
            aboutWindow = null;
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            ShutdownApplication("TrayExit");
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (_shutdownCompleted)
                return;

            e.Cancel = true;
            ShutdownApplication("WindowClosing");
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            Dispose();
        }

        private void ShutdownApplication(string source)
        {
            lock (_shutdownLock)
            {
                if (_shutdownStarted)
                    return;

                _shutdownStarted = true;
            }

            try
            {
                Log.Information("Shutdown started: Source={Source}", source);
                DisableTrayForShutdown();
                ExitNoTraceModeForShutdown();
                DisposeQuickAccessManager();
                CloseAboutWindowForShutdown();
                DisposeTrayIcon();
                Log.Information("ScourgifyMini Exited");
                ReleaseMutex();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during shutdown");
            }
            finally
            {
                _shutdownCompleted = true;
                _disposed = true;
                Log.CloseAndFlush();

                var application = Application.Current;
                if (application != null && !application.Dispatcher.HasShutdownStarted)
                {
                    application.Shutdown();
                }
            }
        }

        private void DisableTrayForShutdown()
        {
            if (autoStartItem != null)
                autoStartItem.Enabled = false;

            if (noTraceModeItem != null)
                noTraceModeItem.Enabled = false;

            if (languageMenu != null)
                languageMenu.Enabled = false;

            if (aboutItem != null)
                aboutItem.Enabled = false;

            if (exitItem != null)
                exitItem.Enabled = false;

            if (trayIcon != null)
                trayIcon.Visible = false;
        }

        private void ExitNoTraceModeForShutdown()
        {
            _noTraceModeSemaphore.Wait();
            try
            {
                try
                {
                    ExitNoTraceModeUnsafe();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to exit no-trace mode during shutdown");
                }
            }
            finally
            {
                _noTraceModeSemaphore.Release();
            }
        }

        private void DisposeQuickAccessManager()
        {
            if (_quickAccessManager != null)
            {
                _quickAccessManager.Dispose();
                _quickAccessManager = null;
            }
        }

        private void CloseAboutWindowForShutdown()
        {
            if (aboutWindow == null)
                return;

            aboutWindow.Closed -= OnAboutWindowClosed;
            aboutWindow.Close();
            aboutWindow = null;
        }

        private void DisposeTrayIcon()
        {
            if (trayIcon == null)
                return;

            var contextMenu = trayIcon.ContextMenuStrip;
            trayIcon.ContextMenuStrip = null;
            trayIcon.Dispose();
            trayIcon = null;

            if (contextMenu != null)
                contextMenu.Dispose();

            autoStartItem = null;
            noTraceModeItem = null;
            languageMenu = null;
            aboutItem = null;
            exitItem = null;
            languageItems.Clear();
        }

        private void ReleaseMutex()
        {
            if (_mutex == null)
                return;

            try
            {
                if (_ownsMutex)
                {
                    _mutex.ReleaseMutex();
                    _ownsMutex = false;
                }
            }
            catch (ApplicationException ex)
            {
                Log.Warning(ex, "Failed to release single-instance mutex");
            }
            finally
            {
                _mutex.Dispose();
                _mutex = null;
            }
        }

        private bool TryUpdateAutoStart(bool enable, out Exception error)
        {
            try
            {
                UpdateAutoStart(enable);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private void UpdateAutoStart(bool enable)
        {
            string appName = "ScourgifyMini";
            string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
            {
                if (key == null)
                    throw new InvalidOperationException("Unable to open the current user Run registry key.");

                if (enable)
                {
                    key.SetValue(appName, appPath);
                }
                else
                {
                    key.DeleteValue(appName, false);
                }
            }
        }
    }
}
