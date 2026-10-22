param([ValidateSet('Browser','Game')][string]$Mode='Browser',[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$project=Join-Path $workspace 'artifacts\advanced-sources'
$output=Join-Path $project 'out'
$records=Join-Path $project ($Mode.ToLowerInvariant()+'-results')
$dotnet=Join-Path $workspace '.tools\dotnet\dotnet.exe'
[IO.Directory]::CreateDirectory($output) | Out-Null
[IO.Directory]::CreateDirectory($records) | Out-Null
if (-not $SkipBuild) {
    $projectXml='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings><AssemblyName>AdvancedSourcesSmoke</AssemblyName></PropertyGroup><ItemGroup><Compile Include="..\..\src\Engine\*.cs" Exclude="..\..\src\Engine\MuxRelay.cs"/><Compile Include="..\..\tools\AdvancedSourcesSmoke.cs"/></ItemGroup></Project>'
    Set-Content -LiteralPath (Join-Path $project 'AdvancedSourcesSmoke.csproj') -Value $projectXml -Encoding utf8
    & $dotnet build (Join-Path $project 'AdvancedSourcesSmoke.csproj') -c Release -o $output --nologo
    if($LASTEXITCODE -ne 0){throw 'Advanced source harness compilation failed.'}
    & (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $output
    & (Join-Path $PSScriptRoot 'stage-browser-runtime.ps1') -AppDirectory $output
}
$env:DOTNET_ROOT=Split-Path -Parent $dotnet
$stdout=Join-Path $records 'stdout.log';$stderr=Join-Path $records 'stderr.log'
$process=Start-Process -FilePath (Join-Path $output 'AdvancedSourcesSmoke.exe') -ArgumentList @('"'+$records+'"',$Mode) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
if(-not $process.WaitForExit(90000)){throw "Advanced source test timed out; inspect owned PID $($process.Id) before cleanup."}
Get-Content -LiteralPath $stdout
if($process.ExitCode -ne 0){Get-Content -LiteralPath $stderr;throw "Advanced source smoke failed ($($process.ExitCode))."}
$ffmpeg='C:\REV\tools\ai\ffmpeg-lgpl\ffmpeg-master-latest-win64-lgpl\bin\ffmpeg.exe'
if(-not (Test-Path -LiteralPath $ffmpeg)){throw 'Set the smoke-test decoder path to a local FFmpeg build.'}
Add-Type -AssemblyName System.Drawing
$recordingPrefix=if($Mode -eq 'Browser'){'BROWSER_RECORDING='}else{'GAME_RECORDING='}
$paths=@(Get-Content -LiteralPath $stdout | Where-Object {$_.StartsWith($recordingPrefix)} | ForEach-Object {$_.Substring($recordingPrefix.Length)})
if($paths.Count -eq 0){throw 'No recording was produced.'}
foreach($recording in $paths){
    $frame=$recording+'.decoded.png'
    & $ffmpeg -hide_banner -loglevel error -y -ss 0.5 -i $recording -frames:v 1 $frame
    if($LASTEXITCODE -ne 0){throw 'Recorded frame decode failed.'}
    $bitmap=[Drawing.Bitmap]::FromFile($frame)
    try {$pixel=$bitmap.GetPixel([int]($bitmap.Width/2),[int]($bitmap.Height/2));if($Mode -eq 'Browser' -and -not($pixel.G -gt 150 -and $pixel.R -lt 100 -and $pixel.B -lt 130)){throw "Decoded browser pixels are unexpected: $pixel"};if($Mode -eq 'Game' -and $pixel.G -lt 100){throw 'Decoded game pixels are unexpected.'}}finally{$bitmap.Dispose()}
    Write-Output "DECODED_FRAME=passed; FILE=$frame"
}
