param([string]$OutputPath = 'artifacts/card-repository')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$target = [IO.Path]::GetFullPath((Join-Path $repo $OutputPath))
if (-not $target.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package output must be inside the repository.' }
if (Test-Path -LiteralPath $target) { throw 'Choose an unused package output directory.' }
$dist = Join-Path $target 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'frontend/channel-card/src') -Filter '*.js' | Copy-Item -Destination $dist
# HACS versions the entry URL, but relative imports otherwise keep their old
# browser-cache identities. Give every companion the same content fingerprint.
$modules = @(Get-ChildItem -LiteralPath $dist -Filter '*.js' | Sort-Object Name)
$sourceText = ($modules | ForEach-Object { $_.Name + ':' + [IO.File]::ReadAllText($_.FullName).Replace("`r`n", "`n") }) -join "`n"
$sha = [Security.Cryptography.SHA256]::Create()
try { $fingerprint = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($sourceText)))).Replace('-', '').ToLowerInvariant().Substring(0, 16) }
finally { $sha.Dispose() }
$modulePattern = '(?m)(\b(?:from|import)\s*)(["''])(\./[^"'']+\.js)\2'
foreach ($module in $modules) {
    $source = [IO.File]::ReadAllText($module.FullName)
    $versioned = [regex]::Replace($source, $modulePattern, {
        param($match)
        $match.Groups[1].Value + $match.Groups[2].Value + $match.Groups[3].Value + '?v=' + $fingerprint + $match.Groups[2].Value
    })
    [IO.File]::WriteAllText($module.FullName, $versioned, [Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $repo 'frontend/channel-card/hacs.json') -Destination $target
$readme = [IO.File]::ReadAllText((Join-Path $repo 'frontend/channel-card/DISTRIBUTION.md')) + "`n`n" + [IO.File]::ReadAllText((Join-Path $repo 'docs/USER-GUIDE.md'))
[IO.File]::WriteAllText((Join-Path $target 'README.md'), $readme)
Copy-Item -LiteralPath (Join-Path $repo 'docs/USER-GUIDE.md') -Destination (Join-Path $target 'USER-GUIDE.md')
New-Item -ItemType Directory -Path (Join-Path $target 'assets') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'assets/hacs-icon.png') -Destination (Join-Path $target 'assets/hacs-icon.png')
foreach ($notice in @('LICENSE','COPYING','COPYRIGHT.txt','NOTICE.txt')) { Copy-Item -LiteralPath (Join-Path $repo $notice) -Destination $target }
# This is a standalone repository tree, without bridge code, config or credentials.
$archive = $target + '.zip'
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $archive
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
[IO.File]::WriteAllText($archive + '.sha256', $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive) + "`n")
Write-Output $target
Write-Output $archive
