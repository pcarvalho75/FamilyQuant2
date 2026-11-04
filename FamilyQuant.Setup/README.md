# FamilyQuant Setup

Build one unsigned Windows x64 installer from the same self-contained folder as the release ZIP:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File FamilyQuant.Setup/Build-Setup.ps1 -PublishFolder <published-app-folder> -OutputFolder <fresh-build-output> -PayloadZip <existing-flat-release.zip>
```

`PayloadZip` is optional; without it the script creates a flat ZIP from the folder. An existing
ZIP is embedded unchanged only after every file name, length and SHA-256 matches the folder.
The result is `FamilyQuant2-<version>-Setup.exe` and a SHA-256 sidecar. The app and compressed
payload are embedded. Nothing is downloaded, installed or started by the build script. Restore
uses an empty local package source and the machine's existing .NET runtime package cache; a
missing package stops the build. Keep intermediate build folders out of the public release.

The interactive installer defaults to `%LOCALAPPDATA%\Programs\FamilyQuant2`, uses only the
current user's Start menu and HKCU uninstall entry, and offers an optional desktop shortcut and
an explicit Launch choice. It requires no administrator rights. Settings and projects under
`%LOCALAPPDATA%\FamilyQuant2` are separate from program files and are retained.

For the updater:

```text
FamilyQuant2-<version>-Setup.exe /silent /dir="<existing program folder>" /waitpid=<application PID>
```

The specified process has 60 seconds to exit. Setup then checks for a running application before
changing files and again before replacement. It never kills the application. Exit codes are 0
for success, 1 for failure/cancellation, 2 for invalid arguments and 3 for a running application
or concurrent setup. Quiet mode has no window and never launches the app. It reports its message
to stderr and `%TEMP%\FamilyQuant2-Setup-<pid>.log`.

All files are checksum-verified in a sibling staging folder before the installation directory
is replaced. Integration failure rolls back both the program folder and prior registry/shortcut
state. Upgrades preserve unlisted personal files and a quiet upgrade keeps the desktop preference.
A recognized older portable folder can be adopted; unrelated nonempty folders and downgrades are
refused. Do not run Setup from inside the folder it will replace.

The registered `FamilyQuant2-Uninstall.exe` accepts `/uninstall /dir="<program folder>"` and
optional `/silent`. It copies itself into a private temporary folder, starts that helper and
exits so Windows can remove the original executable. The helper waits for the original process.
The temporary helper is left for ordinary Windows Temp cleanup; no shell deletion command is
used. Uninstall removes only recorded unchanged files and owned empty folders, retaining user
data, unlisted files/folders and locally changed files.

This installer is unsigned. Windows may show **Windows protected your PC**. For the verified
FamilyQuant release, choose **More info > Run anyway**. Verify the release asset's SHA-256 first.
No signing or third-party installer tooling is used.

## Test-only execution on the development machine

**Never run a normal install here.** Use only this command, with a fresh child folder under the
current user's `%LOCALAPPDATA%\Temp`:

```powershell
FamilyQuant.Tests.exe --test-setup-native <built-Setup.exe> <fresh-LocalAppData-Temp-root> 2.1.4.0
```

That command always passes `/test-mode /silent /test-root=<root> /dir=<root>/app` (plus an owned
result file). Registry, shortcuts, running-process checks, waiting and Launch are file-backed
fakes. Every install/uninstall write stays below the selected test root. It exercises embedded
extraction, older fake upgrade, running/wait refusal, silent execution and the installed
uninstaller's relocation. It reads the installed EXE FileVersion/ProductVersion and adjacent
`FamilyQuant2.dll` AssemblyVersion; it never starts the installed app. ProductVersion may retain
its `+<git sha>` suffix. User data is planted and hash-checked through upgrade and uninstall.

`--test-setup` uses tiny ZIPs and a fake platform for transactional/ownership/path tests.
`--test-setup-window` calls FQLook's inert controller checks on an STA thread. Both are in the
default suite. The installer's `/screens=<folder>` dispatch constructs fake controllers only,
before creating a production controller or machine adapter.

Build settings follow [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/).
The per-user adapter uses the standard [uninstall entry values](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key)
under its fixed HKCU product key, never HKLM.
