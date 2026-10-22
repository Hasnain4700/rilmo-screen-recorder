param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$work=Join-Path $root ('artifacts/virtual-camera-engine/'+[Guid]::NewGuid().ToString('N'))
$out=Join-Path $work 'out'
New-Item -ItemType Directory -Force $out | Out-Null
$enginePath=(Join-Path $root 'src/Engine/*.cs').Replace('&','&amp;')
$project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings></PropertyGroup><ItemGroup><Compile Include="'+$enginePath+'" Exclude="'+(Join-Path $root 'src/Engine/MuxRelay.cs')+'"/><Compile Include="Smoke.cs"/></ItemGroup></Project>'
Set-Content -LiteralPath (Join-Path $work 'Smoke.csproj') -Value $project -Encoding utf8
$source=@'
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Rilmo.Engine;
class Smoke {
 [STAThread] static int Main(string[] args) {
  try {
   var options=new RecorderOptions { Width=1280,Height=720,Fps=30,SystemAudio=false,Microphone=false,OutputDirectory=Path.Combine(args[0],"unused-recordings") };
   using(var engine=new RecorderEngine()) {
    engine.StartSession(options);
    Console.WriteLine("SESSION_ACTIVE="+engine.IsSessionActive);
    for(int cycle=0;cycle<2;cycle++) {
     int started=NativeObs.rilmo_camera_start(NativeObs.obs_get_video(),1280,720,30,1);
     if(started!=0)throw new Exception("Native producer start: "+started);
     try {
      if(NativeObs.rilmo_camera_start(NativeObs.obs_get_video(),1280,720,30,1)!=2)throw new Exception("Duplicate producer not rejected");
      Thread.Sleep(300);
      string bits=cycle==0?"64":"32";
      var start=new ProcessStartInfo(Path.Combine(args[1],"camera-smoke"+bits+".exe"),"\""+Path.Combine(args[1],"rilmo-virtual-camera"+bits+".dll")+"\" --consume-only") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
      using(var child=Process.Start(start)) {
       string output=child.StandardOutput.ReadToEnd(); string error=child.StandardError.ReadToEnd();
       if(!child.WaitForExit(15000)) {child.Kill();throw new Exception("Consumer timeout");}
       Console.WriteLine("CONSUMER_"+bits+"="+output.Replace(Environment.NewLine,"; "));
       if(child.ExitCode!=0)throw new Exception("Consumer failed: "+child.ExitCode+" "+error);
      }
      ulong frames=NativeObs.rilmo_camera_frame_count();Console.WriteLine("NATIVE_PRODUCER_FRAMES="+frames);
      if(frames<20)throw new Exception("Producer did not receive live libobs frames");
     } finally {if(NativeObs.rilmo_camera_stop()!=0)throw new Exception("Producer did not disconnect");}
     if(NativeObs.rilmo_camera_stop()!=0)throw new Exception("Idempotent stop failed");
     if(!engine.IsSessionActive||engine.IsRecording)throw new Exception("Camera changed recording lifecycle");
    }
    engine.Stop();
    Console.WriteLine("LIFECYCLE=passed; LOG="+engine.LogPath);
   }
   return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
'@
Set-Content -LiteralPath (Join-Path $work 'Smoke.cs') -Value $source -Encoding utf8
$dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
& $dotnet build (Join-Path $work 'Smoke.csproj') -c Release -o $out
if($LASTEXITCODE -ne 0){throw 'Virtual camera smoke build failed.'}
& (Join-Path $root 'tools/stage-runtime.ps1') -AppDirectory $out
Copy-Item -LiteralPath (Join-Path $root 'native/virtual-camera/bin/rilmo-virtual-camera64.dll') -Destination $out
$oldDotnetRoot=$env:DOTNET_ROOT
try {
 $env:DOTNET_ROOT=Split-Path $dotnet -Parent
 & (Join-Path $out 'Smoke.exe') $work (Join-Path $root 'native/virtual-camera/bin') 2>&1 | Tee-Object -FilePath (Join-Path $work 'result.log')
 if($LASTEXITCODE -ne 0){throw 'Virtual camera native producer/registration-free consumer test failed.'}
}finally{$env:DOTNET_ROOT=$oldDotnetRoot}
Write-Output ('Virtual camera tested without registration: '+$work)
