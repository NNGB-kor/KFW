param([string]$ApplicationDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'build/diagnostics'),[string]$Python)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot;$taskApp=[IO.Path]::GetFullPath($ApplicationDirectory);$taskExe=Join-Path $taskApp 'KlipperFirmwareWizard.exe'
$taskInfo=Get-Content -Raw -LiteralPath (Join-Path $taskApp 'docs/build-info.json')|ConvertFrom-Json
if(!$taskInfo.diagnostics_included){throw 'Build with -Diagnostics before running developer checks.'}
$taskOut=Join-Path $taskRoot 'build/checks';[IO.Directory]::CreateDirectory($taskOut)|Out-Null
function EngineCase([hashtable]$request){
    $request['data']=Join-Path $taskOut 'data';$request['project']='klipper'
    $start=[Diagnostics.ProcessStartInfo]::new($taskExe,'--engine');$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($start);$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync();$process.StandardInput.WriteLine(($request|ConvertTo-Json -Compress));$process.StandardInput.Close();$process.WaitForExit()
    $line=($stdout.Result -split '\r?\n')|Where-Object {$_ -like '@@RESULT@@*'}|Select-Object -Last 1
    if(!$line){throw $stderr.Result};$result=$line.Substring(10)|ConvertFrom-Json
    if($process.ExitCode -ne 0 -or !$result.ok){throw ($result.error+' '+$stderr.Result)};return $result.result
}
$report=@{uart=(EngineCase @{action='uart_entry_test'});dfu=(EngineCase @{action='dfu_test'});hardware_flashed=$false}
$null=EngineCase @{action='diagnostic_exports';output=(Join-Path $taskOut 'remote')}
$dependencyReport=Join-Path $taskOut 'dependencies.json'
$process=Start-Process -FilePath $taskExe -ArgumentList @('--dependency-smoke-test',('"'+$dependencyReport+'"')) -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0){throw 'Dependency/wizard startup check failed'}
$report['dependencies']=Get-Content -Raw -LiteralPath $dependencyReport|ConvertFrom-Json
if($Python){
    & $Python (Join-Path $taskRoot 'tests/Python/test_ssh_runner.py') (Join-Path $taskOut 'remote/runner.py');if($LASTEXITCODE -ne 0){throw 'SSH runner test failed'}
    & $Python (Join-Path $taskRoot 'tests/Python/test_dfu_remote.py') (Join-Path $taskOut 'remote');if($LASTEXITCODE -ne 0){throw 'DFU bridge test failed'}
    & $Python (Join-Path $taskRoot 'tests/Python/test_bootsel_restore.py') (Join-Path $taskOut 'remote/rp2040_install.py');if($LASTEXITCODE -ne 0){throw 'BOOTSEL restore test failed'}
}
$report['success']=$report.uart.success -and $report.dfu.success -and $report.dependencies.success
$report|ConvertTo-Json -Depth 14|Set-Content -LiteralPath (Join-Path $taskOut 'report.json') -Encoding UTF8
if(!$report.success){throw 'Diagnostic test failure'}
Write-Output 'PASS: native protocol, library loading, wizard initialization and requested remote mocks.'
