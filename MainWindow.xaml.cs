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
        private const string PersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string SystemUsesLightThemeRegistryValue = "SystemUsesLightTheme";
        private const string LightTrayIconResourcePath = "Assets/icons/light/icon.ico";
        private const string DarkTrayIconResourcePath = "Assets/icons/dark/icon.ico";

        private NotifyIcon trayIcon;
        private Config config;
        private AboutWindow aboutWindow;

        private ToolStripMenuItem autoStartItem;
        private ToolStripMenuItem languageMenu;
        private ToolStripMenuItem noTraceModeItem;
        private ToolStripMenuItem aboutItem;
#if DEBUG
        private ToolStripSeparator testMenuSeparator;
        private ToolStripMenuItem testMenu;
        private ToolStripMenuItem testIncognitoModeStartupWarningItem;
#endif
        private ToolStripMenuItem exitItem;
        private readonly Dictionary<string, ToolStripMenuItem> languageItems = new Dictionary<string, ToolStripMenuItem>();

        private QuickAccessManager _quickAccessManager;
        private QuickAccessLockSession _quickAccessLockSession;

        private string _logPath;
        private readonly object _shutdownLock = new object();
        private readonly SemaphoreSlim _noTraceModeSemaphore = new SemaphoreSlim(1, 1);
        private bool _ownsMutex = false;
        private bool _shutdownStarted = false;
        private bool _shutdownCompleted = false;
        private bool _disposed = false;
        private bool _trayThemeEventsSubscribed = false;

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
            // Keep logs next to the executable intentionally for portable deployments.
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
                "Startup context: Version={Version}, ExecutablePath={ExecutablePath}, ConfigPath={ConfigPath}, LogPath={LogPath}, Language={Language}, AutoStart={AutoStart}, IncognitoMode={IncognitoMode}, CleanupNewRecentLinksOnUnlock={CleanupNewRecentLinksOnUnlock}",
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
                Visible = false
            };
            UpdateTrayIconForSystemTheme();

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

#if DEBUG
            testMenuSeparator = new ToolStripSeparator();
            testMenu = new ToolStripMenuItem(Properties.Resources.TestMenu);
            testIncognitoModeStartupWarningItem = new ToolStripMenuItem(
                Properties.Resources.TestIncognitoModeStartupWarning,
                null, OnTestIncognitoModeStartupWarningClick);
            testMenu.DropDownItems.Add(testIncognitoModeStartupWarningItem);
#endif

            exitItem = new ToolStripMenuItem(
                Properties.Resources.Exit,
                null, OnExitClick);

            contextMenu.Items.AddRange(new ToolStripItem[]
            {
                autoStartItem,
                noTraceModeItem,
                new ToolStripSeparator(),
                languageMenu,
                aboutItem
            });

#if DEBUG
            contextMenu.Items.AddRange(new ToolStripItem[]
            {
                testMenuSeparator,
                testMenu
            });
#endif

            contextMenu.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripSeparator(),
                exitItem
            });

            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.Visible = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _trayThemeEventsSubscribed = true;
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.Color &&
                e.Category != UserPreferenceCategory.General &&
                e.Category != UserPreferenceCategory.VisualStyle)
                return;

            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_shutdownStarted && trayIcon != null)
                    UpdateTrayIconForSystemTheme();
            }));
        }

        private void UpdateTrayIconForSystemTheme()
        {
            if (trayIcon == null)
                return;

            var newIcon = LoadTrayIconForSystemTheme();
            var previousIcon = trayIcon.Icon;
            trayIcon.Icon = newIcon;

            if (previousIcon != null)
                previousIcon.Dispose();
        }

        private System.Drawing.Icon LoadTrayIconForSystemTheme()
        {
            string resourcePath = IsSystemLightTheme()
                ? LightTrayIconResourcePath
                : DarkTrayIconResourcePath;
            return LoadIconResource(resourcePath);
        }

        private static System.Drawing.Icon LoadIconResource(string resourcePath)
        {
            var resourceInfo = Application.GetResourceStream(
                new Uri("pack://application:,,,/" + resourcePath, UriKind.Absolute));
            if (resourceInfo == null || resourceInfo.Stream == null)
                throw new FileNotFoundException("Unable to load tray icon resource.", resourcePath);

            using (resourceInfo.Stream)
            using (var icon = new System.Drawing.Icon(resourceInfo.Stream))
            {
                return (System.Drawing.Icon)icon.Clone();
            }
        }

        private static bool IsSystemLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath))
                {
                    object value = key == null ? null : key.GetValue(SystemUsesLightThemeRegistryValue);
                    if (value is int intValue)
                        return intValue != 0;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to read Windows system theme; falling back to light tray icon");
            }

            return true;
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

