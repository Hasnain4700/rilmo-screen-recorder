# SPDX-License-Identifier: GPL-2.0-or-later
# Compare assembled register instructions with the pinned compiler originals.
param([Parameter(Mandatory=$true)][string]$OriginalObjectsDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
function CodeDigest([string]$Path){
 $bytes=[IO.File]::ReadAllBytes($Path)
 if($bytes.Length -lt 20 -or [BitConverter]::ToUInt16($bytes,0) -ne 0x14c){throw 'Expected an i386 COFF object.'}
 $count=[BitConverter]::ToUInt16($bytes,2);$start=20+[BitConverter]::ToUInt16($bytes,16)
 $code=[IO.MemoryStream]::new()
 try{
  for($index=0;$index -lt $count;$index++){
   $section=$start+$index*40;$name=[Text.Encoding]::ASCII.GetString($bytes,$section,8).TrimEnd([char]0)
   if($name -eq '.text'){$length=[BitConverter]::ToUInt32($bytes,$section+16);$offset=[BitConverter]::ToUInt32($bytes,$section+20);if($length -eq 0 -or $offset+$length -gt $bytes.Length){throw 'Invalid object code section.'};$code.Write($bytes,$offset,$length)}
  }
  if($code.Length -eq 0){throw 'No assembled code found.'}
  $sha=[Security.Cryptography.SHA256]::Create()
  try{return [BitConverter]::ToString($sha.ComputeHash($code.ToArray())).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
 }finally{$code.Dispose()}
}
$rows=@(foreach($kind in @('Save','Restore')){
 $original=Join-Path $OriginalObjectsDirectory ('UnwindRegisters'+$kind+'.obj')
 $annotated=Join-Path $root ('native/virtual-camera/build/x86/safe-UnwindRegisters'+$kind+'.obj')
 $expected=CodeDigest $original;$observed=CodeDigest $annotated
 if($expected -cne $observed){throw ('SafeSEH annotation changed original register instructions: '+$kind)}
 [ordered]@{object=('UnwindRegisters'+$kind);originalCodeSha256=$expected;annotatedCodeSha256=$observed;codeIdentical=$true}
})
$dll=Join-Path $root 'native/virtual-camera/bin/rilmo-virtual-camera32.dll'
$bytes=[IO.File]::ReadAllBytes($dll);$pe=[BitConverter]::ToInt32($bytes,60);$flags=[BitConverter]::ToUInt16($bytes,$pe+24+70)
if(-not($flags-band 0x400) -or -not($flags-band 0x100) -or -not($flags-band 0x40)){throw 'Expected linker-produced protection flags are absent.'}
[ordered]@{schemaVersion=1;dllSha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant();linkerSafeSehRequired=$true;linkerFoundNoWindowsSehHandlers=$true;dep=$true;aslr=$true;objects=$rows;wackRetested=$false}|ConvertTo-Json -Depth 5
