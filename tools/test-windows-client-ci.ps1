# SPDX-License-Identifier: GPL-3.0-or-later
# Actual packaged x64 application on a disposable Windows 11 ARM64 client.
# This covers x64 emulation, not native ARM64 or a physical audio/GPU matrix.
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_ARCH -ne 'ARM64'){throw 'Only the disposable Windows 11 ARM64 CI job may run this test.'}
$root=Split-Path $PSScriptRoot -Parent
$evidence=Join-Path $root 'artifacts/component-ci/evidence'
New-Item -ItemType Directory -Force -Path $evidence|Out-Null
$os=Get-CimInstance Win32_OperatingSystem
if($os.Caption -notmatch 'Windows 11' -or [int]$os.BuildNumber -lt 22000){throw 'A Windows 11 client is required; a Server test does not satisfy this check.'}
$version=(Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw|ConvertFrom-Json).sdk.version
$dotnet=(Get-Command dotnet -ErrorAction SilentlyContinue).Source
if(-not $dotnet -or -not(@(& $dotnet --list-sdks)|Where-Object {$_ -match ('^'+[regex]::Escape($version)+' ')})){
 $directory=Join-Path $root '.tools/dotnet-client-ci'
 $installer=Join-Path $root 'artifacts/dotnet-install-client.ps1'
 Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
 & $installer -Version $version -Architecture arm64 -InstallDir $directory -NoPath
 if(-not $?){throw 'Official .NET SDK acquisition failed.'}
 $dotnet=Join-Path $directory 'dotnet.exe'
}
$receipt=[ordered]@{sourceCommit=$env:GITHUB_SHA;osCaption=$os.Caption;osBuild=$os.BuildNumber;hostArchitecture=$env:RUNNER_ARCH;applicationArchitecture='x64 emulated';noPreinstalledOBS=-not(Test-Path 'C:/Program Files/obs-studio/bin/64bit/obs64.exe');audioHardwareTested=$false;physicalGpuMatrixTested=$false;passed=$false;error=$null}
try{
 if(-not $receipt.noPreinstalledOBS){throw 'Unexpected preinstalled OBS on the clean runner.'}
 & (Join-Path $PSScriptRoot 'verify-upstream-notices.ps1')
 & (Join-Path $PSScriptRoot 'test-msix-native-ci.ps1') -DotnetPath $dotnet
 $native=Get-Content -LiteralPath (Join-Path $evidence 'native-msix-result.json') -Raw|ConvertFrom-Json
 if(-not $native.packageInstalled -or $native.error){throw 'Installed native recording receipt did not pass.'}
 $ui=Get-Content -LiteralPath (Join-Path $evidence 'native-msix-ui-smoke-result.json') -Raw|ConvertFrom-Json
 if(-not $ui.passed){throw 'Packaged recorder UI/integration did not pass.'}
 $receipt.passed=$true
}catch{$receipt.error=$_.Exception.Message;throw}
finally{$receipt|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $evidence 'clean-windows-client.json') -Encoding utf8}
