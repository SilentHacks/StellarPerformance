[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]*$')][string]$RepositoryName = 'StellarPerformance',
    [string]$GameDirectory,
    [switch]$IncludeOptionalModTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'mod.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($field in @('id', 'name', 'version', 'description')) {
    if ([string]::IsNullOrWhiteSpace($manifest.$field)) { throw "Missing manifest field: $field" }
}
if ($manifest.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part release version.' }
$project = Join-Path $root 'src\StellarPerformance\StellarPerformance.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
if ($projectXml.Project.PropertyGroup.Version -cne $manifest.version) { throw 'Project and manifest versions differ.' }
$entryPoint = Get-Content -LiteralPath (Join-Path $root 'src\StellarPerformance\PerformancePlugin.cs') -Raw
if ($entryPoint -notmatch ('Version = "' + [regex]::Escape($manifest.version) + '";')) { throw 'Melon version differs.' }
$changes = & git -C $root status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit the reviewed source before packaging; the repository must be clean.' }
$commit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'A local source commit is required.' }
$properties = @('-p:IncludeOptionalModTests=' + $IncludeOptionalModTests.ToString().ToLowerInvariant())
if ($GameDirectory) { $properties += '-p:GameDirectory=' + $GameDirectory }
& dotnet build (Join-Path $root 'StellarPerformance.sln') -c Release @properties --no-incremental --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
& dotnet test (Join-Path $root 'tests\StellarPerformance.Tests\StellarPerformance.Tests.csproj') -c Release @properties --no-build --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
$dll = Join-Path $root 'src\StellarPerformance\bin\Release\net472\StellarPerformance.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString() -cne ($manifest.version + '.0')) { throw 'Shipping DLL version differs.' }
$output = Join-Path $root ('artifacts\' + $manifest.version)
$null = New-Item -ItemType Directory -Path $output -Force
$zipPath = Join-Path $output ($RepositoryName + '.zip')
$inputs = [ordered]@{ 'Mods/StellarPerformance.dll' = $dll; 'mod.json' = $manifestPath }
Add-Type -AssemblyName System.IO.Compression
$archiveStream = [IO.File]::Open($zipPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($pair in $inputs.GetEnumerator()) {
            $entry = $archive.CreateEntry($pair.Key, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $sourceStream = [IO.File]::OpenRead($pair.Value)
            try { $entryStream = $entry.Open(); try { $sourceStream.CopyTo($entryStream) } finally { $entryStream.Dispose() } } finally { $sourceStream.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $archiveStream.Dispose() }
$verifyStream = [IO.File]::OpenRead($zipPath)
try {
    $archive = [IO.Compression.ZipArchive]::new($verifyStream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        if ($archive.Entries.Count -ne $inputs.Count) { throw 'Unexpected archive entries.' }
        foreach ($entry in $archive.Entries) {
            if (!$inputs.Contains($entry.FullName)) { throw ('Unapproved archive entry: ' + $entry.FullName) }
            $hash = [Security.Cryptography.SHA256]::Create()
            try {
                $entryStream = $entry.Open()
                try { $actual = [BitConverter]::ToString($hash.ComputeHash($entryStream)).Replace('-', '') } finally { $entryStream.Dispose() }
            } finally { $hash.Dispose() }
            if ($actual -cne (Get-FileHash -LiteralPath $inputs[$entry.FullName] -Algorithm SHA256).Hash) { throw 'Archive bytes differ from the verified inputs.' }
        }
    } finally { $archive.Dispose() }
} finally { $verifyStream.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $output ($RepositoryName + '.sha256')), $zipHash + '  ' + $RepositoryName + ".zip`n")
[ordered]@{ Version=$manifest.version; IntendedTag=('v'+$manifest.version); Commit=$commit.Trim(); Archive=($RepositoryName+'.zip'); SHA256=$zipHash; DllSHA256=(Get-FileHash -LiteralPath $dll).Hash; Entries=@($inputs.Keys); OptionalModTests=[bool]$IncludeOptionalModTests } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding utf8
Write-Output ('Prepared ' + $zipPath + '; SHA256=' + $zipHash + '; no deployment or publishing performed.')
