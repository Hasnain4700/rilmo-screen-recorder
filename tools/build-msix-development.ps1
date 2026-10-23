# SPDX-License-Identifier: GPL-3.0-or-later
# An unsigned, independent identity for package validation only. This cannot be
# uploaded to the reserved Store product and never changes release approval flags.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishedDirectory,[string]$ManagedPublishDirectory,[string]$MakeAppxPath)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$published=[IO.Path]::GetFullPath($PublishedDirectory)
if(-not $published.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Development input must stay inside this workspace.'}
foreach($name in @('Rilmo Recorder.exe','Rilmo Recorder.dll','coreclr.dll','obs.dll','runtime/obs/runtime-inventory.json')){
 if(-not(Test-Path -LiteralPath (Join-Path $published $name))){throw ('Complete self-contained published app required: '+$name)}
}
if(-not $MakeAppxPath){$MakeAppxPath=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/App Certification Kit/makeappx.exe'}
if(-not(Test-Path -LiteralPath $MakeAppxPath)){throw 'Microsoft MakeAppx tool is unavailable.'}
$output=Join-Path $root ('artifacts/msix-development-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
if($output.StartsWith($published.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Development output must not be inside the source publish directory.'}
if((Get-Item -LiteralPath $published).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked publish root is not permitted.'}
if(Test-Path -LiteralPath $output){throw 'Development output already exists; preserving it.'}
$stage=Join-Path $output 'stage';$app=Join-Path $stage 'app'
New-Item -ItemType Directory -Path $app -Force|Out-Null
foreach($file in Get-ChildItem -LiteralPath $published -Recurse -File){
 $relative=[IO.Path]::GetRelativePath($published,$file.FullName)
 if($relative -match '(^|\\)(tools|virtual-camera)(\\|$)|^rilmo-virtual-camera(32|64)\.dll$|^windows-installer\.json$|\.pdb$'){continue}
 if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked development input is not permitted.'}
 $target=Join-Path $app $relative
 New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null
 Copy-Item -LiteralPath $file.FullName -Destination $target
}
if($ManagedPublishDirectory){
 $managed=[IO.Path]::GetFullPath($ManagedPublishDirectory)
 if(-not $managed.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)-or -not(Test-Path -LiteralPath (Join-Path $managed 'coreclr.dll'))){throw 'Managed overlay must be a self-contained publish inside this workspace.'}
 foreach($file in Get-ChildItem -LiteralPath $managed -Recurse -File){
  if($file.Extension -eq '.pdb'){continue}
  if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked managed overlay is not permitted.'}
  $target=Join-Path $app ([IO.Path]::GetRelativePath($managed,$file.FullName))
  New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null
  Copy-Item -LiteralPath $file.FullName -Destination $target -Force
 }
}
& (Join-Path $root 'tools/configure-msix-framework.ps1') -AppDirectory $app
Copy-Item -LiteralPath (Join-Path $root 'packaging/Assets') -Destination $stage -Recurse
$manifest=Get-Content -LiteralPath (Join-Path $root 'packaging/AppxManifest.template.xml') -Raw
$manifest=$manifest.Replace('__IDENTITY_NAME__','Rilmo.Development.ScreenRecorder').Replace('__PUBLISHER__','CN=RilmoDevelopmentOnly').Replace('__PUBLISHER_DISPLAY_NAME__','Rilmo development test').Replace('__VERSION__','0.4.2.0').Replace('__MAX_VERSION_TESTED__','10.0.19045.0')
$manifest=$manifest.Replace('<DisplayName>Rilmo Screen Recorder</DisplayName>','<DisplayName>Rilmo development test</DisplayName>').Replace('DisplayName="Rilmo Screen Recorder"','DisplayName="Rilmo development test"')
[IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'),$manifest,[Text.UTF8Encoding]::new($false))
'DEVELOPMENT VALIDATION ONLY. Unsigned, not Store-ready. Virtual camera is excluded from this Store release. Runtime source/license, package-graph and final WACK qualification remain incomplete. Do not distribute.'|Set-Content -LiteralPath (Join-Path $app 'DEVELOPMENT-PACKAGE.txt') -Encoding utf8
$package=Join-Path $output 'Rilmo-Development-0.4.2-x64-unsigned.msix'
& $MakeAppxPath pack /d $stage /p $package /o *> (Join-Path $output 'makeappx.log')
if($LASTEXITCODE -ne 0){throw ('MakeAppx validation failed. Inspect '+$output)}
[ordered]@{schemaVersion=1;developmentOnly=$true;storeUploadAllowed=$false;identity='Rilmo.Development.ScreenRecorder';publisher='CN=RilmoDevelopmentOnly';version='0.4.2.0';package=$package;sha256=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant();cameraIntegrated=$false;sourceComplianceApproved=$false;wackPassed=$false;installed=$false;signed=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
Write-Output ('Unsigned development MSIX validated: '+$package)
