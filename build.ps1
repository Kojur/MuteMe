param([switch]$Test)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework 4.x compiler is required.' }
$destination = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$app = Join-Path $destination 'MuteMe.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /win32icon:"$PSScriptRoot\assets\MuteMe.ico" /out:$app /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\src\MuteMe.cs" "$PSScriptRoot\src\Audio.cs" "$PSScriptRoot\src\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Built $app"
if ($Test) {
    $report = Join-Path $destination 'test-results.txt'
    $process = Start-Process -FilePath $app -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw (Get-Content -LiteralPath $report -Raw) }
    Get-Content -LiteralPath $report
}
