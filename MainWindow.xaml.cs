using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using Wincent;
using Serilog;
using Application = System.Windows.Application;
using NotifyIcon = System.Windows.Forms.NotifyIcon;


namespace CleanRecentMini
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        private static Mutex _mutex = null;
        private const string MutexName = "CleanRecentMini_SingleInstance_Mutex";
        
        private NotifyIcon trayIcon;
        private Config config;

        private ToolStripMenuItem languageMenu;
        private ToolStripMenuItem noTraceModeItem;
        private Dictionary<string, ToolStripMenuItem> languageItems = new Dictionary<string, ToolStripMenuItem>();

        private QuickAccessManager _quickAccessManager;
        private QuickAccessLock _quickAccessLock;

        private bool _disposed = false;

        public MainWindow()
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                Log.Warning("Another instance is already running");
                System.Windows.MessageBox.Show(
                    Properties.Resources.AlreadyRunning,
                    Properties.Resources.Warning,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Application.Current.Shutdown();
                return;
            }

            InitializeLogger();
            _quickAccessManager = new QuickAccessManager(new QuickAccessManagerOptions
            {
                Timeout = TimeSpan.FromSeconds(10),
                RetryPolicy = RetryPolicy.Standard
            });
            
            Task.Run(async () =>
            {
                try 
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        config = Config.Load();
                        UpdateAutoStart(config.AutoStart);

                        InitializeLanguage();
                        InitializeComponent();
                        InitializeTrayIcon();
                        
                        if (config.NoTraceMode)
                        {
                            StartNoTraceModeFromStartup();
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during initialization");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.MessageBox.Show(
                            "Application initialization failed",
                            Properties.Resources.Warning,
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        Application.Current.Shutdown();
                    });
                }
            });
        }

        ~MainWindow()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (trayIcon != null)
                    {
                        trayIcon.Visible = false;
                        trayIcon.Dispose();
                    }

                    if (_mutex != null)
                    {
                        _mutex.ReleaseMutex();
                        _mutex.Dispose();
                        _mutex = null;
                    }

                    if (_quickAccessLock != null)
                    {
                        ExitNoTraceMode();
                    }

                    if (_quickAccessManager != null)
                    {
                        _quickAccessManager.Dispose();
                        _quickAccessManager = null;
                    }
                }

                _disposed = true;
            }
        }

        private void InitializeLogger()
        {
            var logPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "logs", "CleanRecentMini.log");

            var logConfig = new LoggerConfiguration()
                .MinimumLevel.Debug();

#if DEBUG
            logConfig = logConfig.WriteTo.Console();
#endif
            logConfig = logConfig.WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: 10,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

            Log.Logger = logConfig.CreateLogger();
            Log.Information("CleanRecentMini Started");
        }

        private void InitializeLanguage()
        {
            var culture = new CultureInfo(config.Language);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            Properties.Resources.Culture = culture;
        }

        private void InitializeTrayIcon()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo(config.Language);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(config.Language);
            Properties.Resources.Culture = new CultureInfo(config.Language);

            trayIcon = new NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();

            var autoStartItem = new ToolStripMenuItem(
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

            var aboutItem = new ToolStripMenuItem(
                Properties.Resources.About,
                null, OnAboutClick);

            var exitItem = new ToolStripMenuItem(
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
            if (menuItem != null && menuItem.Tag is string langCode)
            {
                foreach (var item in languageItems.Values)
                {
                    item.Checked = false;
                }
                menuItem.Checked = true;

                config.Language = langCode;
                Config.Save(config);

                InitializeLanguage();

                RefreshMenuTexts();
            }
        }

        private void RefreshMenuTexts()
        {
            Properties.Resources.Culture = new CultureInfo(config.Language);
            
            var contextMenu = trayIcon.ContextMenuStrip;
            if (contextMenu != null)
            {
                if (contextMenu.Items[0] is ToolStripMenuItem autoStartItem)
                {
                    autoStartItem.Text = Properties.Resources.AutoStart;
                }

                if (contextMenu.Items[1] is ToolStripMenuItem incognitoModeItem)
                    incognitoModeItem.Text = Properties.Resources.IncognitoMode;

                if (contextMenu.Items[3] is ToolStripMenuItem languageMenuItem)
                    languageMenuItem.Text = Properties.Resources.Language;

                if (contextMenu.Items[4] is ToolStripMenuItem aboutItem)
                    aboutItem.Text = Properties.Resources.About;

                if (contextMenu.Items[6] is ToolStripMenuItem exitItem)
                    exitItem.Text = Properties.Resources.Exit;
            }
        }

        private void OnAutoStartClick(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            config.AutoStart = menuItem.Checked;
            UpdateAutoStart(config.AutoStart);
            Config.Save(config);
        }

        private async void OnNoTraceModeClick(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem == null)
                return;

            menuItem.Enabled = false;
            try
            {
                if (menuItem.Checked)
                {
                    await EnterNoTraceModeAsync();
                    config.NoTraceMode = true;
                }
                else
                {
                    await ExitNoTraceModeAsync();
                    config.NoTraceMode = false;
                }

                Config.Save(config);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to change no-trace mode");
                menuItem.Checked = config.NoTraceMode;
                System.Windows.MessageBox.Show(
                    ex.Message,
                    Properties.Resources.Warning,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                menuItem.Enabled = true;
            }
        }

        private async void StartNoTraceModeFromStartup()
        {
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
        }

        private Task EnterNoTraceModeAsync()
        {
            return Task.Run(() => EnterNoTraceMode());
        }

        private Task ExitNoTraceModeAsync()
        {
            return Task.Run(() => ExitNoTraceMode());
        }

        private void EnterNoTraceMode()
        {
            if (_quickAccessLock != null)
                return;

            _quickAccessLock = _quickAccessManager.LockQuickAccess();
            Log.Information(
                "No-trace mode started: Target={Target}, LockedFileCount={LockedFileCount}, InitialShortcutCount={InitialShortcutCount}",
                _quickAccessLock.Target,
                _quickAccessLock.LockedFileCount,
                _quickAccessLock.InitialShortcutPaths.Count);
        }

        private void ExitNoTraceMode()
        {
            var quickAccessLock = _quickAccessLock;
            if (quickAccessLock == null)
                return;

            _quickAccessLock = null;
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

        private void OnAboutClick(object sender, EventArgs e)
        {
            AboutWindow aboutWindow = new AboutWindow();
            aboutWindow.ShowDialog();
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Log.Information("CleanRecentMini Exited");
            Log.CloseAndFlush();

            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }

            if (_quickAccessLock != null)
            {
                ExitNoTraceMode();
            }

            if (_quickAccessManager != null)
            {
                _quickAccessManager.Dispose();
                _quickAccessManager = null;
            }

            System.Windows.Application.Current.Shutdown();
        }

        private void UpdateAutoStart(bool enable)
        {
            string appName = "CleanRecentMini";
            string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
            {
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
