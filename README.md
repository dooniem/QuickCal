# QuickCal

A quick ISO week calendar for Windows with Norwegian holidays and school vacations. QuickCal shows three months at a glance, the whole year when maximized, and keeps the current week number right next to the clock.

QuickCal comes in two versions:

| | **QuickCal Portable** (Windows) | **QuickCal Web** (browser) |
|---|---|---|
| Get it | Download `QuickCal.exe` from [Releases](../../releases) | Open **https://YOUR-USERNAME.github.io/quickcal/** |
| Installation | None. One file, runs from any folder or USB stick | None. Works in a tab, and can be [installed as an app](#quickcal-web-browser) from Edge or Chrome |
| Admin rights | Not needed | Not needed |
| Week number always visible | Icon with the week number in the notification area, by the clock | Badge on the QuickCal taskbar icon (installed app) |
| Always on top | Yes (compact mode) | No (browsers don't allow it) |
| Start automatically | One click in the app | In the browser's app settings (explained in the app) |

## Features

- **ISO 8601 week numbers** on every row, including week 53.
- **Three months** in the normal window, with the current month in the middle.
- **The whole year** (4 × 3 months) when the window is maximized. Change year with the arrows, the ← / → keys, and press Space to jump back to the current year.
- **Norwegian holidays**: New Year's Day, Easter, Labour Day, Constitution Day (17 May), Ascension, Whitsun and Christmas, with names shown as tooltips. Easter can optionally be shown as a full week off.
- **Summer vacation** (1 to 3 weeks, configurable) marked in the calendar.
- **Language**: English, Norwegian, or the Windows/browser language. Month and day names come from the operating system, so every language works.
- **Always on top**: shrinks the window to a compact two-month view that stays above other windows (Portable only).
- **Getting started page** on first run, explaining how to pin the app, turn on autostart and keep the week number visible.

## QuickCal Portable (Windows)

1. Download `QuickCal.exe` from the latest [release](../../releases).
2. Put it in any folder you like and double-click it. No installation and no .NET runtime needed.
3. Follow the *Getting started* page to turn on autostart and keep the week icon visible by the clock.

Closing the window with **X** hides it to the notification area, so the week number stays visible. Right-click the week icon and choose **Exit** to quit.

Settings are stored in `QuickCal.settings.json` next to `QuickCal.exe`. If that folder is read-only, `%AppData%\QuickCal` is used instead. Autostart uses the current user's `Run` registry key, so it needs no admin rights and can be turned off in Task Manager under *Startup apps*.

> **Windows SmartScreen / Smart App Control:** `QuickCal.exe` is not code-signed, so Windows may warn you the first time you run it. Choose *More info → Run anyway*. On managed work PCs, your IT department's policies decide whether unsigned apps may run.

### Uninstall

QuickCal does not install anything. To remove it:

1. If you turned on autostart, open **Settings** and click **Disable autostart** (or turn QuickCal off in Task Manager under *Startup apps*).
2. Right-click the week icon by the clock and choose **Exit**.
3. Delete `QuickCal.exe` and `QuickCal.settings.json` (and `%AppData%\QuickCal`, if it exists).

### Privacy

QuickCal Portable makes no network connections and collects no data. See the [code signing policy](CODE_SIGNING_POLICY.md).

### Build from source

Requirements: Visual Studio 2026 with the **.NET desktop development** workload (includes the .NET 10 SDK).

1. Open `portable/QuickCal.csproj` in Visual Studio and press **F5** to build and run.
2. To create the single portable file, double-click `portable/publish.cmd`, or right-click the project → **Publish** → profile **Portable**.
3. The finished file is `portable/publish/QuickCal.exe`. It is self-contained and is the only file you need to distribute.

Releases are built automatically by GitHub Actions ([`.github/workflows/release.yml`](.github/workflows/release.yml)): publishing a release on GitHub builds `QuickCal.exe` from the code in this repository and attaches it to the release.

Command line alternative:

```
dotnet publish portable/QuickCal.csproj -c Release -p:PublishProfile=Portable
```

## QuickCal Web (browser)

Open **https://YOUR-USERNAME.github.io/quickcal/** and the calendar works right away, as an ordinary web page in a tab.

Installing it as an app is optional, but worth it: QuickCal then gets its own window without browser toolbars, works offline, can be pinned to the taskbar, can start automatically when you sign in, and shows the current week number as a badge on its taskbar icon.

### Install in Microsoft Edge

1. Open the link in Edge.
2. Look at the **right end of the address bar** for the install icon — a small monitor with an arrow, sometimes with the text *App available*. Click it, then click **Install**.
3. No icon there? Open the **⋯** menu (top right, or `Alt`+`F`) → **Apps** → **Install this site as an app** → **Install**.
4. QuickCal opens in its own window, and Edge offers to pin it. Say yes, or right-click the QuickCal icon on the taskbar and choose **Pin to taskbar**.
5. To start it automatically: in the QuickCal window, click **⋯** (top right) → **App settings**, and turn on **Auto-start on device login**. The same settings page is at `edge://apps`.

### Install in Google Chrome

1. Open the link in Chrome.
2. Look at the **right end of the address bar** for the install icon — a small monitor with a down arrow. Click it, then click **Install**.
3. No icon there? Open the **⋮** menu (top right) and look for **Install QuickCal…**. In some Chrome versions it sits under **Cast, save, and share**.
4. QuickCal opens in its own window. Right-click its taskbar icon and choose **Pin to taskbar**.
5. To start it automatically: go to `chrome://apps`, right-click **QuickCal** and tick **Start app when you sign in**.

The first time QuickCal opens as an installed app, it shows these autostart steps with a button that copies `chrome://apps` or `edge://apps` for you.

The names of these menu items change between browser versions and languages. QuickCal also explains the steps inside the app, in English or Norwegian, on the **Getting started** page shown on the first visit and under **Settings → Install as app**.

### If the install option is missing

- The page must be opened over **https://**, as it is on GitHub Pages. Opening `index.html` as a local file shows the calendar, but cannot be installed.
- Press `Ctrl`+`F5` once to reload the page fully, then look again.
- Check `edge://apps` or `chrome://apps` — QuickCal may already be installed, in which case the install icon is gone by design.
- Installing does not work in InPrivate / Incognito windows.
- On a managed work PC, the IT department can block installation of web apps by policy. Edge then shows nothing in the address bar and no **Apps** entry in the menu; `edge://policy` lists the policies in force. The calendar still works as a normal web page, only the taskbar badge and the separate window are lost.

### Updating the web version

The web files live in the [`docs`](docs) folder, which GitHub Pages serves. When you change them, bump `VERSION` at the top of [`docs/sw.js`](docs/sw.js) (for example to `quickcal-v2.0.1`). Visitors then get the new version the next time they open QuickCal.

Norwegian step-by-step instructions you can forward to colleagues: [INSTALLASJON.md](INSTALLASJON.md).

## Repository layout

```
quickcal/
├── .github/          GitHub Actions: builds QuickCal.exe for each release
├── portable/         QuickCal Portable: WPF / .NET 10 Visual Studio project
├── docs/             QuickCal Web: static site served by GitHub Pages
├── CODE_SIGNING_POLICY.md  Code signing policy and privacy policy
├── INSTALLASJON.md   Install guide for the web app, in Norwegian
└── LICENSE           MIT License
```

## History

QuickCal started as a UWP app on the Microsoft Store. Version 2.0 is a rewrite as a portable WPF app (no installation, no admin rights) plus a web version, with these fixes over the Store version:

- Summer vacation was shown one week too early in some years (e.g. 2027 and 2028).
- Week 53 had no week icon.
- Easter was wrong in some years (e.g. 2049 and 2076).
- With the system language selected, month names were shifted by one month.
- The app crashed when changing month on the 29th, 30th or 31st.

## Author

Magnus Petersen

## License

QuickCal is free and open source under the [MIT License](LICENSE). You may use, copy, change and share it, as long as the copyright notice is kept.
