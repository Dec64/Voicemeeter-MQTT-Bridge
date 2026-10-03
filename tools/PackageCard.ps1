param([string]$OutputPath = 'artifacts/card-repository')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$target = [IO.Path]::GetFullPath((Join-Path $repo $OutputPath))
if (-not $target.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package output must be inside the repository.' }
if (Test-Path -LiteralPath $target) { throw 'Choose an unused package output directory.' }
$dist = Join-Path $target 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'frontend/channel-card/src') -Filter '*.js' | Copy-Item -Destination $dist
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
