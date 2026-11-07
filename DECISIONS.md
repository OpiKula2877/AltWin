# Decisions and status

These are the choices made while building the spec ("Build spec: Alt Account Switcher for Windows"), where they differ from or add to the spec, and anything not built or not verified.

## Choices

1. **.NET 10 (LTS) instead of .NET 8.** Support for .NET 8 ends on 10 Nov 2026, and the build PC has only the .NET 10 SDK. Target `net10.0-windows`, `win-x64`, self-contained single file (about 72 MB).
2. **One exe for everything.** The elevated helper is the same `AltAccounts.exe`, started with `--elevated <request> <result>` and the `runas` verb. Each admin action shows UAC. Cancelling UAC means the helper never starts, so nothing changes.
3. **Passwords never cross a process boundary.** The spec suggested `New-LocalUser`. Passing a password to PowerShell means putting it on a command line or in a pipe, so instead the *Add account* dialog runs **inside** the elevated helper. It calls `System.DirectoryServices.AccountManagement` (documented) and clears the box afterwards. Requests between the normal app and the helper never contain a password.
4. **Groups are found by SID** (`S-1-5-32-544` Administrators, `S-1-5-32-545` Users), not by name. Group names change with the Windows language (Czech: "Administrátoři").
5. **Left Alt only by default.** Right Alt is AltGr on the Czech layout (and Polish and German), where it types `@ # { } [ ]`. Right Alt can be turned on in General. Even then, AltGr never triggers, because Windows sends a fake Ctrl with it. Injected (synthetic) key events are ignored.
6. **The hook runs on its own thread** with its own message loop, so a busy UI thread can never make Windows drop it.
7. **Autostart uses one `HKLM\…\Run` value** written by the installer, instead of a per-user HKCU entry on first launch. The uninstaller then removes one value and never has to edit other users' registry hives for it. An admin can turn the app off per account (*Don't run Alt Account Switcher in this account*). Settings stays reachable with `--settings`.
8. **Sign-in hours (idea 7) use `net user <name> /times:`.** Windows enforces this for new sign-ins, and it works on Home, which is stronger than "[App] only". The app also locks a session that is still open outside the hours, after a one-minute warning. Whole hours only, and the latest end is 23:00, because that is what `net user` accepts reliably.
9. **Daily limit (idea 6)** counts a minute only when there was input in the last 5 minutes and the session is unlocked. It warns 5 minutes before the limit, then locks after a one-minute warning. Usage lives in the user's own `user.json`, so it is [App] only, as the spec says.
10. **Removing a share never removes a permission the app did not add.** Before granting, the helper checks whether the account already has an explicit allow entry on the folder. If it does, the grant is marked `PreExisting`, and removing the share leaves it alone (the UI says so). Changing the level of an app-made grant removes it and grants the new level.
11. **Quota detection** runs `fsutil quota query C:`. If that fails, it runs `fsutil quota track C:` and queries again. This happens only when an admin clicks *Detect and enable quota support* or first saves a disk limit. If it works, the mode is **Enforced**: setting a limit runs `fsutil quota enforce` and `modify`, with the warning at 80%. Removing a limit sets it to "no limit". If any `fsutil` call fails, the mode drops to **Soft**, and both About and Storage show the soft mode.
12. **Audit log ACL.** `audit.log` lets Users *append* (`FILE_APPEND_DATA`) but not rewrite, so switches by standard users can be logged without letting them edit history. When the shared log does not exist yet, entries go to `%LocalAppData%\AltAccounts\audit.log`. The viewer merges both.
13. **Removing an account also removes its share grants** while its SID still resolves, plus its per-account options, so no stale entries are left behind. The *Sign out first* step is required when the account is signed in.
14. **Notifications (idea 11)** use the tray `NotifyIcon` balloon, which Windows 11 shows as a toast. No extra toast library.
15. **Themes (idea 9)** use WPF's built-in Fluent theme for Settings and dialogs (light, dark, or same as Windows). The Fluent resources load only when a Settings or helper window opens, which roughly halves the tray app's memory. The popup has its own blue styles, with a darker blue in dark mode.
16. **Update check (idea 20)** runs `winget list --id AltAccounts.AltAccounts --upgrade-available`, read-only. It shows the `winget upgrade` command instead of running it.
17. **Export/import (idea 12)** matches accounts **by username**, because SIDs differ between PCs. Folders that don't exist on the new PC are skipped. Every import goes through the same checks as manual changes.
18. **Downloads folder (idea 18)** uses `SHSetKnownFolderPath(FOLDERID_Downloads)` for the current user only. Existing files are not moved.
19. **Packaging: Inno Setup.** The uninstaller has to run cleanup (`--uninstall-cleanup`), which a plain portable winget package cannot do. The winget manifest is of type `inno`. winget needs an https `InstallerUrl`, so the URL is a placeholder until the setup file is uploaded. `build.ps1 -Installer -InstallerUrl …` fills in the URL and the SHA-256.
20. **Default share suggestion is `C:\Shared`.** A folder *inside* a profile (for example `C:\Users\Alice\Games`) is allowed with a warning, but a whole profile is refused. `C:\ProgramData` is refused as well, in addition to the folders the spec lists.

## Built

- Every core feature in spec section 4.
- All 20 optional ideas in section 5.
- The Settings tabs from section 6.
- Install and removal behaviour from section 7, with autostart handled as in choice 7.

## Not built, or not verified on this PC

| Item | Status |
|---|---|
| Pro direct switch (`WTSDisconnectSession`) | Built, but **not tested on Pro**: this PC runs Home, so the button stays hidden. |
| Windows 10 rounded-corner fallback | Built (transparent window with a clipped `Border`), but **not tested on Windows 10**: this PC runs Windows 11. |
| `fsutil quota` on Windows Home | **Not verified yet**: it needs admin. The app checks at runtime and falls back to soft mode, and About shows the result. |
| Brother's account end to end (spec §8 #3, #6, #9) | Needs a second real account. Create it through Settings and follow the README checklist. |
| Alt tap with real key presses | Covered by unit tests of the state machine. Injected keys are ignored by design, so a live check needs a person at the keyboard. |
| Alt+mouse-click cancelling the tap | **Not built.** Holding Alt, clicking, and releasing Alt still counts as a tap. A mouse hook seemed too costly for this rare case. |
| Suppressing the foreground app's menu bar on an Alt tap | **Not built.** Some classic apps briefly highlight their menu bar before the popup takes focus. Blocking that would mean injecting fake keys. |
| Public winget-pkgs submission | **Not done** (the spec marks it optional). |
| Installer built and run | **Not done on this PC.** Inno Setup is not installed. Run `winget install JRSoftware.InnoSetup`, then `./build.ps1 -Installer`. |
