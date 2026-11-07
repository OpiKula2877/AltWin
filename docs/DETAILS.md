# Alt Account Switcher (AltWin): details

A small Windows background app for PCs with several local accounts.

- **Tap Alt** on its own and a blue popup lists the other accounts on the PC.
- **Switch** locks the PC. On the sign-in screen you pick the account and type its password or PIN.
- **Settings** let an admin add, edit and remove accounts, share folders between accounts (for example a game library), and set per-account options.

It works on Windows 11 **Home** and **Pro** and finds out the edition when it runs. It never stores, shows or bypasses a password.

---

## Setup

### Requirements

- Windows 10 1809 or newer (built and tested on Windows 11 Home, build 26300).
- To build: the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`).
- To build the installer: Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

### Build

```powershell
./build.ps1              # runs the tests, then publishes artifacts\publish\AltAccounts.exe (one self-contained file)
./build.ps1 -Installer   # also builds artifacts\AltWin-Setup-1.0.0.exe and fills in the winget manifest hash
```

To publish by hand:

```powershell
dotnet publish src\AltAccounts.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts\publish
```

### Install

**With the installer (recommended).** Run `artifacts\AltWin-Setup-1.0.0.exe` as an admin. It:

- installs to `C:\Program Files\AltAccounts`;
- adds **one** machine-wide autostart entry (`HKLM\...\Run\AltAccounts`), so the app starts in **every** account at sign-in;
- creates `C:\ProgramData\AltAccounts`, which only admins can change;
- adds Start menu entries for the app and for *Alt Account Switcher Settings*.

**With winget.** winget downloads the installer from a URL, so upload the setup file first (for example to a GitHub release). Then run:

```powershell
./build.ps1 -Installer -InstallerUrl https://github.com/OpiKula2877/AltWin/releases/download/v1.0.0/AltWin-Setup-1.0.0.exe
winget settings --enable LocalManifestFiles     # once, from an admin terminal
winget install --manifest winget\manifests\o\OpiKula2877\AltWin\1.0.0
```

**Without installing.** You can run `artifacts\publish\AltAccounts.exe` directly to try it. It won't start with Windows, and the shared settings folder is created the first time an admin saves something in Settings.

### First use

1. After you install, sign out and back in (or start the app from the Start menu). A tray icon appears.
2. Tap **Left Alt** on its own. The popup opens. Alt+Tab, Alt+F4 and other Alt shortcuts keep working.
3. Open **Settings** from the popup or from the tray icon.

### Example: sharing a game library with a brother's account

1. **Settings > Accounts > Add account… (UAC).** Windows asks for admin approval, then a small window asks for the username (for example `brother`), an initial password and the type (**Standard**).
2. **Settings > Shared folders > Share a folder… (UAC).** Pick the library folder (for example `C:\Shared\Games`), the account `brother` and **Read and write**. Read and write lets new downloads go into the folder.
3. In the brother's account, open **Settings > General > My Downloads folder > Use a shared folder…** and pick the shared folder. Browsers and game launchers each have their own download folder setting too. Point their library or download folder at the same shared folder.
4. The brother signs in once, so that his profile and the app's tray icon exist in his account. Tapping Alt there shows your account.

> A shared folder is **not a game licence**. Each account needs its own right to the game. Steam's official option is **Steam Family Sharing** (one player at a time). Sharing one account's login breaks Steam's and Rockstar's terms, and this app never does that or signs in to any game account.

---

## Features

| Area | What it does |
|---|---|
| Alt trigger | A low-level keyboard hook that only watches and never blocks keys. It counts Left Alt pressed and released alone within 600 ms. Right Alt can be added in General. It is off by default because Right Alt is AltGr on Czech, Polish and German keyboards. Any other key in between (Tab, F4, Shift, letters) cancels it. Esc or a second tap closes the popup. |
| Popup | A borderless blue window (`#1E5FD6`) with its own title bar (title, minimize, close). Corners are rounded by DWM on Windows 11; Windows 10 gets a clipped rounded border instead. Each account row shows its picture or initials, display name, username, Admin/Standard, live status and **Switch**. Keys: Up/Down, Enter, 1–9, Esc. |
| Switch | `LockWorkStation`. A one-line explanation appears the first time. **Pro only:** a *Sign-in screen* button disconnects the session instead of locking first. The password is still required. |
| Accounts (UAC) | Add (the dialog runs inside the elevated helper, so the password never leaves it), edit display name and type, sign out, and remove. Removal asks twice and previews the profile path and size. You can keep or delete the profile folder. |
| Password / PIN / picture | Opens the official pages: `ms-settings:signinoptions`, `ms-settings:yourinfo` and `netplwiz`. A PIN and the picture can only be changed by the account's own user. |
| Shared folders (UAC) | `icacls <path> /grant *SID:(OI)(CI)R` (or `M`, `F`). Whole drives, user profiles, `C:\Windows`, Program Files and ProgramData are refused. Every grant is recorded and removed with `/remove:g *SID`. If the account already had its own permission on the folder, the app never removes it. |
| Disk limit | Uses NTFS quotas through `fsutil` when they work (the *Detect and enable quota support* button checks). Otherwise it uses a **soft** limit that warns at 80% and 100%. About and Storage show which mode is active. |
| Storage overview | Profile size and the size of the shared folders each account can use. |
| Optional ideas | All 20 are built. See [DECISIONS.md](../DECISIONS.md). |

