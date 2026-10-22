param([ValidateSet('x64','x86','both')][string]$Architecture = 'both')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$zig = Join-Path $root '.tools/zig-x86_64-windows-0.14.1/zig.exe'
if (-not (Test-Path -LiteralPath $zig)) { throw 'Pinned Zig 0.14.1 is required in .tools/zig-x86_64-windows-0.14.1. See native/virtual-camera/README.md.' }
$native = Join-Path $root 'native/virtual-camera'
$up = Join-Path $native 'upstream'
$src = Join-Path $native 'src'
$out = Join-Path $native 'bin'
$project=[xml](Get-Content -LiteralPath (Join-Path $root 'src/Rilmo.Recorder.csproj') -Raw)
$productVersion=[string](@($project.Project.PropertyGroup|Where-Object Version)[0].Version)
if($productVersion -notmatch '^\d+\.\d+\.\d+$'){throw 'A three-part product version is required for native metadata.'}
$resourceTemplate=Get-Content -LiteralPath (Join-Path $src 'version.rc.in') -Raw
foreach ($entry in (Get-Content -LiteralPath (Join-Path $native 'upstream-file-manifest.json') -Raw | ConvertFrom-Json)) {
    $file=Join-Path $native $entry.path
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $entry.sha256){throw ('Pinned upstream source was changed: '+$entry.path)}
}
New-Item -ItemType Directory -Force $out | Out-Null
$include = @(); foreach ($path in @($src, (Join-Path $up 'plugins/win-dshow/virtualcam-module'), (Join-Path $up 'plugins/win-dshow'), (Join-Path $up 'shared/obs-tiny-nv12-scale'), (Join-Path $up 'deps/libdshowcapture/src'), (Join-Path $up 'libobs'))) { $include += '-I'; $include += $path }
$cpp = @((Join-Path $src 'virtualcam-filter.cpp'), (Join-Path $src 'virtualcam-module.cpp'), (Join-Path $src 'placeholder.cpp'), (Join-Path $src 'bridge.cpp'))
foreach ($name in @('output-filter.cpp','dshow-formats.cpp','dshow-media-type.cpp')) { $cpp += Join-Path $up ('deps/libdshowcapture/src/source/'+$name) }
$c = @((Join-Path $src 'shared-memory-queue.c'), (Join-Path $up 'shared/obs-tiny-nv12-scale/tiny-nv12-scale.c'), (Join-Path $up 'plugins/win-dshow/virtualcam-module/sleepto.c'))
$arches = if ($Architecture -eq 'both') { @('x64','x86') } else { @($Architecture) }
foreach ($arch in $arches) {
    $target = if ($arch -eq 'x64') { 'x86_64-windows-gnu' } else { 'x86-windows-gnu' }
    $suffix = if ($arch -eq 'x64') { '64' } else { '32' }
    $build = Join-Path $native ('build/'+$arch)
    New-Item -ItemType Directory -Force $build | Out-Null
    $common = @('-target',$target,'-O2','-DUNICODE','-D_UNICODE','-DVIRTUALCAM_AVAILABLE','-fms-extensions','-Wno-ignored-attributes','-Wno-pragma-pack','-Wno-nonportable-include-path','-Wno-inconsistent-missing-override','-include',(Join-Path $src 'mingw-compat.h')) + $include
    $objects = @()
    foreach ($file in $c) {
        $obj = Join-Path $build ([IO.Path]::GetFileNameWithoutExtension($file)+'.o')
        & $zig cc @common -c $file -o $obj
        if ($LASTEXITCODE -ne 0) { throw ('Native camera C compilation failed: '+$file) }
        $objects += $obj
    }
    $def = Join-Path $build 'camera.def'
    @('EXPORTS','DllGetClassObject','DllCanUnloadNow','DllRegisterServer','DllUnregisterServer','DllInstall','rilmo_camera_start','rilmo_camera_stop','rilmo_camera_frame_count') | Set-Content -LiteralPath $def -Encoding ascii
    $dll = Join-Path $out ('rilmo-virtual-camera'+$suffix+'.dll')
    $resourceSource=Join-Path $build 'version.rc';$resourceBinary=Join-Path $build 'version.res'
    $resourceTemplate.Replace('@VERSION_COMMAS@',($productVersion.Replace('.',',')+',0')).Replace('@VERSION@',$productVersion).Replace('@FILENAME@',[IO.Path]::GetFileName($dll))|Set-Content -LiteralPath $resourceSource -Encoding utf8
    & $zig rc /fo $resourceBinary $resourceSource
    if($LASTEXITCODE -ne 0){throw 'Camera version-resource compilation failed.'}
    & $zig c++ @common -std=c++17 -shared -static @cpp @objects $def $resourceBinary -lole32 -loleaut32 -lstrmiids -luuid -lwinmm -lshell32 -ladvapi32 -o $dll
    if ($LASTEXITCODE -ne 0) { throw ('Native camera C++ compilation failed: '+$arch) }
    Write-Output ('Built '+$dll)
    & $zig c++ @common -std=c++17 -static -municode (Join-Path $src 'smoke.cpp') @objects -lole32 -loleaut32 -lstrmiids -luuid -lwinmm -lshell32 -ladvapi32 -o (Join-Path $out ('camera-smoke'+$suffix+'.exe'))
    if ($LASTEXITCODE -ne 0) { throw ('Native camera smoke compilation failed: '+$arch) }
    & $zig c++ @common -std=c++17 -static (Join-Path $src 'filter-data.cpp') -lole32 -lstrmiids -luuid -o (Join-Path $out ('camera-filter-data'+$suffix+'.exe'))
    if ($LASTEXITCODE -ne 0) { throw ('DirectShow installer metadata helper compilation failed: '+$arch) }
}
if ($Architecture -eq 'both') {
    if (-not ('RilmoPeImports' -as [type])) { Add-Type -Path (Join-Path $root 'tools/PeImports.cs') }
    $binaries=@()
    foreach($suffix in @('64','32')) {
        $file=Join-Path $out ('rilmo-virtual-camera'+$suffix+'.dll')
        $binaries += [ordered]@{name=[IO.Path]::GetFileName($file);sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $file).Length;machine=[RilmoPeImports]::Machine($file);imports=@([RilmoPeImports]::Read($file))}
    }
    $sources=@(Get-ChildItem -LiteralPath $src -File | Sort-Object Name | ForEach-Object { [ordered]@{path=('src/'+$_.Name);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
    [ordered]@{
        schemaVersion=1;productName='Rilmo Screen Recorder';productVersion=$productVersion;deviceName='Rilmo Virtual Camera';classId='{9E7FF7BC-AA7A-4B1D-8A9E-DDDFB0692DFD}';sharedMemoryName='Local\RilmoVirtualCamVideo_9E7FF7BC_AA7A_4B1D_8A9E_DDDFB0692DFD';builtAtUtc=[DateTime]::UtcNow.ToString('o');
        upstreamObs=@{repository='https://github.com/obsproject/obs-studio';version='32.2.2';commit='ba2f32bdf791005443988a4955e963663e16b1ed'};
        libdshowcapture=@{repository='https://github.com/obsproject/libdshowcapture';commit='8878638324393815512f802640b0d5ce940161f1'};
        toolchain=@{name='Zig';version='0.14.1';url='https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip';archiveSha256='554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c';targets=@('x86_64-windows-gnu','x86-windows-gnu')};
        binaries=$binaries;modifiedAndOriginalSources=$sources;publicDistributionApproved=$false;correspondingSourceReleaseComplete=$false;registrationPerformedByBuild=$false;
        outstandingReleaseWork=@('Bundle complete corresponding source including static compiler-runtime sources/notices and exact build scripts','Review the narrow x64 constructor optimization workaround on the release compiler/OS matrix','Trusted-sign both camera PE files for the Store EXE/MSI route','Test explicit device installation, upgrade, uninstall, and real camera clients on clean Windows 10/11 machines','MSIX alone currently does not install this external DirectShow device')
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $native 'source-provenance.json') -Encoding utf8
}


