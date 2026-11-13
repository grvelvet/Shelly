# Security policy

## Supported versions

Security fixes go into the latest release of the .NET edition only. Please update before reporting.

| Version | Supported |
|---|---|
| 1.5.0 (latest) | yes |
| < 1.5.0 | no |

## Reporting a vulnerability

Please report vulnerabilities **privately** through GitHub:

1. Open https://github.com/grvelvet/shelly/security.
2. Click **Report a vulnerability** (GitHub private vulnerability reporting) to create a private security advisory.

Include the Shelly version (Settings → About), Windows version, steps or a sample script, and expected behavior. Do not open a public issue or disclose details before a fix is released. There is no security e-mail address; reports are reviewed in spare time and credited in `CHANGELOG.md` if desired.

**In scope:** Shelly running a script without the confirmation it promises (danger findings, administrator runs, Mark of the Web), bypassing trusted-source or SHA-256 trust matching, Shelly removing Mark of the Web on its own, code execution from Shelly's own data files or simply listing a script, or the update check fetching anything other than release metadata.

**Not vulnerabilities by themselves:** harm caused by a script you chose to run, a new obfuscation pattern bypassing static analysis (welcome as a regular issue or PR with a test case for `tests/Shelly.Core.Tests/TestData/Safety`), or SmartScreen warnings on the unsigned executable.

## Security model

- **User rights, not a sandbox:** scripts run with your Windows account rights (or full control after UAC for administrator runs). Built `.exe` wrappers run scripts directly without Shelly's checks. Use a VM or Windows Sandbox for untrusted code.
- **Static guardrail & SHA-256 binding:** PowerShell scripts are parsed with PowerShell's AST (plus a fallback C# scanner before PS responds). Confirmations and *Don't warn again* trust bind to the exact path + SHA-256 hash; files are re-hashed right before launch (and after UAC). Administrator runs always prompt.
- **Batch & custom interpreters:** `.bat`/`.cmd` files receive a basic command scan (no variable expansion). Custom interpreter scripts are not checked. Arguments typed in Shelly are quoted (`CommandLineToArgvW`; cmd-expansion characters are rejected for Batch).
- **Trusted HTTPS sources:** listed HTTPS hosts (or `host/owner/repo` on shared hosts) downgrade download-and-run findings to info notices. Trusting a source means trusting whatever it serves at run time.
- **Mark of the Web & imported folders:** files with NTFS `Zone.Identifier` 3 or 4 are blocked until explicitly unblocked. Imported folders re-verify SHA-256 on the main script file only, preserve MOTW, ignore symlinks/junctions, and delete only inside `Scripts` when marked with `.shelly.json`.
- **Network & download integrity:** Shelly contacts the network only for update checks (GitHub release metadata), user-initiated PowerShell 7 installation, or clicked links. Verify downloads against `SHA256SUMS.txt` (`Get-FileHash`).

For full analyzer rules, severities, and the built-in trusted sources list, see [docs/USER_GUIDE.md (Safety checks)](docs/USER_GUIDE.md#6-safety-checks).

## Disclaimer

Shelly is provided "as is", without warranty of any kind (see [LICENSE](LICENSE)). Run only scripts you trust and have read. The authors are not responsible for damage caused by scripts run with Shelly. The same notice is shown on the first start of Shelly.
