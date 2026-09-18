Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Package-Release.ps1"
. "$PSScriptRoot/Verify-Release.ps1"

function Assert-Throws {
    param([Parameter(Mandatory)][scriptblock]$Action, [Parameter(Mandatory)][string]$Name)
    try { & $Action } catch { return }
    throw "Expected failure: $Name"
}

function Write-FixtureFile {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$RelativePath, [string]$Content = 'fixture')
    $path = [System.IO.Path]::Combine($Root, $RelativePath)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    [System.IO.File]::WriteAllText($path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function New-PublishFixture {
    param([Parameter(Mandatory)][string]$Root)
    $desktop = [System.IO.Path]::Combine($Root, 'desktop')
    $cli = [System.IO.Path]::Combine($Root, 'cli')
    [System.IO.Directory]::CreateDirectory($desktop) | Out-Null
    [System.IO.Directory]::CreateDirectory($cli) | Out-Null
    $runtime = '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"11.0.0-rc.1.26425.128"},{"name":"Microsoft.WindowsDesktop.App","version":"11.0.0-rc.1.26425.128"}]}}'
    $cliRuntime = '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"11.0.0-rc.1.26425.128"}]}}'
    foreach ($name in @('CallLedger.exe', 'CallLedger.dll', 'CallLedger.deps.json', 'CallLedger.Core.dll', 'CallLedger.Vault.dll', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'clrjit.dll', 'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'README.md', 'CLI.md', 'DOTNET-LICENSE.txt', 'DOTNET-THIRD-PARTY-NOTICES.txt')) { Write-FixtureFile $desktop $name }
    Write-FixtureFile $desktop 'CallLedger.runtimeconfig.json' $runtime
    foreach ($name in @('CallLedger.Cli.exe', 'CallLedger.Cli.dll', 'CallLedger.Cli.deps.json', 'CallLedger.Core.dll', 'CallLedger.Vault.dll')) { Write-FixtureFile $cli $name }
    Write-FixtureFile $cli 'CallLedger.Cli.runtimeconfig.json' $cliRuntime
    return [pscustomobject]@{ Desktop = $desktop; Cli = $cli }
}

function New-UnsafeArchive {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$BaseArchive, [Parameter(Mandatory)][string[]]$Entries)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.File]::Copy($BaseArchive, $Path)
    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        foreach ($name in $Entries) {
            $writer = [System.IO.StreamWriter]::new($archive.CreateEntry($name).Open())
            try { $writer.Write('x') } finally { $writer.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    $checksum = [System.IO.Path]::ChangeExtension($Path, 'SHA256SUMS.txt')
    [System.IO.File]::WriteAllText($checksum, "$(Get-Sha256Hex $Path) *$([System.IO.Path]::GetFileName($Path))$([Environment]::NewLine)", [System.Text.UTF8Encoding]::new($false))
    return $checksum
}

$root = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "CallLedger.PackageTests.$([Guid]::NewGuid().ToString('N'))")
try {
    [System.IO.Directory]::CreateDirectory($root) | Out-Null
    $fixture = New-PublishFixture $root
    $positiveOutput = [System.IO.Path]::Combine($root, 'positive')
    $package = New-ReleasePackage -DesktopPublish $fixture.Desktop -CliPublish $fixture.Cli -Version 'v1.2.3-rc.1' -SourceCommitSha ('a' * 40) -SdkVersion '11.0.100-rc.1.26425.128' -OutputDirectory $positiveOutput
    $verified = Test-ReleasePackage -ArchivePath $package.ArchivePath -ChecksumPath $package.ChecksumPath -ManifestPath $package.ManifestPath
    if ($verified.FileCount -lt 20) { throw 'Positive fixture did not retain required files.' }
    $missing = New-PublishFixture ([System.IO.Path]::Combine($root, 'missing'))
    [System.IO.File]::Delete([System.IO.Path]::Combine($missing.Cli, 'CallLedger.Cli.exe'))
    Assert-Throws { New-ReleasePackage -DesktopPublish $missing.Desktop -CliPublish $missing.Cli -Version 'v1.2.3-rc.2' -SourceCommitSha ('b' * 40) -SdkVersion '11.0.100-rc.1.26425.128' -OutputDirectory ([System.IO.Path]::Combine($root, 'missing-output')) } 'missing CLI'
    $private = New-PublishFixture ([System.IO.Path]::Combine($root, 'private'))
    Write-FixtureFile $private.Desktop 'Data/call-ledger.vault' 'private'
    Assert-Throws { New-ReleasePackage -DesktopPublish $private.Desktop -CliPublish $private.Cli -Version 'v1.2.3-rc.3' -SourceCommitSha ('c' * 40) -SdkVersion '11.0.100-rc.1.26425.128' -OutputDirectory ([System.IO.Path]::Combine($root, 'private-output')) } 'private data'
    $backup = New-PublishFixture ([System.IO.Path]::Combine($root, 'backup'))
    Write-FixtureFile $backup.Desktop 'root-backup.clb' 'private'
    Assert-Throws { New-ReleasePackage -DesktopPublish $backup.Desktop -CliPublish $backup.Cli -Version 'v1.2.3-rc.3' -SourceCommitSha ('c' * 40) -SdkVersion '11.0.100-rc.1.26425.128' -OutputDirectory ([System.IO.Path]::Combine($root, 'backup-output')) } 'root backup'
    $collision = New-PublishFixture ([System.IO.Path]::Combine($root, 'collision'))
    Write-FixtureFile $collision.Cli 'CallLedger.Core.dll' 'different'
    Assert-Throws { New-ReleasePackage -DesktopPublish $collision.Desktop -CliPublish $collision.Cli -Version 'v1.2.3-rc.4' -SourceCommitSha ('d' * 40) -SdkVersion '11.0.100-rc.1.26425.128' -OutputDirectory ([System.IO.Path]::Combine($root, 'collision-output')) } 'colliding merge'
    $publishCaseAlias = New-PublishFixture ([System.IO.Path]::Combine($root, 'publish-case-alias'))
    Write-FixtureFile $publishCaseAlias.Cli 'readme.md'
    Assert-Throws { New-ReleasePackage -DesktopPublish $publishCaseAlias.Desktop -CliPublish $publishCaseAlias.Cli -Version 'v1.2.3-rc.4' -SourceCommitSha ('d' * 40) -SdkVersion '11.0.100-rc.1.26425.128' -OutputDirectory ([System.IO.Path]::Combine($root, 'publish-case-alias-output')) } 'publish Windows case alias'
    $tampered = [System.IO.Path]::Combine($root, 'tampered.zip')
    [System.IO.File]::Copy($package.ArchivePath, $tampered)
    $bytes = [System.IO.File]::ReadAllBytes($tampered); $bytes[0] = $bytes[0] -bxor 1; [System.IO.File]::WriteAllBytes($tampered, $bytes)
    Assert-Throws { Test-ReleasePackage -ArchivePath $tampered -ChecksumPath $package.ChecksumPath -ManifestPath $package.ManifestPath } 'tampered archive'
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $traversal = [System.IO.Path]::Combine($root, 'traversal.zip')
    $archive = [System.IO.Compression.ZipFile]::Open($traversal, [System.IO.Compression.ZipArchiveMode]::Create)
    try { $entry = $archive.CreateEntry('../escape'); $writer = [System.IO.StreamWriter]::new($entry.Open()); try { $writer.Write('x') } finally { $writer.Dispose() } } finally { $archive.Dispose() }
    $traversalChecksum = [System.IO.Path]::Combine($root, 'traversal.SHA256SUMS.txt')
    [System.IO.File]::WriteAllText($traversalChecksum, "$(Get-Sha256Hex $traversal) *traversal.zip$([Environment]::NewLine)", [System.Text.UTF8Encoding]::new($false))
    Assert-Throws { Test-ReleasePackage -ArchivePath $traversal -ChecksumPath $traversalChecksum -ManifestPath $package.ManifestPath } 'path traversal'
    $privateArchive = [System.IO.Path]::Combine($root, 'private.zip')
    $privateChecksum = New-UnsafeArchive $privateArchive $package.ArchivePath @('CallLedger/private.zip')
    Assert-Throws { Test-ReleasePackage -ArchivePath $privateArchive -ChecksumPath $privateChecksum -ManifestPath $package.ManifestPath } 'private archive'
    $caseAlias = [System.IO.Path]::Combine($root, 'case-alias.zip')
    $caseAliasChecksum = New-UnsafeArchive $caseAlias $package.ArchivePath @('CallLedger/README.md', 'CallLedger/readme.md')
    Assert-Throws { Test-ReleasePackage -ArchivePath $caseAlias -ChecksumPath $caseAliasChecksum -ManifestPath $package.ManifestPath } 'Windows case alias'
    'PASS packaging filesystem self-test'
}
finally { if ([System.IO.Directory]::Exists($root)) { [System.IO.Directory]::Delete($root, $true) } }
