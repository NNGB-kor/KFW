param([string]$ApplicationDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'dist/KlipperFirmwareWizard'),[string]$ArchivePath)
$ErrorActionPreference='Stop'
$taskApp=[IO.Path]::GetFullPath($ApplicationDirectory)
$taskInfo=Get-Content -Raw -LiteralPath (Join-Path $taskApp 'docs/build-info.json')|ConvertFrom-Json
if($taskInfo.diagnostics_included){throw 'Rebuild without -Diagnostics before packaging a release.'}
$taskVersion=[Version]$taskInfo.version
$taskVersionText=$taskVersion.ToString(3)
if(!$ArchivePath){$ArchivePath=Join-Path (Split-Path -Parent $taskApp) ('KlipperFirmwareWizard-'+$taskVersionText+'-win-x64.zip')}
$taskArchive=[IO.Path]::GetFullPath($ArchivePath)
if(Test-Path -LiteralPath $taskArchive){throw 'The release ZIP already exists. Choose a new ArchivePath or explicitly replace it.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$files=@(Get-Item -LiteralPath (Join-Path $taskApp 'KlipperFirmwareWizard.exe'),(Join-Path $taskApp 'KlipperFirmwareWizard.exe.config'))
foreach($directory in @('lib','licenses')){$files+=@(Get-ChildItem -LiteralPath (Join-Path $taskApp $directory) -File -Recurse)}
$manifest=Get-Content -Raw -LiteralPath (Join-Path $taskApp 'builder_assets/manifest.json')|ConvertFrom-Json
foreach($name in @('manifest.json','adapters.json','katapult-adapters.json')){$files+=Get-Item -LiteralPath (Join-Path $taskApp ('builder_assets/'+$name))}
$files+=@(Get-ChildItem -LiteralPath (Join-Path $taskApp ('builder_assets/'+$manifest.toolchain_directory)) -Recurse -File)
foreach($name in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','build-info.json')){$files+=Get-Item -LiteralPath (Join-Path $taskApp ('docs/'+$name))}
$zip=[IO.Compression.ZipFile]::Open($taskArchive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in $files){$relative=$file.FullName.Substring($taskApp.Length+1).Replace('\','/');if($relative -match '(^|/)(Klipper|Katapult|winforms-data|Firmware|Bootloader|Out)(/|$)'){throw ('Private/generated content in package: '+$relative)};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,('KlipperFirmwareWizard/'+$relative),[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$zip.Dispose()}
$hash=(Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($taskArchive+'.sha256'),$hash+'  '+[IO.Path]::GetFileName($taskArchive)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
Write-Output $taskArchive
Write-Output ('SHA256 '+$hash)