#if DEBUG
            if (testMenu != null)
                testMenu.Text = Properties.Resources.TestMenu;

            if (testIncognitoModeStartupWarningItem != null)
                testIncognitoModeStartupWarningItem.Text = Properties.Resources.TestIncognitoModeStartupWarning;
#endif

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
                "Incognito mode change requested: RequestedIncognitoMode={RequestedIncognitoMode}, PreviousIncognitoMode={PreviousIncognitoMode}",
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

                    ShowPartialProtectionWarningIfNeeded();
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
                Log.Information("Incognito mode config changed: IncognitoMode={IncognitoMode}", config.NoTraceMode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to change incognito mode");
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

            Log.Information("Starting incognito mode from saved config");
            try
            {
                await EnterNoTraceModeAsync();
                if (!_shutdownStarted)
                    ShowPartialProtectionWarningIfNeeded();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start incognito mode from saved config");
                Log.Warning("Incognito mode startup failed; saved preference remains enabled");

                if (noTraceModeItem != null)
                {
                    noTraceModeItem.Checked = true;
                }

                ShowIncognitoModeStartupFailedWarning(ex.Message);
            }
            finally
            {
                if (noTraceModeItem != null && !_shutdownStarted)
                    noTraceModeItem.Enabled = true;
            }
        }

        private async Task EnterNoTraceModeAsync()
        {
            await _noTraceModeSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() => EnterNoTraceModeUnsafe()).ConfigureAwait(false);
            }
            finally
            {
                _noTraceModeSemaphore.Release();
            }
        }

        private async Task ExitNoTraceModeAsync()
        {
            await _noTraceModeSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() => ExitNoTraceModeUnsafe()).ConfigureAwait(false);
            }
            finally
            {
                _noTraceModeSemaphore.Release();
            }
        }

        private void EnterNoTraceModeUnsafe()
        {
            if (_quickAccessLockSession != null)
                return;

            if (_quickAccessManager == null)
                throw new ObjectDisposedException(nameof(_quickAccessManager));

            _quickAccessLockSession = LockQuickAccessWithFallback();
            Log.Information(
                "Incognito mode started: LockedTargets={LockedTargets}, MissingTargets={MissingTargets}, IsPartial={IsPartial}, LockedFileCount={LockedFileCount}, InitialShortcutCount={InitialShortcutCount}",
                _quickAccessLockSession.LockedTargetsText,
                _quickAccessLockSession.MissingTargetsText,
                _quickAccessLockSession.IsPartial,
                _quickAccessLockSession.LockedFileCount,
                _quickAccessLockSession.InitialShortcutCount);
        }

        private void ExitNoTraceModeUnsafe()
        {
            var quickAccessLockSession = _quickAccessLockSession;
            if (quickAccessLockSession == null)
                return;

            try
            {
                UnlockQuickAccessSession(quickAccessLockSession);
            }
            finally
            {
                _quickAccessLockSession = null;
            }
        }

        private QuickAccessLockSession LockQuickAccessWithFallback()
        {
            try
            {
                var quickAccessLock = _quickAccessManager.LockQuickAccess();
                return QuickAccessLockSession.CreateComplete(quickAccessLock);
            }
            catch (FileNotFoundException ex)
            {
                Log.Warning(ex, "Full Quick Access lock failed because a backing file is missing; trying partial lock fallback");
                return LockQuickAccessPartially(ex);
            }
        }

        private QuickAccessLockSession LockQuickAccessPartially(FileNotFoundException fullLockError)
        {
            var locks = new List<QuickAccessLock>();
            var lockedTargets = new List<QuickAccessLockTarget>();
            var missingTargets = new List<QuickAccessLockTarget>();
            var failures = new List<Exception>();

            TryLockQuickAccessTarget(
                QuickAccessLockTarget.RecentFiles,
                () => _quickAccessManager.LockRecentFiles(),
                locks,
                lockedTargets,
                missingTargets,
                failures);

            TryLockQuickAccessTarget(
                QuickAccessLockTarget.FrequentFolders,
                () => _quickAccessManager.LockFrequentFolders(),
                locks,
                lockedTargets,
                missingTargets,
                failures);

            if (failures.Count > 0)
            {
                DisposeLocks(locks);
                throw new AggregateException("Failed to start incognito mode because one or more Quick Access backing files could not be locked.", failures);
            }

            if (locks.Count == 0)
                throw fullLockError;

            if (missingTargets.Count > 0)
            {
                Log.Warning(
                    "Incognito mode partially started: LockedTargets={LockedTargets}, MissingTargets={MissingTargets}",
                    FormatTargetsForLog(lockedTargets),
                    FormatTargetsForLog(missingTargets));
            }
            else
            {
                Log.Information(
                    "Incognito mode started through fallback lock path: LockedTargets={LockedTargets}",
                    FormatTargetsForLog(lockedTargets));
            }

            return QuickAccessLockSession.CreateFallback(locks, lockedTargets, missingTargets);
        }

        private void TryLockQuickAccessTarget(
            QuickAccessLockTarget target,
            Func<QuickAccessLock> lockFactory,
            List<QuickAccessLock> locks,
            List<QuickAccessLockTarget> lockedTargets,
            List<QuickAccessLockTarget> missingTargets,
            List<Exception> failures)
        {
            try
            {
                locks.Add(lockFactory());
                lockedTargets.Add(target);
            }
            catch (FileNotFoundException ex)
            {
                Log.Warning(ex, "Quick Access backing file missing during partial incognito lock: Target={Target}", target);
                missingTargets.Add(target);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to lock Quick Access target during partial incognito lock: Target={Target}", target);
                failures.Add(ex);
            }
        }

        private void UnlockQuickAccessSession(QuickAccessLockSession quickAccessLockSession)
        {
            bool cleanupNewRecentLinks = config == null || config.CleanupNewRecentLinksOnUnlock;
            bool cleanupAttempted = false;
            var failures = new List<Exception>();

            foreach (var quickAccessLock in quickAccessLockSession.Locks)
            {
                bool cleanupForThisLock = cleanupNewRecentLinks && !cleanupAttempted;
                if (cleanupForThisLock)
                    cleanupAttempted = true;

                try
                {
                    var report = quickAccessLock.Unlock(new QuickAccessUnlockOptions
                    {
                        CleanupNewRecentLinks = cleanupForThisLock
                    });

                    Log.Information(
                        "Incognito mode lock stopped: Target={Target}, CleanupNewRecentLinks={CleanupNewRecentLinks}, CurrentShortcutCount={CurrentShortcutCount}, NewShortcutCount={NewShortcutCount}, DeletedShortcutCount={DeletedShortcutCount}, FailedShortcutDeletionCount={FailedShortcutDeletionCount}",
                        quickAccessLock.Target,
                        cleanupForThisLock,
                        report.CurrentShortcutPaths.Count,
                        report.NewShortcutPaths.Count,
                        report.DeletedShortcutPaths.Count,
                        report.FailedShortcutDeletions.Count);

                    foreach (var failure in report.FailedShortcutDeletions)
                    {
                        Log.Warning(
                            failure.Error,
                            "Failed to delete new Recent shortcut during incognito unlock: {Path}",
                            failure.Path);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    Log.Error(ex, "Failed to unlock Quick Access lock: Target={Target}", quickAccessLock.Target);
                }
            }

            if (failures.Count > 0)
                throw new AggregateException("Failed to unlock one or more Quick Access locks.", failures);
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

#if DEBUG
        private void OnTestIncognitoModeStartupWarningClick(object sender, EventArgs e)
        {
            ShowIncognitoModeStartupFailedWarning(Properties.Resources.TestMenu);
        }
#endif

        private void ShowIncognitoModeStartupFailedWarning(string errorMessage)
        {
            System.Windows.MessageBox.Show(
                string.Format(Properties.Resources.IncognitoModeStartupFailed, errorMessage),
                Properties.Resources.Warning,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void ShowPartialProtectionWarningIfNeeded()
        {
            var quickAccessLockSession = _quickAccessLockSession;
            if (quickAccessLockSession == null || !quickAccessLockSession.IsPartial)
                return;

            System.Windows.MessageBox.Show(
                string.Format(
                    Properties.Resources.IncognitoModePartialProtection,
                    quickAccessLockSession.LockedTargetsText,
                    quickAccessLockSession.MissingTargetsText),
                Properties.Resources.Warning,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

#if DEBUG
            if (testMenuSeparator != null)
                testMenuSeparator.Enabled = false;

            if (testMenu != null)
                testMenu.Enabled = false;
#endif

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
                    Log.Error(ex, "Failed to exit incognito mode during shutdown");
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

            if (_trayThemeEventsSubscribed)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _trayThemeEventsSubscribed = false;
            }

            var contextMenu = trayIcon.ContextMenuStrip;
            var icon = trayIcon.Icon;
            trayIcon.ContextMenuStrip = null;
            trayIcon.Icon = null;
            trayIcon.Dispose();
            trayIcon = null;

            if (icon != null)
                icon.Dispose();

            if (contextMenu != null)
                contextMenu.Dispose();

            autoStartItem = null;
            noTraceModeItem = null;
            languageMenu = null;
            aboutItem = null;
#if DEBUG
            testMenuSeparator = null;
            testMenu = null;
            testIncognitoModeStartupWarningItem = null;
#endif
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
                    key.SetValue(appName, "\"" + appPath + "\"");
                }
                else
                {
                    key.DeleteValue(appName, false);
                }
            }
        }

        private static void DisposeLocks(IEnumerable<QuickAccessLock> locks)
        {
            foreach (var quickAccessLock in locks)
            {
                try
                {
                    quickAccessLock.Dispose();
                }
                catch
                {
                    // Best effort cleanup while preserving the original lock failure.
                }
            }
        }

        private static string FormatTargetsForLog(IEnumerable<QuickAccessLockTarget> targets)
        {
            return string.Join(", ", FormatTargetNames(targets));
        }

        private static IReadOnlyList<string> FormatTargetNames(IEnumerable<QuickAccessLockTarget> targets)
        {
            var targetNames = new List<string>();
            foreach (var target in targets)
            {
                targetNames.Add(FormatTargetName(target));
            }

            return targetNames.AsReadOnly();
        }

        private static string FormatTargetName(QuickAccessLockTarget target)
        {
            switch (target)
            {
                case QuickAccessLockTarget.All:
                    return "Recent Files + Frequent Folders";
                case QuickAccessLockTarget.RecentFiles:
                    return "Recent Files";
                case QuickAccessLockTarget.FrequentFolders:
                    return "Frequent Folders";
                default:
                    return target.ToString();
            }
        }

        private sealed class QuickAccessLockSession
        {
            private QuickAccessLockSession(
                IEnumerable<QuickAccessLock> locks,
                IEnumerable<QuickAccessLockTarget> lockedTargets,
                IEnumerable<QuickAccessLockTarget> missingTargets,
                bool isPartial)
            {
                Locks = new List<QuickAccessLock>(locks).AsReadOnly();
                LockedTargetsText = FormatTargetsForLog(lockedTargets);
                MissingTargetsText = FormatTargetsForLog(missingTargets);
                IsPartial = isPartial;

                int lockedFileCount = 0;
                var initialShortcutPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var quickAccessLock in Locks)
                {
                    lockedFileCount += quickAccessLock.LockedFileCount;
                    foreach (var shortcutPath in quickAccessLock.InitialShortcutPaths)
                    {
                        initialShortcutPaths.Add(shortcutPath);
                    }
                }

                LockedFileCount = lockedFileCount;
                InitialShortcutCount = initialShortcutPaths.Count;
            }

            public IReadOnlyList<QuickAccessLock> Locks { get; }

            public bool IsPartial { get; }

            public int LockedFileCount { get; }

            public int InitialShortcutCount { get; }

            public string LockedTargetsText { get; }

            public string MissingTargetsText { get; }

            public static QuickAccessLockSession CreateComplete(QuickAccessLock quickAccessLock)
            {
                return new QuickAccessLockSession(
                    new[] { quickAccessLock },
                    new[] { QuickAccessLockTarget.All },
                    new QuickAccessLockTarget[0],
                    false);
            }

            public static QuickAccessLockSession CreateFallback(
                IEnumerable<QuickAccessLock> locks,
                IEnumerable<QuickAccessLockTarget> lockedTargets,
                IEnumerable<QuickAccessLockTarget> missingTargets)
            {
                var missingTargetList = new List<QuickAccessLockTarget>(missingTargets);
                return new QuickAccessLockSession(locks, lockedTargets, missingTargetList, missingTargetList.Count > 0);
            }
        }
    }
}
