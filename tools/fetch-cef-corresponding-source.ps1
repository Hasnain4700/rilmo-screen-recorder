# SPDX-License-Identifier: GPL-3.0-or-later
# Source preparation only. Run deliberately on a suitably sized build machine.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$DownloadDirectory,[string]$Python='python',[switch]$BuildRelease)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$destination=[IO.Path]::GetFullPath($DownloadDirectory)
if(Test-Path -LiteralPath $destination){throw 'Choose a new empty source directory; existing files are preserved.'}
New-Item -ItemType Directory -Path $destination|Out-Null
$archive=Join-Path $destination 'cef-source.zip'
$cefCommit='2b7d20b8cdcba6e4e9507a1015b8262515a196c6'
Invoke-WebRequest -Uri ('https://codeload.github.com/chromiumembedded/cef/zip/'+$cefCommit) -OutFile $archive
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine 'f5039ba15b7ba129fe11a9ba1c039f2a2a06d9d7958129c45b993bbe458ad7a5'){throw 'Pinned CEF source archive changed.'}
Expand-Archive -LiteralPath $archive -DestinationPath $destination
$automation=Join-Path $destination ('cef-'+$cefCommit+'/tools/automate/automate-git.py')
# The pinned automation reads its own CHROMIUM_BUILD_COMPATIBILITY.txt and
# selects depot_tools 22f6c9d8e9; gclient resolves root and recursive DEPS.
# All OBS-specific CEF Chromium patches are in that same source revision.
$arguments=@($automation,('--download-dir='+$destination), '--branch=6533','--url=https://github.com/pkviet/cef.git',('--checkout='+$cefCommit),'--chromium-checkout=c51f2916e4a14157c276fd33c8bfb24155aac1ad','--x64-build','--no-debug-build')
if(-not $BuildRelease){$arguments += @('--no-build','--no-distrib')}
# This is a documented rebuild profile, not a recovered producer GN receipt.
# Full source and GN definitions are supplied; users may change these flags.
$oldGn=$env:GN_DEFINES;$oldDepot=$env:DEPOT_TOOLS_WIN_TOOLCHAIN
try{
 $env:GN_DEFINES='is_official_build=true'
 $env:DEPOT_TOOLS_WIN_TOOLCHAIN='0'
 & $Python @arguments
 if($LASTEXITCODE -ne 0){throw 'CEF source preparation/build failed; inspect upstream output.'}
}finally{$env:GN_DEFINES=$oldGn;$env:DEPOT_TOOLS_WIN_TOOLCHAIN=$oldDepot}
