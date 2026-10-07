# Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

Only `QuickCal.exe` (QuickCal Portable), built from the source code in this repository by GitHub Actions ([`.github/workflows/release.yml`](.github/workflows/release.yml)) and published under [Releases](../../releases). Nothing built on a personal computer is signed.

## Team roles

QuickCal is maintained by one person, who holds all roles:

| Role | Member |
|---|---|
| Author (writes and changes the code) | [Magnus Petersen (@dooniem)](https://github.com/dooniem) |
| Reviewer (reviews changes from others) | [Magnus Petersen (@dooniem)](https://github.com/dooniem) |
| Approver (approves each signing request) | [Magnus Petersen (@dooniem)](https://github.com/dooniem) |

Multi-factor authentication is enabled for GitHub and SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

QuickCal Portable makes no network connections at all. Its settings are stored locally in `QuickCal.settings.json` next to `QuickCal.exe`, or in `%AppData%\QuickCal` if that folder is read-only.
