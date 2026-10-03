param([string]$OutputDirectory,[string]$AssetsFrom,[switch]$Diagnostics)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskDest=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot 'dist/KlipperFirmwareWizard'}
$taskAssets=Join-Path $taskRoot 'assets'
$taskManifest=Get-Content -Raw -LiteralPath (Join-Path $taskAssets 'manifest.json')|ConvertFrom-Json
$taskSeed=$null
if($AssetsFrom){
    $taskSeedRoot=[IO.Path]::GetFullPath($AssetsFrom)
    if(Test-Path -LiteralPath (Join-Path $taskSeedRoot 'builder_assets/manifest.json')){$taskSeedRoot=Join-Path $taskSeedRoot 'builder_assets'}
    $taskSeed=Join-Path $taskSeedRoot $taskManifest.toolchain_directory
    if(Test-Path -LiteralPath (Join-Path $taskSeedRoot 'bin/arm-none-eabi-gcc.exe')){$taskSeed=$taskSeedRoot}
}
$taskCompiler=Join-Path $taskDest ('builder_assets/'+$taskManifest.toolchain_directory)
& (Join-Path $PSScriptRoot 'prepare_toolchain.ps1') -Destination $taskCompiler -SourceCompiler $taskSeed -AssetsDirectory $taskAssets
foreach($name in @('manifest.json','adapters.json','katapult-adapters.json')){Copy-Item -LiteralPath (Join-Path $taskAssets $name) -Destination (Join-Path $taskDest ('builder_assets/'+$name)) -Force}
$taskDiagnosticFlag=$Diagnostics.IsPresent.ToString().ToLowerInvariant()
dotnet publish (Join-Path $taskRoot 'nativecs/KlipperNative.csproj') -c Release -p:SelfContained=false "-p:IncludeDiagnostics=$taskDiagnosticFlag" -o $taskDest
if($LASTEXITCODE -ne 0){throw 'C# publish failed'}
$taskLib=Join-Path $taskDest 'lib';$taskDocs=Join-Path $taskDest 'docs';$taskLicenseDir=Join-Path $taskDest 'licenses'
foreach($folder in @($taskLib,$taskDocs,$taskLicenseDir,(Join-Path $taskDest 'Out'))){[IO.Directory]::CreateDirectory($folder)|Out-Null}
foreach($file in Get-ChildItem -LiteralPath $taskDest -File -Filter '*.dll'){Move-Item -LiteralPath $file.FullName -Destination (Join-Path $taskLib $file.Name) -Force}
foreach($name in @('LICENSE','README.md','README_CSHARP_KO.md','build-info.json','package-info.json','validation-report.json')){if(Test-Path -LiteralPath (Join-Path $taskDest $name)){Move-Item -LiteralPath (Join-Path $taskDest $name) -Destination (Join-Path $taskDocs $name) -Force}}
foreach($name in @('LICENSE','README.md','THIRD_PARTY_NOTICES.md')){if(Test-Path -LiteralPath (Join-Path $taskRoot $name)){Copy-Item -LiteralPath (Join-Path $taskRoot $name) -Destination (Join-Path $taskDocs $name) -Force}}
$taskNuget=Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'nativecs/obj/project.assets.json')|ConvertFrom-Json
foreach($package in $taskNuget.libraries.PSObject.Properties.Name){
    if($package -like 'Microsoft.NETFramework.ReferenceAssemblies*'){continue}
    foreach($cache in $taskNuget.packageFolders.PSObject.Properties.Name){
        $folder=Join-Path $cache $package;if(!(Test-Path -LiteralPath $folder)){continue}
        Get-ChildItem -LiteralPath $folder -File -Recurse|Where-Object {$_.Name -match 'LICENSE|COPYING|NOTICE|\.nuspec$'}|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskLicenseDir (($package -replace '/','-')+'-'+$_.Name)) -Force}
        break
    }
}
$taskForbidden=@(Get-ChildItem -LiteralPath $taskDest -Recurse -File|Where-Object {$_.Name -match '^python.*\.(dll|exe)$|^FirmwareEngine\.exe$|^coreclr\.dll$|^hostfxr\.dll$|^System\.Private\.CoreLib\.dll$'})
if($taskForbidden.Count){throw 'Unexpected bundled runtime files'}
$taskExe=Join-Path $taskDest 'KlipperFirmwareWizard.exe'
@{application='Klipper Firmware Wizard';version=[Diagnostics.FileVersionInfo]::GetVersionInfo($taskExe).FileVersion;exe_sha256=(Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant();target_framework='net48';runtime_bundled=$false;diagnostics_included=$Diagnostics.IsPresent;managed_libraries='lib';documents='docs'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $taskDocs 'build-info.json') -Encoding UTF8
Write-Output "C# application updated in place: $taskDest"
