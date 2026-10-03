param([Parameter(Mandatory=$true)][string]$ApplicationDirectory)
$ErrorActionPreference='Stop'
$taskApp=[IO.Path]::GetFullPath($ApplicationDirectory);$taskExe=Join-Path $taskApp 'KlipperFirmwareWizard.exe'
foreach($folder in @('Klipper','Katapult','winforms-data')){if(Test-Path -LiteralPath (Join-Path $taskApp $folder)){throw ('Cold-start test requires no '+$folder+' directory')}}
$taskOut=Join-Path (Split-Path -Parent $PSScriptRoot) 'build/first-run-check';[IO.Directory]::CreateDirectory($taskOut)|Out-Null
$jobs=@();$results=@{}
foreach($project in @('klipper','katapult')){
    $request=Join-Path $taskOut ($project+'-request.json');$stdout=Join-Path $taskOut ($project+'.log');$stderr=Join-Path $taskOut ($project+'-error.log')
    @{project=$project;action='startup';data=(Join-Path $taskApp 'winforms-data')}|ConvertTo-Json -Compress|Set-Content -LiteralPath $request -Encoding ASCII
    $process=Start-Process -FilePath $taskExe -ArgumentList '--engine' -RedirectStandardInput $request -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    $jobs+=@{project=$project;process=$process;stdout=$stdout;stderr=$stderr;last=''}
}
$deadline=[DateTime]::UtcNow.AddMinutes(6)
while(@($jobs|Where-Object {!$_.process.HasExited}).Count){
    if([DateTime]::UtcNow -gt $deadline){foreach($job in $jobs){if(!$job.process.HasExited){$job.process.Kill()}};throw 'First-run download timed out'}
    foreach($job in $jobs){if(Test-Path -LiteralPath $job.stdout){$line=Get-Content -LiteralPath $job.stdout -Tail 12|Where-Object {$_ -like '@@DOWNLOAD_PROGRESS@@*'}|Select-Object -Last 1;if($line -and $line -ne $job.last){Write-Output ('{0}: {1}' -f $job.project,$line.Substring(21));$job.last=$line}}}
    Start-Sleep -Seconds 2
}
foreach($job in $jobs){
    $job.process.WaitForExit();$marker=Get-Content -LiteralPath $job.stdout|Where-Object {$_ -like '@@RESULT@@*'}|Select-Object -Last 1
    if(!$marker){throw ('No result from '+$job.project+': '+(Get-Content -Raw -LiteralPath $job.stderr))}
    $response=$marker.Substring(10)|ConvertFrom-Json
    if(!$response.ok -or !$response.result.latest_selected -or !$response.result.assets_ready){throw ($job.project+': '+$response.error+' '+$response.result.startup_warning)}
    $results[$job.project]=@{version=$response.result.version;latest_selected=$true;assets_ready=$true;settings_rows=@($response.result.rows).Count}
}
@{success=$true;projects=$results;physical_hardware_opened=$false}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $taskOut 'report.json') -Encoding UTF8
Write-Output 'PASS: both source directories created and latest source/settings/build inputs downloaded from an empty cache.'
