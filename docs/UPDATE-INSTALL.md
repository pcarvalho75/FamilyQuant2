# Checking and installing updates

FamilyQuant checks the public releases-only repository a few seconds after its main window is
shown, once per start. A newer release produces a dismissible banner. The check does not take
the operation/busy host or open a dialog, browser, or download. Offline, failed and current checks
stay silent and append an outcome to `update-check.log` beside the per-user `shell.json` file.
Help > Check for updates at startup turns this default-on preference off. Existing preference
files without the new field retain the default. Help > Check for updates works independently.

Help > Install update, the banner's Install update button and the manual check's Install update
button use the same explicit action. If the copy is current, the action says so. A newer release
without an installer offers its release page. No download starts just because a check succeeded.

The installer asset is `FamilyQuant2-<tag without v>-Setup.exe`. Its metadata name and HTTPS
`github.com/<repository>/releases/download/<tag>/<name>` path must agree exactly. Automatic HTTP
redirects are disabled; each hop is checked before requesting it. Only the initial asset URL and
the exact HTTPS hosts `release-assets.githubusercontent.com` and `objects.githubusercontent.com`
are allowed. Credentials, nondefault ports, insecure hops and arbitrary hosts are refused.
There are no private credentials or authenticated requests.

The downloader streams into a unique temporary folder with byte progress and cancellation. It
checks the declared length and GitHub's SHA256 digest when supplied. Failed, incomplete and
cancelled files never become the executable target. Checking and downloading do not execute code
in Core. The shell honors the ordinary Save/Discard/Cancel choice before downloading. It launches
the completed installer with `/silent`, `/dir=<current install folder>`, and `/waitpid=<own PID>`,
then closes FamilyQuant. A failed launch leaves the app open. The installer owns the bounded
60-second process wait, running-app guard and replacement (M7a).

`FamilyQuant.Tests.exe --test-update-check <fresh-output>` runs the existing checker tests plus
fake-network metadata/download tests and native UI tests on a never-selected private desktop.
They never use a real network or installer: process launch and close are captured by fakes.
The existing DefaultSuite enrollment includes the expanded command, so these checks run in both
ordinary quick and full gates. A fixture result is not an installed-release or live-download proof.