### Where things are stored

| File | Who can change it | Contents |
|---|---|---|
| `C:\ProgramData\AltAccounts\config.json` | Admins only | Shares and their grants, per-account options, quota mode |
| `C:\ProgramData\AltAccounts\audit.log` | Admins; users may only **append** | Switches, sharing changes, admin actions |
| `%LocalAppData%\AltAccounts\user.json` | That user | Trigger, theme, notifications, today's usage |

No file ever contains a password or PIN.

---

## Known limits

- Alt does not work on the sign-in or lock screen, and only in accounts where the app runs. An admin can turn it off per account.
- Switching always goes through the Windows sign-in screen with that account's password or PIN. Windows Home has no direct switch.
- A shared folder is not a game licence (see above). Check Rockstar's own sharing options on their site before relying on them.
- **Daily time limits** are enforced by this app only, so a determined user can close it. **Sign-in hours** use `net user /times`, which Windows enforces for new sign-ins. The app also locks a session that is still open outside the hours. Real parental controls need Microsoft Family Safety, which usually needs Microsoft accounts.
- NTFS quotas count every file the account *owns* on the drive, not exactly "profile + shared folders".
- Windows does not reveal current passwords or PINs.
- While running in the tray the app uses about 80 MB of private memory.

## Uninstall

Use **Settings > Apps > Installed apps > Alt Account Switcher > Uninstall**. The uninstaller:

- stops the app in every account;
- removes the machine-wide autostart value;
- removes every folder permission the app recorded;
- removes every `AltAccounts_*` startup entry the app created in any profile.

It asks whether to keep `C:\ProgramData\AltAccounts` (kept by default).

## Development

```
src/AltAccounts.Core     no UI: accounts, sessions, Alt-tap detector, share validator, icacls/fsutil, config, audit
src/AltAccounts.App      WPF app: tray, keyboard hook, popup, Settings, elevated helper (same exe, --elevated)
tests/AltAccounts.Core.Tests   xUnit (dotnet test)
installer/AltAccounts.iss      Inno Setup
winget/manifests/...           winget manifest (1.6 schema)
tools/make-icon.ps1            regenerates the icon
```

- Debug builds can render the popup and every Settings tab to PNG without touching anything: `AltAccounts.exe --render-preview <folder>`.
- Command-line modes: *(none)* starts the tray app; `--settings` opens Settings; `--elevated <req> <res>` runs the elevated helper; `--init-programdata` and `--uninstall-cleanup [--delete-config]` are used by the installer.
