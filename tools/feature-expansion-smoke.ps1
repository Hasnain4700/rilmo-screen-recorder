$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$project=Join-Path $root 'artifacts\feature-expansion'
New-Item -ItemType Directory -Path $project -Force | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings></PropertyGroup><ItemGroup><Compile Include="..\..\src\Engine\*.cs" Exclude="..\..\src\Engine\MuxRelay.cs"/><Compile Include="..\..\tools\FeatureExpansionSmoke.cs"/></ItemGroup></Project>
'@ | Set-Content (Join-Path $project 'FeatureExpansion.csproj')
$out=Join-Path $project 'out'
& (Join-Path $root '.tools\dotnet\dotnet.exe') build (Join-Path $project 'FeatureExpansion.csproj') -c Release -o $out --nologo
if($LASTEXITCODE -ne 0){throw 'Feature test build failed.'}
& (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $out
$results=Join-Path $project ('results-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
$previous=$env:DOTNET_ROOT
try{$env:DOTNET_ROOT=Join-Path $root '.tools\dotnet'; & (Join-Path $out 'FeatureExpansion.exe') $results; if($LASTEXITCODE -ne 0){throw 'Feature expansion smoke failed.'}}finally{$env:DOTNET_ROOT=$previous}
