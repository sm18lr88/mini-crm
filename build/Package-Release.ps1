param(
    [string]$DesktopPublish,
    [string]$CliPublish,
    [string]$Version,
    [string]$SourceCommitSha,
    [string]$SdkVersion,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ExpectedSdkVersion = '11.0.100-rc.1.26425.128'
$ExpectedRuntimeVersion = '11.0.0-rc.1.26425.128'

function Get-Sha256Hex {
    param([Parameter(Mandatory)][string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($stream)
        return [System.Convert]::ToHexString($hash).ToLowerInvariant()
    }
    finally { $stream.Dispose() }
}

function Get-RelativeReleasePath {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Path)
    $relative = [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
    if ($relative -eq '.' -or $relative.StartsWith('../') -or $relative.Contains('/../')) {
        throw "Unsafe publish path '$Path'."
    }
    return $relative
}

function Assert-PublishPathAllowed {
    param([Parameter(Mandatory)][string]$RelativePath)
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or $RelativePath.Contains('\') -or $RelativePath.StartsWith('/') -or $RelativePath.Contains(':')) { throw "Publish output contains unsafe path '$RelativePath'." }
    $segments = $RelativePath.Split('/')
    $blockedSegments = @('data', '.git', '.github', 'build', 'src', 'tests', '.omo', '.solidkg', '.opencode', '.skillos', '.vs')
    foreach ($segment in $segments) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -in @('.', '..') -or $segment.TrimEnd('.', ' ') -ne $segment -or $blockedSegments -contains $segment.ToLowerInvariant()) { throw "Publish output contains excluded path '$RelativePath'." }
    }
    if ($segments[-1] -match '(?i)(\.vault$|\.clb$|\.previous$|\.lock$|^\.env($|\.)|\.pfx$|\.p12$|\.key$|\.pem$|\.ppk$|\.pvk$|credential|secret|token|private.?key|local.?agent)') {
        throw "Publish output contains excluded credential or local-agent file '$RelativePath'."
    }
}

function Get-PublishFiles {
    param([Parameter(Mandatory)][string]$Source)
    if (-not [System.IO.Directory]::Exists($Source)) { throw "Publish folder does not exist: $Source" }
    if (([System.IO.File]::GetAttributes($Source) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Publish folder is a reparse point: $Source" }
    $pending = [System.Collections.Generic.Stack[string]]::new(); $pending.Push($Source)
    while ($pending.Count -gt 0) {
        foreach ($item in [System.IO.Directory]::EnumerateFileSystemEntries($pending.Pop())) {
            $attributes = [System.IO.File]::GetAttributes($item)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Publish output contains a reparse point: $item" }
            if (($attributes -band [System.IO.FileAttributes]::Directory) -ne 0) { $pending.Push($item) } else { $item }
        }
    }
}

function Merge-PublishFolder {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][System.Collections.Generic.Dictionary[string, string]]$KnownPaths
    )
    foreach ($sourceFile in Get-PublishFiles $Source | Sort-Object) {
        $relative = Get-RelativeReleasePath $Source $sourceFile
        Assert-PublishPathAllowed $relative
        $existingRelative = $null
        if ($KnownPaths.TryGetValue($relative, [ref]$existingRelative) -and $existingRelative -cne $relative) {
            throw "Publish collision has a Windows case alias: $relative"
        }
        $KnownPaths[$relative] = $relative
        $target = [System.IO.Path]::Combine($Destination, $relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
        if ([System.IO.File]::Exists($target)) {
            $sameLength = ([System.IO.FileInfo]$sourceFile).Length -eq ([System.IO.FileInfo]$target).Length
            if (-not $sameLength -or (Get-Sha256Hex $sourceFile) -ne (Get-Sha256Hex $target)) {
                throw "Publish collision has different bytes: $relative"
            }
            continue
        }
        [System.IO.File]::Copy($sourceFile, $target)
    }
}

function Assert-ReleaseFolder {
    param([Parameter(Mandatory)][string]$ReleaseRoot, [Parameter(Mandatory)][string]$RuntimeVersion)
    $required = @(
        'CallLedger.exe', 'CallLedger.dll', 'CallLedger.deps.json', 'CallLedger.runtimeconfig.json',
        'CallLedger.Cli.exe', 'CallLedger.Cli.dll', 'CallLedger.Cli.deps.json', 'CallLedger.Cli.runtimeconfig.json',
        'CallLedger.Core.dll', 'CallLedger.Vault.dll', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'clrjit.dll',
        'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'README.md', 'CLI.md',
        'DOTNET-LICENSE.txt', 'DOTNET-THIRD-PARTY-NOTICES.txt')
    foreach ($relative in $required) {
        if (-not [System.IO.File]::Exists([System.IO.Path]::Combine($ReleaseRoot, $relative))) {
            throw "Required release file is missing: $relative"
        }
    }
    foreach ($runtimeConfigName in @('CallLedger.runtimeconfig.json', 'CallLedger.Cli.runtimeconfig.json')) {
        $runtimeConfig = Get-Content -Raw -LiteralPath ([System.IO.Path]::Combine($ReleaseRoot, $runtimeConfigName)) | ConvertFrom-Json
        $frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
        if ($frameworks.Count -eq 0 -or ($frameworks | Where-Object { $_.name -eq 'Microsoft.NETCore.App' -and $_.version -eq $RuntimeVersion }).Count -ne 1) {
            throw "$runtimeConfigName does not pin Microsoft.NETCore.App $RuntimeVersion."
        }
        if ($runtimeConfigName -eq 'CallLedger.runtimeconfig.json' -and ($frameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -eq $RuntimeVersion }).Count -ne 1) {
            throw "$runtimeConfigName does not pin Microsoft.WindowsDesktop.App $RuntimeVersion."
        }
    }
}

function New-ReleasePackage {
    param(
        [Parameter(Mandatory)][string]$DesktopPublish,
        [Parameter(Mandatory)][string]$CliPublish,
        [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+-(rc|beta|alpha)\.[0-9]+$')][string]$Version,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommitSha,
        [Parameter(Mandatory)][string]$SdkVersion,
        [Parameter(Mandatory)][string]$OutputDirectory
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if ($SdkVersion -ne $ExpectedSdkVersion) { throw "Unexpected SDK version: $SdkVersion" }
    [System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $assetName = "mini-crm-$Version-win-x64.zip"
    $archivePath = [System.IO.Path]::Combine($OutputDirectory, $assetName)
    $checksumPath = [System.IO.Path]::Combine($OutputDirectory, 'SHA256SUMS.txt')
    $manifestPath = [System.IO.Path]::Combine($OutputDirectory, 'release-manifest.json')
    foreach ($path in @($archivePath, $checksumPath, $manifestPath)) {
        if ([System.IO.File]::Exists($path)) { throw "Refusing to overwrite existing release artifact: $path" }
    }
    $stageRoot = [System.IO.Path]::Combine($OutputDirectory, ".release-stage-$([Guid]::NewGuid().ToString('N'))")
    $releaseRoot = [System.IO.Path]::Combine($stageRoot, 'CallLedger')
    try {
        [System.IO.Directory]::CreateDirectory($releaseRoot) | Out-Null
        $knownPaths = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        Merge-PublishFolder $DesktopPublish $releaseRoot $knownPaths
        Merge-PublishFolder $CliPublish $releaseRoot $knownPaths
        Assert-ReleaseFolder $releaseRoot $ExpectedRuntimeVersion
        $files = foreach ($file in [System.IO.Directory]::GetFiles($releaseRoot, '*', [System.IO.SearchOption]::AllDirectories) | Sort-Object) {
            $relative = Get-RelativeReleasePath $stageRoot $file
            [ordered]@{ path = $relative; length = ([System.IO.FileInfo]$file).Length; sha256 = Get-Sha256Hex $file }
        }
        $manifest = [ordered]@{ version = $Version; sourceCommitSha = $SourceCommitSha.ToLowerInvariant(); sdkVersion = $SdkVersion; rid = 'win-x64'; files = @($files) }
        $manifestJson = $manifest | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText($manifestPath, $manifestJson + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
        $archive = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in [System.IO.Directory]::GetFiles($releaseRoot, '*', [System.IO.SearchOption]::AllDirectories) | Sort-Object) {
                $entry = $archive.CreateEntry((Get-RelativeReleasePath $stageRoot $file), [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = [System.IO.File]::OpenRead($file); $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
            }
            $entry = $archive.CreateEntry('release-manifest.json', [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = [System.IO.File]::OpenRead($manifestPath); $output = $entry.Open()
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        }
        finally { $archive.Dispose() }
        [System.IO.File]::WriteAllText($checksumPath, "$(Get-Sha256Hex $archivePath) *$assetName$([Environment]::NewLine)", [System.Text.UTF8Encoding]::new($false))
        return [pscustomobject]@{ ArchivePath = $archivePath; ChecksumPath = $checksumPath; ManifestPath = $manifestPath }
    }
    finally { if ([System.IO.Directory]::Exists($stageRoot)) { [System.IO.Directory]::Delete($stageRoot, $true) } }
}

if ($MyInvocation.InvocationName -ne '.') {
    New-ReleasePackage -DesktopPublish $DesktopPublish -CliPublish $CliPublish -Version $Version -SourceCommitSha $SourceCommitSha -SdkVersion $SdkVersion -OutputDirectory $OutputDirectory
}
