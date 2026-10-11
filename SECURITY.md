# Security policy

## Supported versions

Security fixes go into the latest release of the .NET edition only. Please update before reporting.

| Version | Supported |
|---|---|
| 1.0.11 (latest) | yes |
| 1.0.0 – 1.0.10 | no, update to the latest release |
| 0.9.x (PowerShell edition in `legacy/`) | no, kept for reference only |

## Reporting a vulnerability

Please report vulnerabilities **privately** through GitHub:

1. Open https://github.com/grvelvet/shelly/security.
2. Click **Report a vulnerability** (GitHub private vulnerability reporting). This creates a private security advisory
   that only the maintainers can see.

Please include the Shelly version (Settings → About), Windows version, the steps or a script that shows the problem,
and what you expected. Don't open a public issue for a vulnerability and don't publish details before a fix is
released. There is no security e-mail address; GitHub's private reporting is the channel. Shelly is maintained in spare
time, so there is no fixed response time, but reports are taken seriously and credited in the CHANGELOG if you wish.

**In scope**, for example: Shelly running a script without the confirmation it promises (danger findings, administrator
runs, a script marked as downloaded from the internet), a way to make a dangerous script look trusted (trusted-source
matching, SHA-256 trust), Shelly removing the Mark of the Web on its own, code execution from Shelly's own files
(settings, history, cache) or from a script that is only listed and not run, or the update check fetching anything but
release metadata.

**Not vulnerabilities by themselves**: a script you chose to run doing harm (that is what scripts can do; see below), a
new way to hide a command from the static check (welcome as a regular issue or pull request with a test script for the
corpus in `tests/Shelly.Core.Tests/TestData/Safety`), or SmartScreen warnings about the unsigned exe.

## Security model

- **Shelly runs scripts with your user rights.** Normal runs have the rights of your Windows account. *As
  administrator* runs go through UAC and get full control of the PC. Built `.exe` files run the script the same way,
  without any of Shelly's checks.
- **Shelly is not a sandbox.** It doesn't isolate, restrict or monitor a running script. To run code you don't trust,
  use a virtual machine or Windows Sandbox.
- **The safety check is a static guardrail.** Before a run or a build Shelly reads the script with PowerShell's own
  parser (and a simpler built-in scanner until that has reported) and flags risky commands as danger, warning or info.
  It resolves aliases, constant variables, string tricks and splatting, and flags code it can't see (dynamic
  invocation, encoded commands, reflection, download-and-run) as danger. It can't know what a URL serves, what a script
  reads or downloads at run time, or what programs it starts do; it can miss things and it can raise false alarms.
  Danger findings ask before every run and can't be trusted; warnings can be trusted per file version (path + SHA-256).
  Administrator runs always ask.
- **Trusted sources** turn download-and-run from listed HTTPS sources into an info notice. They don't check the
  downloaded code: trusting a source means trusting whatever it serves at run time. Only HTTPS URLs match; a host covers
  its subdomains; shared hosts (GitHub, URL shorteners, file hosts, CDNs) need an owner/repo or file path. The built-in
  entries are the install commands documented by those projects and can be turned off in Settings → Safety.
- **Mark of the Web.** Scripts that Windows marked as downloaded from the internet (or restricted sites) are blocked
  from running and building until you unblock them in Shelly, which removes the mark. Shelly reads the mark only on
  NTFS; files without it (other file systems, some archivers, files created by scripts) are not blocked. Shelly never
  removes the mark on its own.
- **Network.** Shelly itself contacts the network only for the update check (GitHub release metadata; nothing is
  downloaded or installed automatically), for the PowerShell 7 installer when you choose it, and when you click a link.
  Your scripts can do anything their code does.
- **Integrity of the download.** `Shelly.exe` isn't code-signed. Each release lists SHA-256 checksums in
  `SHA256SUMS.txt`; check them with `Get-FileHash` (see the [README](README.md#download)).

## Disclaimer

Shelly is provided "as is", without warranty of any kind (see [LICENSE](LICENSE)). Run only scripts you trust and have
read. The authors are not responsible for damage caused by scripts run with Shelly. The same notice is shown on the
first start of Shelly and in Settings → About → Disclaimer.
