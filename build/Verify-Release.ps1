param(
    [string]$ArchivePath,
    [string]$ChecksumPath,
    [string]$ManifestPath,
    [string]$ExtractTo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ExpectedSdkVersion = '11.0.100-rc.1.26425.128'
$ExpectedRuntimeVersion = '11.0.0-rc.1.26425.128'
. "$PSScriptRoot/Package-Release.ps1"

function Assert-SafeZipEntries {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchive]$Archive)
    if ($Archive.Entries.Count -gt 512) { throw 'Archive has too many entries.' }
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $totalLength = [int64]0
    foreach ($entry in $Archive.Entries) {
        $name = $entry.FullName
        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrEmpty($entry.Name) -or -not $seen.Add($name)) {
            throw "Unsafe or duplicate ZIP entry '$name'."
        }
        if ($name -ne 'release-manifest.json' -and -not $name.StartsWith('CallLedger/')) { throw "Unexpected ZIP entry '$name'." }
        if ($name -ne 'release-manifest.json') { Assert-PublishPathAllowed $name.Substring('CallLedger/'.Length) }
        if ($entry.Length -gt 268435456) { throw "ZIP entry is too large: $name" }
        $totalLength += $entry.Length
        if ($totalLength -gt 2147483648) { throw 'Archive expands beyond the 2 GiB validation limit.' }
    }
}

function Read-ZipEntryUtf8 {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry)
    $reader = [System.IO.StreamReader]::new($Entry.Open(), [System.Text.UTF8Encoding]::new($false), $true)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Test-ReleasePackage {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$ChecksumPath,
        [Parameter(Mandatory)][string]$ManifestPath,
        [string]$ExtractTo
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($path in @($ArchivePath, $ChecksumPath, $ManifestPath)) { if (-not [System.IO.File]::Exists($path)) { throw "Release artifact is missing: $path" } }
    if ($ExtractTo -and ([System.IO.File]::Exists($ExtractTo) -or [System.IO.Directory]::Exists($ExtractTo))) { throw "Refusing to overwrite existing verification target: $ExtractTo" }
    $expectedChecksum = ([System.IO.File]::ReadAllText($ChecksumPath, [System.Text.UTF8Encoding]::new($false))).Trim()
    $assetName = [System.IO.Path]::GetFileName($ArchivePath)
    if ($expectedChecksum -ne "$(Get-Sha256Hex $ArchivePath) *$assetName") { throw 'SHA256SUMS.txt does not match the archive.' }
    $externalManifest = [System.IO.File]::ReadAllText($ManifestPath, [System.Text.UTF8Encoding]::new($false))
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        Assert-SafeZipEntries $archive
        $manifestEntry = $archive.GetEntry('release-manifest.json')
        if ($null -eq $manifestEntry -or (Read-ZipEntryUtf8 $manifestEntry) -ne $externalManifest) { throw 'Archive manifest does not match release-manifest.json.' }
        $manifest = $externalManifest | ConvertFrom-Json
        if ($manifest.version -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+-(rc|beta|alpha)\.[0-9]+$' -or $manifest.sourceCommitSha -notmatch '^[0-9a-f]{40}$' -or $manifest.sdkVersion -ne $ExpectedSdkVersion -or $manifest.rid -ne 'win-x64' -or $manifest.files.Count -eq 0) { throw 'Manifest has invalid release identity or file list.' }
        $entryNames = @($archive.Entries.FullName)
        $manifestPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($file in $manifest.files) {
            if ($file.path -notmatch '^CallLedger/[^/].*' -or $file.path -eq 'release-manifest.json' -or -not $manifestPaths.Add($file.path) -or $entryNames -notcontains $file.path) { throw "Manifest path is invalid or absent: $($file.path)" }
            Assert-PublishPathAllowed $file.path.Substring('CallLedger/'.Length)
            $entry = $archive.GetEntry($file.path)
            if ($entry.Length -ne [int64]$file.length) { throw "Manifest length mismatch: $($file.path)" }
            $temporary = [System.IO.Path]::GetTempFileName()
            try { $stream = $entry.Open(); $output = [System.IO.File]::OpenWrite($temporary); try { $stream.CopyTo($output) } finally { $output.Dispose(); $stream.Dispose() }; if ((Get-Sha256Hex $temporary) -ne $file.sha256) { throw "Manifest hash mismatch: $($file.path)" } }
            finally { [System.IO.File]::Delete($temporary) }
        }
        if ($manifestPaths.Count -ne $entryNames.Count - 1) { throw 'Manifest file list does not exactly describe the archive.' }
        $required = @(
            'CallLedger/CallLedger.exe', 'CallLedger/CallLedger.dll', 'CallLedger/CallLedger.deps.json', 'CallLedger/CallLedger.runtimeconfig.json',
            'CallLedger/CallLedger.Cli.exe', 'CallLedger/CallLedger.Cli.dll', 'CallLedger/CallLedger.Cli.deps.json', 'CallLedger/CallLedger.Cli.runtimeconfig.json',
            'CallLedger/CallLedger.Core.dll', 'CallLedger/CallLedger.Vault.dll', 'CallLedger/hostfxr.dll', 'CallLedger/hostpolicy.dll',
            'CallLedger/coreclr.dll', 'CallLedger/clrjit.dll', 'CallLedger/System.Private.CoreLib.dll', 'CallLedger/PresentationFramework.dll',
            'CallLedger/WindowsBase.dll', 'CallLedger/README.md', 'CallLedger/CLI.md', 'CallLedger/DOTNET-LICENSE.txt', 'CallLedger/DOTNET-THIRD-PARTY-NOTICES.txt')
        foreach ($path in $required) { if ($entryNames -notcontains $path) { throw "Archive misses required file: $path" } }
        foreach ($runtimeConfigName in @('CallLedger/CallLedger.runtimeconfig.json', 'CallLedger/CallLedger.Cli.runtimeconfig.json')) {
            $runtimeConfig = (Read-ZipEntryUtf8 ($archive.GetEntry($runtimeConfigName))) | ConvertFrom-Json
            $frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
            if (($frameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' -and $_.version -eq $ExpectedRuntimeVersion }).Count -ne 1) { throw "$runtimeConfigName does not pin Microsoft.NETCore.App." }
            if ($runtimeConfigName -eq 'CallLedger/CallLedger.runtimeconfig.json' -and ($frameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -eq $ExpectedRuntimeVersion }).Count -ne 1) { throw "$runtimeConfigName does not pin Microsoft.WindowsDesktop.App." }
        }
        if ($ExtractTo) { [System.IO.Directory]::CreateDirectory($ExtractTo) | Out-Null; foreach ($entry in $archive.Entries) { $target = [System.IO.Path]::Combine($ExtractTo, $entry.FullName.Replace('/', [System.IO.Path]::DirectorySeparatorChar)); [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null; $input = $entry.Open(); $output = [System.IO.File]::Create($target); try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() } } }
        return [pscustomobject]@{ Version = $manifest.version; FileCount = $manifest.files.Count; ArchivePath = $ArchivePath }
    }
    finally { $archive.Dispose() }
}

if ($MyInvocation.InvocationName -ne '.') {
    Test-ReleasePackage -ArchivePath $ArchivePath -ChecksumPath $ChecksumPath -ManifestPath $ManifestPath -ExtractTo $ExtractTo
}
