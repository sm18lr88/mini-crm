# Verification Summary

This page records portable evidence from local verification of Mini CRM v0.1.0-rc.1. It doesn't claim that a GitHub release pipeline has completed or that a published asset exists.

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
- PMAT's documentation grade was 94.6 out of 100 from four Markdown files. That grade doesn't assess product code.
- PMAT repository score was 25 out of 100 under its generic rubric. Its C# dead-code analysis is unsupported and exited with code 5.
- SolidKG supplied partial static evidence across 46 indexed files, with 793 unresolved references, 2 file-cycle signals, 46 unassigned manifest files, and 166 static-unreferenced candidates. These signals aren't proof of runtime defects or architecture health.
- GUI checks did not exercise a normal visible window, native clipboard operation, native file dialogs, actual desktop z-order interaction, multi-monitor behavior, or high-DPI behavior.
- DPAPI behavior was exercised only under the local Windows profile. Cross-profile decryption was not separately tested.

These results support the exercised paths only. Mini CRM is not presented as security audited, HIPAA compliant, or free of defects.
