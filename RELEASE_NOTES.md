# Mini CRM Windows Prerelease

Mini CRM is a prerelease, offline Windows call tracker for managing customers, calls, and follow-ups. The release title identifies the application version.

## Install

Get the Windows x64 package from the [Mini CRM releases page](https://github.com/sm18lr88/mini-crm/releases). Select `mini-crm-v*-win-x64.zip`, not GitHub's Source code ZIP. Extract all files, keep the `CallLedger` folder in a writable local folder, then run `CallLedger.exe`.

The package is self-contained. It doesn't need an installer or a separate .NET runtime. It includes `CallLedger.Cli.exe` for structured, local command-line work.

## Included

- Dark-by-default GUI with compact topmost mode and expanded search.
- Customer records for calls, follow-ups, and notes.
- Required customer name with optional DOB, SSN, Medicare ID, phone, follow-up time, call reason, and notes.
- Copy and paste support, sensitive-field masking, and a saved display preference shared with the CLI.
- Encrypted local storage and backup support for the current Windows profile.

Close the GUI before running the CLI. `--redact` hides only DOB, SSN, and Medicare ID. It doesn't hide names, phone numbers, call reasons, or notes. Read [CLI.md](https://github.com/sm18lr88/mini-crm/blob/main/CLI.md) before automating the CLI.

## Privacy and Limits

Mini CRM is offline and has no automatic notifications while it is closed. It makes no HIPAA compliance claim.

Storage uses Windows DPAPI and is bound to the current Windows profile. Data isn't portable across PCs or Windows accounts. Lost profile keys can make encrypted data unrecoverable. Masking doesn't protect copied values or command output. Windows clipboard history and clipboard sync can expose copied data. AI providers can receive any CLI output you send to them.

## RC Support

This package bundles the prerelease .NET 11 RC1 runtime. Support ends October 13, 2026. Update Mini CRM before then. .NET 11 GA is planned for November 10, 2026. The package doesn't update its bundled runtime automatically.

The build is unsigned. This release is not presented as security audited or HIPAA compliant.

## Verification Status

Local validation passed 19 Core tests, 12 CLI tests, and no-window WPF checks. The release workflow builds and tests the applications, validates the ZIP and its per-file manifest, downloads the uploaded draft ZIP for verification, and only then publishes it. See [verification details](https://github.com/sm18lr88/mini-crm/blob/main/docs/VERIFICATION.md) for scope and known native-interaction limits.

Compare the downloaded ZIP with the accompanying `SHA256SUMS.txt` in PowerShell:

```powershell
$version = 'v0.1.0-rc.2' # Use the version shown in this release's title.
Get-FileHash ".\mini-crm-$version-win-x64.zip" -Algorithm SHA256
```
