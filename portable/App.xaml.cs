using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace QuickCal
{
    /// <summary>
    /// Replaces the UWP App class.
    /// - Only one QuickCal runs at a time. Starting it again just shows the running window.
    /// - "--tray" (used by autostart) starts QuickCal hidden, with only the week icon in the notification area.
    /// - Closing the window hides it; "Exit" in the tray menu quits for real.
    /// </summary>
    public partial class App : Application
    {
        public const string TrayArgument = "--tray";

        private const string MutexName = @"Local\QuickCalPortable_SingleInstance_7F3A2C1E";
        private const string ShowEventName = @"Local\QuickCalPortable_Show_7F3A2C1E";

        private Mutex singleInstanceMutex;
        private bool ownsMutex;
        private EventWaitHandle showEvent;
        private RegisteredWaitHandle showWaitRegistration;
        private TrayIconManager trayIcon;
        private MainWindow mainWindow;

        public static new App Current => (App)Application.Current;
        public SettingsManager Settings { get; private set; }
        public bool IsExiting { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Single instance: if QuickCal is already running, ask it to show itself and quit.
            singleInstanceMutex = new Mutex(true, MutexName, out ownsMutex);
            if (!ownsMutex)
            {
                try
                {
                    using (var existing = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        existing.Set();
                    }
                }
                catch (Exception)
                {
                    // The other instance is shutting down; nothing to do.
                }
                Shutdown();
                return;
            }

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            showWaitRegistration = ThreadPool.RegisterWaitForSingleObject(
                showEvent,
                (state, timedOut) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            Settings = new SettingsManager();
            Settings.Load();

            // A portable app can be moved: keep the autostart entry pointing at this .exe.
            AutostartManager.RefreshPathIfEnabled();

            mainWindow = new MainWindow(Settings);

            trayIcon = new TrayIconManager(Settings, ShowMainWindow, ExitApplication);
            trayIcon.DateChanged += (s, args) => mainWindow.RefreshCalendar();

            bool startInTray = e.Args.Any(a => string.Equals(a, TrayArgument, StringComparison.OrdinalIgnoreCase));
            if (!Settings.Values.WelcomeShown)
            {
                // First time QuickCal runs here: show the "Getting started" page once
                ShowMainWindow();
                mainWindow.ShowWelcomePage();
                Settings.Values.WelcomeShown = true;
                Settings.Save();
            }
            else if (!startInTray)
            {
                ShowMainWindow();
            }
        }

        public void ShowMainWindow()
        {
            if (mainWindow == null || IsExiting) return;

            mainWindow.ResetToToday();
            if (!mainWindow.IsVisible) mainWindow.Show();
            if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }

        /// <summary>Called by the main window after it has been hidden (closed with X).</summary>
        public void OnMainWindowHidden()
        {
            if (!Settings.Values.TrayHintShown)
            {
                trayIcon?.ShowHint(Settings.TrayHiddenHint());
                Settings.Values.TrayHintShown = true;
                Settings.Save();
            }
        }

        /// <summary>Called after the settings page is saved (language may have changed).</summary>
        public void OnSettingsChanged()
        {
            trayIcon?.Refresh(force: true);
        }

        public void ExitApplication()
        {
            IsExiting = true;
            mainWindow?.SaveWindowPlacement();
            Settings?.Save();
            mainWindow?.Close();
            Shutdown();
        }

        /// <summary>Windows is logging off or shutting down: quit without blocking it.</summary>
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            IsExiting = true;
            mainWindow?.SaveWindowPlacement();
            Settings?.Save();
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            trayIcon?.Dispose();
            showWaitRegistration?.Unregister(null);
            showEvent?.Dispose();
            if (singleInstanceMutex != null)
            {
                if (ownsMutex)
                {
                    try { singleInstanceMutex.ReleaseMutex(); } catch (Exception) { }
                }
                singleInstanceMutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
