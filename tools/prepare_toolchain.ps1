param(
    [Parameter(Mandatory=$true)][string]$Destination,
    [string]$SourceCompiler,
    [string]$AssetsDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'assets')
)
$ErrorActionPreference='Stop'
$taskManifest=Get-Content -Raw -LiteralPath (Join-Path $AssetsDirectory 'manifest.json')|ConvertFrom-Json
$taskProfilePath=Join-Path $AssetsDirectory 'toolchain-profile.json'
$taskProfile=Get-Content -Raw -LiteralPath $taskProfilePath|ConvertFrom-Json
$taskDestination=[IO.Path]::GetFullPath($Destination)
$taskPrefix=$taskDestination.TrimEnd('\')+'\'
function TargetPath([string]$relative){$resolved=[IO.Path]::GetFullPath((Join-Path $taskDestination $relative));if(!$resolved.StartsWith($taskPrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe toolchain path'};return $resolved}
function Matches([string]$path,$expected){return (Test-Path -LiteralPath $path -PathType Leaf) -and (Get-Item -LiteralPath $path).Length -eq $expected.bytes -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $expected.sha256}
[IO.Directory]::CreateDirectory($taskDestination)|Out-Null
$taskMissing=@($taskProfile.files.PSObject.Properties|Where-Object {!(Matches (TargetPath $_.Name) $_.Value)})
if($taskMissing.Count -gt 0 -and $SourceCompiler){
    foreach($item in $taskMissing){$from=Join-Path $SourceCompiler $item.Name;if(!(Matches $from $item.Value)){throw ('Invalid toolchain seed: '+$item.Name)};$to=TargetPath $item.Name;[IO.Directory]::CreateDirectory((Split-Path -Parent $to))|Out-Null;[IO.File]::Copy($from,$to,$true)}
}elseif($taskMissing.Count -gt 0){
    $taskCache=Join-Path (Split-Path -Parent $PSScriptRoot) 'build/downloads';[IO.Directory]::CreateDirectory($taskCache)|Out-Null
    $taskArchive=Join-Path $taskCache ('xpack-arm-'+$taskManifest.toolchain_version+'.zip')
    if(!(Test-Path -LiteralPath $taskArchive) -or (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne $taskManifest.toolchain_archive_sha256){
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $taskManifest.toolchain_url -OutFile ($taskArchive+'.partial')
        if((Get-FileHash -LiteralPath ($taskArchive+'.partial') -Algorithm SHA256).Hash -ne $taskManifest.toolchain_archive_sha256){throw 'Toolchain archive hash mismatch'}
        Move-Item -LiteralPath ($taskArchive+'.partial') -Destination $taskArchive -Force
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $taskZip=[IO.Compression.ZipFile]::OpenRead($taskArchive)
    try{
        $gcc=@($taskZip.Entries|Where-Object {$_.FullName.EndsWith('/bin/arm-none-eabi-gcc.exe')})
        if($gcc.Count -ne 1){throw 'Ambiguous toolchain archive layout'}
        $base=$gcc[0].FullName.Substring(0,$gcc[0].FullName.Length-'bin/arm-none-eabi-gcc.exe'.Length)
        foreach($item in $taskMissing){$entry=$taskZip.GetEntry($base+$item.Name.Replace('\','/'));if(!$entry){throw ('Toolchain archive is missing '+$item.Name)};$to=TargetPath $item.Name;[IO.Directory]::CreateDirectory((Split-Path -Parent $to))|Out-Null;[IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$to,$true);if(!(Matches $to $item.Value)){throw ('Toolchain file hash mismatch: '+$item.Name)}}
    }finally{$taskZip.Dispose()}
}
Copy-Item -LiteralPath $taskProfilePath -Destination (Join-Path $taskDestination 'compact-profile.json') -Force
Write-Output ('Toolchain verified: '+@($taskProfile.files.PSObject.Properties).Count+' files')
