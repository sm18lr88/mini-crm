# Verification Summary

This page records local and hosted-release evidence for Mini CRM.

## Published Release

- [v0.1.0-rc.2](https://github.com/sm18lr88/mini-crm/releases/tag/v0.1.0-rc.2) was published on September 18, 2026.
- Both jobs in [workflow run 35389735173](https://github.com/sm18lr88/mini-crm/actions/runs/35389735173) completed successfully, including 31 application tests, packaging tests, and the non-visible WPF render check.
- Release source commit: `8f1286045a33a5fa1b576799f612dbc7f184b1c5`.
- ZIP: `mini-crm-v0.1.0-rc.2-win-x64.zip`, 67,914,566 bytes.
- SHA-256: `8075b23c77f919693fd1c4f4f4f7b5904cb41a64287916111a9c1c9e79701a5a`.
- The publicly hosted ZIP was downloaded independently. Its checksum matched both the release checksum and GitHub's asset digest; all 423 manifest file entries verified before extraction.
- The downloaded GUI passed `--render-smoke` with runtime roots pointed at a nonexistent directory. CoreLib, WPF, and CoreCLR resolved inside the extracted package. Downloaded CLI help also passed, and neither check created customer data.
- An anonymous request to the download URL returned HTTP 200. The public release contains exactly the compiled ZIP, `SHA256SUMS.txt`, and `release-manifest.json`.

The earlier `v0.1.0-rc.1` build stopped at the smoke-output guard and did not publish a release. The Windows GUI executable needed an explicit PowerShell output pipeline; the corrected invocation was verified in the successful run. Existing tags were not rewritten.

## Local Evidence

- Release builds for the Windows x64 GUI and CLI completed locally with the exact SDK pinned in `global.json`.
- 31 repository tests passed locally: 19 Core tests and 12 CLI tests.
- Independent acceptance checks covered storage, the editor, view models, and CLI boundaries. The editor acceptance suite completed 12 checks, including `en-US`, `th-TH`, pasted whitespace, and ISO timestamps with seconds.
- The published-package headless smoke path passed without showing a window. WPF binding and closing checks exercised editing, paste and copy routing, masking and reveal, saving, compact and topmost properties, validation, empty state, and recovery state.
- Source review was approved after a Thai-calendar parsing defect was fixed. Gregorian ISO editor input now uses an invariant Gregorian parser.
- Release packaging self-tests rejected missing entry points, private data and backup files, differing merge collisions, tampered archives, traversal, and Windows filename aliases. Pure filesystem tests used synthetic files, not runnable application substitutes.
- A real separate GUI/CLI publish was merged and verified against its per-file manifest. The extracted GUI passed `--render-smoke` with bundle-local runtime libraries, and CLI help passed without creating a data store.

## Release Process

The GitHub release workflow builds and tests both self-contained Windows executables, merges their publish outputs, and validates the package, per-file manifest, and `SHA256SUMS.txt`. It verifies the uploaded draft ZIP by downloading it before making the release public. This page does not substitute for a specific successful workflow run. Release consumers can compare a ZIP's SHA-256 value with `Get-FileHash` and the supplied checksum.

## Tooling Limits

- C# LSP returned internal or workspace-root errors. PMAT has limited C# coverage.
- After release validation, `pmat analyze complexity` reported 49 files but only 2 detected functions, so its C# function coverage is incomplete.
- `pmat analyze tdg` reported 92.9 out of 100 across 15 Markdown files and 1 YAML file. That grade doesn't assess C# product code.
- `pmat analyze dead-code --path .` is unsupported for C# and exited with code 5; it did not pass.
- `pmat repo-score` reported 30 out of 100 under its generic Makefile/hooks/every-push-CI rubric, separate from the successful release workflow.
- The subsequent `solidkg architecture health --path . --json` supplied partial static evidence across 47 indexed files, with 793 unresolved references, 2 file-cycle signals, 47 unassigned manifest files, and 166 static-unreferenced candidates. SCIP was unavailable and layer checks were not run. These signals aren't proof of runtime defects or architecture health.
- GUI checks did not exercise a normal visible window, native clipboard operation, native file dialogs, actual desktop z-order interaction, multi-monitor behavior, or high-DPI behavior.
- DPAPI behavior was exercised only under the local Windows profile. Cross-profile decryption was not separately tested.

These results support the exercised paths only. Mini CRM is not presented as security audited, HIPAA compliant, or free of defects.
