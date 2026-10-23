# SPDX-License-Identifier: GPL-3.0-or-later
# Verifiable component build; no OBS installation and no public binary release.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$work=Join-Path $root 'artifacts/component-ci'
$evidence=Join-Path $work 'evidence'
New-Item -ItemType Directory -Force -Path $evidence|Out-Null
& (Join-Path $PSScriptRoot 'verify-upstream-notices.ps1')
# Upstream .gitattributes requests CRLF for solution/project files. The pinned
# manifest describes original Git blob bytes. Extract those exact committed
# bytes for this build rather than weakening digest checks for checkout EOLs.
if(Test-Path -LiteralPath (Join-Path $root '.git')){
 $nativeRoot=Join-Path $root 'native/virtual-camera'
 foreach($entry in (Get-Content -LiteralPath (Join-Path $nativeRoot 'upstream-file-manifest.json') -Raw|ConvertFrom-Json)){
  if($entry.gitBlobSha1 -notmatch '^[a-f0-9]{40}$' -or -not $entry.path.StartsWith('upstream/')){throw 'Invalid pinned source entry.'}
  $target=[IO.Path]::GetFullPath((Join-Path $nativeRoot $entry.path))
  if(-not $target.StartsWith((Join-Path $nativeRoot 'upstream')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Pinned source escaped its directory.'}
  $start=[Diagnostics.ProcessStartInfo]::new((Get-Command git -ErrorAction Stop).Source)
  $start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.CreateNoWindow=$true
  foreach($argument in @('-C',$root,'cat-file','blob',$entry.gitBlobSha1)){$start.ArgumentList.Add($argument)}
  $process=[Diagnostics.Process]::Start($start)
  $bytes=[IO.MemoryStream]::new()
  try{
   $process.StandardOutput.BaseStream.CopyTo($bytes);$errorText=$process.StandardError.ReadToEnd();$process.WaitForExit()
   if($process.ExitCode -ne 0){throw ('Missing pinned source Git blob: '+$entry.path+' / '+$errorText)}
   $sha=[Security.Cryptography.SHA256]::Create()
   try{$hash=[BitConverter]::ToString($sha.ComputeHash($bytes.ToArray())).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
   if($hash -cne $entry.sha256){throw ('Pinned source Git blob digest mismatch: '+$entry.path)}
   [IO.File]::WriteAllBytes($target,$bytes.ToArray())
  }finally{$bytes.Dispose();$process.Dispose()}
 }
}
$zig=Join-Path $root '.tools/zig-x86_64-windows-0.14.1/zig.exe'
if(-not(Test-Path -LiteralPath $zig)){
 $archive=Join-Path $root '.tools/downloads/zig-0.14.1.zip'
 New-Item -ItemType Directory -Force -Path (Split-Path $archive -Parent)|Out-Null
 if(-not(Test-Path -LiteralPath $archive)){Invoke-WebRequest -Uri 'https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip' -OutFile $archive}
 if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine '554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c'){throw 'Pinned Zig archive digest mismatch.'}
 Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $root '.tools')
}
$dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$managed=Join-Path $work 'managed'
& $dotnet publish (Join-Path $root 'src/Rilmo.Recorder.csproj') -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true -p:RilmoTestMode=false -p:RilmoStoreEdition=true -o $managed 2>&1|Tee-Object -FilePath (Join-Path $evidence 'managed-build.log')
if($LASTEXITCODE -ne 0){throw 'Managed component build failed.'}
& powershell.exe -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'msix-app-ui-probe.ps1') -ManagedDirectory $managed
if($LASTEXITCODE -ne 0){throw 'MSIX application UI probe failed.'}
& (Join-Path $PSScriptRoot 'build-virtual-camera.ps1') -Architecture both 2>&1|Tee-Object -FilePath (Join-Path $evidence 'native-build.log')
if(-not $?){throw 'Native component build failed.'}
$version=([xml](Get-Content -LiteralPath (Join-Path $root 'src/Rilmo.Recorder.csproj') -Raw)).Project.PropertyGroup|Where-Object Version|Select-Object -First 1 -ExpandProperty Version
$guardRc=Join-Path $work 'installer-guard.rc';$guardRes=Join-Path $work 'installer-guard.res';$guard=Join-Path $work 'installer-guard.dll'
(Get-Content -LiteralPath (Join-Path $root 'packaging/installer-guard.rc.in') -Raw).Replace('@VERSION_COMMAS@',($version.Replace('.',',')+',0')).Replace('@VERSION@',$version)|Set-Content -LiteralPath $guardRc -Encoding utf8
& $zig rc /fo $guardRes $guardRc
if($LASTEXITCODE -ne 0){throw 'Installer guard resource failed.'}
& $zig cc -target x86_64-windows-gnu -O2 -shared (Join-Path $root 'packaging/installer-guard.c') $guardRes -lmsi -o $guard
if($LASTEXITCODE -ne 0){throw 'Installer guard build failed.'}
$native=Join-Path $root 'native/virtual-camera/bin'
foreach($bits in @('64','32')){
 & (Join-Path $native ('camera-smoke'+$bits+'.exe')) (Join-Path $native ('rilmo-virtual-camera'+$bits+'.dll')) 2>&1|Tee-Object -FilePath (Join-Path $evidence ('camera-'+$bits+'.log'))
 if($LASTEXITCODE -ne 0){throw "Registration-free $bits-bit DirectShow test failed."}
}
$files=@((Join-Path $managed 'Rilmo Recorder.exe'),(Join-Path $managed 'Rilmo Recorder.dll'),(Join-Path $native 'rilmo-virtual-camera64.dll'),(Join-Path $native 'rilmo-virtual-camera32.dll'),$guard)
$rows=@(foreach($file in $files){
 $info=(Get-Item -LiteralPath $file).VersionInfo
 if($info.ProductName -cne 'Rilmo Screen Recorder' -or $info.ProductVersion -notmatch ('^'+[regex]::Escape($version)+'(?:\.0)?$')){throw ('Signing metadata mismatch: '+[IO.Path]::GetFileName($file)+' / '+$info.ProductName+' / '+$info.ProductVersion)}
 [ordered]@{file=[IO.Path]::GetFileName($file);sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant();productName=$info.ProductName;productVersion=$info.ProductVersion}
})
[ordered]@{schemaVersion=1;commit=$env:GITHUB_SHA;version=$version;componentBuildPassed=$true;registeredDeviceTested=$false;fullPackageTested=$false;publicBinaryDistributionApproved=$false;signingEnabled=$false;files=$rows}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
Write-Output 'PASS: managed app, camera architectures and installer guard built from source. Only evidence is uploaded; this is not a complete installer or signing approval.'
& (Join-Path $PSScriptRoot 'test-msix-native-ci.ps1') -DotnetPath $dotnet
