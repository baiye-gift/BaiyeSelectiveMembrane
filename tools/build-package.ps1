param(
    [string]$GameManaged = 'D:\Steam\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed',
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'mod_info.yaml')), '(?m)^version:\s*([0-9]+\.[0-9]+\.[0-9]+)\s*$').Groups[1].Value
if (-not $version) { throw 'Missing semantic version.' }
& $Python (Join-Path $root 'tools/verify-assets.py')
if ($LASTEXITCODE -ne 0) { throw 'Animation asset verification failed.' }
& dotnet build (Join-Path $root 'SelectiveMembrane.csproj') -c Release "-p:GameManaged=$GameManaged" -v:q -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
$stage = [IO.Path]::GetFullPath((Join-Path $root 'dist/package/BaiyeSelectiveMembrane'))
if (-not $stage.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path.' }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'bin/Release/netstandard2.1/BaiyeSelectiveMembrane.dll') -Destination $stage
foreach ($file in @('mod.yaml','mod_info.yaml','README.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $root 'docs/playtest.md') -Destination (Join-Path $stage 'PLAYTEST.md')
$assetFiles = @(
    'anim/assets/baiye_membrane_tile/baiye_membrane_tile_anim.bytes',
    'anim/assets/baiye_membrane_tile/baiye_membrane_tile_build.bytes',
    'anim/assets/baiye_membrane_tile/baiye_membrane_tile.png',
    'anim/assets/baiye_membrane_door/baiye_membrane_door_anim.bytes',
    'anim/assets/baiye_membrane_door/baiye_membrane_door_build.bytes',
    'anim/assets/baiye_membrane_door/baiye_membrane_door.png'
)
foreach ($relative in $assetFiles) {
    $destination = Join-Path $stage $relative
    New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $destination
}
$expectedFiles = @('BaiyeSelectiveMembrane.dll','mod.yaml','mod_info.yaml','README.md','CHANGELOG.md','PLAYTEST.md') + $assetFiles
$dlls = @(Get-ChildItem -LiteralPath $stage -Recurse -Filter '*.dll')
if ($dlls.Count -ne 1 -or $dlls[0].Name -ne 'BaiyeSelectiveMembrane.dll') { throw 'Unexpected packaged dependencies.' }
$zip = Join-Path $root "dist/BaiyeSelectiveMembrane-$version.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $stage -DestinationPath $zip
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($archive.Entries | Where-Object { $_.Name })
    if ($entries.Count -ne $expectedFiles.Count) { throw 'Unexpected package file count.' }
    $seen = @{}
    foreach ($entry in $entries) {
        $entryPath = $entry.FullName.Replace('\','/')
        if (-not $entryPath.StartsWith('BaiyeSelectiveMembrane/', [StringComparison]::Ordinal)) { throw 'Unexpected archive root.' }
        $relative = $entryPath.Substring('BaiyeSelectiveMembrane/'.Length)
        if ($relative -notin $expectedFiles -or $seen.ContainsKey($relative)) { throw "Unexpected/duplicate archive file: $relative" }
        $seen[$relative] = $true
        $file = [IO.Path]::GetFullPath((Join-Path $stage $relative))
        if (-not $file.StartsWith($stage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive entry.' }
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $zipHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $stream.Dispose(); $sha.Dispose() }
        if ($zipHash -ne (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash) { throw "Checksum mismatch: $relative" }
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($zip + '.sha256', "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
Write-Output 'PASS package: 12 files including six own animation assets, one own DLL, every file checksum verified.'
Write-Output "Package: $zip"
Write-Output "SHA256: $hash"
