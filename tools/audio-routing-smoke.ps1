$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$project=Join-Path $root 'artifacts\audio-routing'
New-Item -ItemType Directory -Path $project -Force | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings></PropertyGroup><ItemGroup><Compile Include="..\..\src\Engine\*.cs" Exclude="..\..\src\Engine\MuxRelay.cs"/><Compile Include="..\..\tools\AudioRoutingSmoke.cs"/></ItemGroup></Project>
'@ | Set-Content (Join-Path $project 'AudioRouting.csproj')
$out=Join-Path $project 'out'
& (Join-Path $root '.tools\dotnet\dotnet.exe') build (Join-Path $project 'AudioRouting.csproj') -c Release -o $out --nologo
if($LASTEXITCODE -ne 0){throw 'Audio routing build failed.'}
& (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $out
$ffmpeg=& (Join-Path $root '.tools\benchmark-python\Scripts\python.exe') -c 'import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())'
foreach($frequency in @(440,880)){
    & $ffmpeg -v error -y -f lavfi -i "sine=frequency=${frequency}:sample_rate=48000:duration=2" -ac 2 (Join-Path $project "$frequency.wav")
    if($LASTEXITCODE -ne 0){throw 'Tone fixture generation failed.'}
}
$results=Join-Path $project ('results-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
$previous=$env:DOTNET_ROOT
try{$env:DOTNET_ROOT=Join-Path $root '.tools\dotnet'; & (Join-Path $out 'AudioRouting.exe') $results (Join-Path $project '440.wav') (Join-Path $project '880.wav'); if($LASTEXITCODE -ne 0){throw 'Audio routing smoke failed.'}}finally{$env:DOTNET_ROOT=$previous}
