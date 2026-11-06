# Unexpected errors

The real application registers `AppDomain.UnhandledException`,
`Application.DispatcherUnhandledException` and `TaskScheduler.UnobservedTaskException`
in the `App` constructor, before display culture and application resources load.
The test host registers its own isolated handlers explicitly.

A fatal handler first writes a report below
`ApplicationPaths.LocalDataDirectory/logs`, normally named
`crash-yyyyMMdd-HHmmss.txt`. A numeric suffix preserves reports when two failures
share the same second. The write uses a temporary file and atomic rename; it never
replaces an existing report.

Reports contain the local timestamp, application version, Windows version,
exception types and HResults, full available stack traces and every inner
exception. The most recently started operation supplies a best-effort hint while
it is still active. Only fixed operation labels are admitted; arbitrary titles
are omitted. Messages, exception `Data`, project state and progress text are not
collected. No separate user/machine names or extra paths are added. Source paths
already present in stack traces are retained.

The native Windows notice identifies the saved report and offers **Open folder**
and **Close**. It requires no WPF dispatcher or resources. An unavailable native
dialog falls back to a basic Windows error notice. A write failure is stated
truthfully. Duplicate exception notifications are suppressed. After the notice
closes, the process exits with a failure code; it does not save a potentially
inconsistent project or enter ordinary close prompts.

**Help > Open error logs folder** opens the same directory, creating it when
needed. Report, folder-launch and notification failures are contained.

Focused validation:

```powershell
./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-crash-handler
```

`CrashHandlerTests` is in DefaultSuite. It runs actual dispatcher, unobserved-task
and domain failures in owned subprocesses with temporary logs and stub notices.
It also exercises the native dialog offscreen with automatic button clicks and
an intercepted folder opener. No real project or user settings are used.
