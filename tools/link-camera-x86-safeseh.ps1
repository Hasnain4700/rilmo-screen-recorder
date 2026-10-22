# SPDX-License-Identifier: GPL-2.0-or-later
param([Parameter(Mandatory=$true)][string]$LinkTracePath,[Parameter(Mandatory=$true)][string]$OutputDll)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$native=Join-Path $root 'native/virtual-camera'
$build=Join-Path $native 'build/x86'
$zigRoot=Join-Path $root '.tools/zig-x86_64-windows-0.14.1'
$zig=Join-Path $zigRoot 'zig.exe'
$unwind=Join-Path $zigRoot 'lib/libunwind'
$pinned=@{
 'src/UnwindRegistersSave.S'='5d4d94842641d34a0213947bc33c69c2283ca4bf54d70673c41ce77303bb68ad'
 'src/UnwindRegistersRestore.S'='83a0e71b5a1ea8c90be705882c16b8fbc5b2e2c9866566988dd1094abcacd183'
 'src/assembly.h'='c3d765a0642dd4bc59e2f92ab16be2e9192432fcb834b8b313a69eb9165e699a'
 'include/__libunwind_config.h'='5e963e752482bf8cefcabbb6b4f3f09ae3bff247f59b7a2f1d8fcabbe539c18b'
}
foreach($entry in $pinned.GetEnumerator()){
 if((Get-FileHash -LiteralPath (Join-Path $unwind $entry.Key) -Algorithm SHA256).Hash -ine $entry.Value){throw ('Unreviewed LLVM assembly input: '+$entry.Key)}
}
$environment=& $zig env|ConvertFrom-Json
if($environment.version -ne '0.14.1'){throw 'SafeSEH helper requires the reviewed Zig version.'}
$trace=Get-Content -LiteralPath $LinkTracePath -Raw
$linkLine=@($trace -split "`n"|Where-Object {$_ -like 'lld-link *'})
if($linkLine.Count -ne 1 -or $linkLine[0] -notmatch '-MACHINE:X86' -or $linkLine[0] -notmatch '-DLL'){throw 'Unexpected compiler linker trace.'}
$cachePattern=[regex]::Escape($environment.global_cache_dir.TrimEnd('\'))+'\\o\\[a-f0-9]{32}\\[^\s]+'
$cacheInputs=@([regex]::Matches($linkLine[0],$cachePattern)|ForEach-Object{$_.Value})
if($cacheInputs.Count -lt 20 -or @($cacheInputs|Where-Object {$_ -like '*\unwind.lib'}).Count -ne 1){throw 'Incomplete compiler runtime linker inputs.'}
foreach($file in $cacheInputs){if(-not(Test-Path -LiteralPath $file -PathType Leaf) -or [IO.Path]::GetExtension($file) -notin @('.obj','.lib')){throw 'Unexpected cached linker input.'}}
$template=Get-Content -LiteralPath (Join-Path $native 'src/unwind-safeseh.S.in') -Raw
$overrides=@()
foreach($kind in @('Save','Restore')){
 $assembly=Join-Path $build ('safe-UnwindRegisters'+$kind+'.S')
 $object=Join-Path $build ('safe-UnwindRegisters'+$kind+'.obj')
 $template.Replace('@UNWIND_FILE@',('UnwindRegisters'+$kind+'.S'))|Set-Content -LiteralPath $assembly -Encoding utf8
 & $zig cc -target x86-windows-gnu -O2 -I (Join-Path $unwind 'src') -I (Join-Path $unwind 'include') -D_LIBUNWIND_IS_NATIVE_ONLY -D_LIBUNWIND_HIDE_SYMBOLS -c $assembly -o $object
 if($LASTEXITCODE -ne 0){throw 'SafeSEH register assembly failed.'}
 $overrides+=$object
}
$inputs=@('shared-memory-queue.o','tiny-nv12-scale.o','sleepto.o','version.res')|ForEach-Object{Join-Path $build $_}
# /SAFESEH validates all linked objects. LLD itself emits NO_SEH only when no
# SEH handlers exist. Never use /SAFESEH:NO, /NOSEH, or edit PE flags to pass QA.
$options=@('-lldmingw','-ERRORLIMIT:0','-NOLOGO','-MACHINE:X86','-DLL','-BREPRO','-DYNAMICBASE','-NXCOMPAT','-BASE:268435456','-SAFESEH',('-OUT:'+$OutputDll),('-DEF:'+(Join-Path $build 'camera.def')),'-ALTERNATENAME:__image_base__=___ImageBase','-ALTERNATENAME:__DllMainCRTStartup@12=_DllMainCRTStartup@12')
& $zig lld-link @options @inputs @overrides @cacheInputs
if($LASTEXITCODE -ne 0){throw 'Camera SafeSEH validation/link failed.'}
$bytes=[IO.File]::ReadAllBytes($OutputDll);$pe=[BitConverter]::ToInt32($bytes,60)
$flags=[BitConverter]::ToUInt16($bytes,($pe+24+70))
if(-not($flags -band 0x400) -or -not($flags -band 0x100) -or -not($flags -band 0x40)){throw 'Expected linker-generated no-handler, DEP and ASLR metadata is absent.'}
Write-Output 'Linked x86 camera with /SAFESEH; linker confirmed no Windows SEH handlers, with DEP and ASLR.'
