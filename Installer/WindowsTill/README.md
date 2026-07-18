# Windows 10 and Windows 11 Till Setup

Use these files on every production till after installing OrderWeb POS. Run them from an administrator account; never make the dedicated POS account an administrator.

## Configure a till

Open Windows PowerShell as Administrator from the repository or deployment media:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Installer\WindowsTill\Configure-OrderWebTill.ps1 -CreatePosAccount
```

The default account is `OrderWebPOS` and the default executable is:

```text
C:\Program Files\OrderWeb POS\POS-in-NET.exe
```

Supply `-PosUserName` or `-AppPath` if the installation differs. The script:

- requires Windows 10 22H2 (build 19045) or Windows 11, using Pro, Enterprise, or Education edition;
- warns that Windows 10 must have active Microsoft Extended Security Updates (ESU), or another applicable supported servicing arrangement;
- creates or validates a dedicated standard local user;
- removes that user from local Administrators if necessary;
- sets the UK timezone and Windows time synchronisation;
- disables sleep and hibernation on mains and battery power;
- enables all Windows firewall profiles and Defender real-time protection;
- starts OrderWeb POS automatically whenever the POS user logs in;
- configures Microsoft Assigned Access so the POS account can run only OrderWeb POS, with the normal desktop and taskbar unavailable;
- warns when system-disk free space is below 20%.

The script does not add antivirus exclusions.

Restart Windows and sign in once as the new POS user to validate Assigned Access. Sign out with `Ctrl+Alt+Delete`, return to the administrator account, and verify the till:

```powershell
.\Installer\WindowsTill\Test-OrderWebTill.ps1
```

The verification command returns exit code `1` if an automated requirement fails.

## Automatic login

Enable automatic login only after confirming that the till and its ports are physically secured. Use the official Microsoft Sysinternals **Autologon** utility interactively. Do not put the POS password in PowerShell, batch files, installer parameters, documentation, or Git.

Keep a separate named administrator account and its password in the company's password manager. Test administrator recovery before opening the site.

The lock-down uses [Windows Assigned Access](https://learn.microsoft.com/windows/configuration/assigned-access/configure-multi-app-kiosk), which supports Windows 10 and Windows 11. Windows 10 receives a compatible XML Start layout; Windows 11 receives the newer StartPins schema. The configuration is applied as LocalSystem because the Windows MDM Bridge requires that security context.

## Windows 10 production policy

Windows 10 reached the end of standard support on 14 October 2025. Old tills may continue to run OrderWeb POS only when all of these conditions are met:

- Windows 10 version 22H2/build 19045 is installed;
- the PC is enrolled and activated for Microsoft ESU, or is covered by another applicable supported servicing programme;
- current cumulative security updates are installed;
- the till passes the same account, Assigned Access, Defender, firewall, clock, power, disk, network and hardware checks as Windows 11.

Windows 10 without current security servicing is not approved for production, even when the POS application starts successfully.

## Administrator recovery

Sign in with the separate administrator account and run:

```powershell
.\Installer\WindowsTill\Remove-OrderWebTillLockdown.ps1
```

Confirm the prompt and restart Windows. This removes Assigned Access and the automatic POS startup task. It deliberately keeps the POS account, timezone, power, firewall, and Defender settings.

## Manual sign-off on every till

- [ ] Windows Update shows no outstanding security or cumulative update and the till has been restarted.
- [ ] The till is physically secured before automatic login is enabled.
- [ ] Staff cannot reach the desktop, Task Manager, browsers, Settings, Control Panel, command prompt, or PowerShell.
- [ ] OrderWeb POS starts after sign-in and restarts if it crashes.
- [ ] The clock shows UK local time and remains synchronised.
- [ ] At least 20% disk space is free; never allow it below 15%.
- [ ] Defender and all firewall profiles are enabled with no full app/database-folder exclusions.
- [ ] The Mother terminal, router, and network switch are connected to a tested UPS.
- [ ] BIOS/UEFI `Restore on AC Power Loss` is set to `Power On` and has been tested.
- [ ] A manager can recover the till using the separate administrator account.

## Important limitations

Windows cannot verify physical security, UPS wiring, or the BIOS power-recovery setting. Those checks must be observed and signed off by the installer. Windows update history also does not prove that no update is pending, so confirm the Windows Update screen manually before go-live.
