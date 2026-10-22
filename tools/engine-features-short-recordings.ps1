param([Parameter(Mandatory=$true)][string]$Decoder,[Parameter(Mandatory=$true)][string]$Probe)
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
$project=Join-Path $workspace 'artifacts\short-recordings'
$output=Join-Path $project 'out'
$results=Join-Path $project ('results-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$dotnet=Join-Path $workspace '.tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $Decoder) -or !(Test-Path -LiteralPath $Probe)){throw 'Supply FFmpeg and FFprobe executables for complete output validation.'}
New-Item -ItemType Directory -Force -Path $project,$output,$results|Out-Null
$projectSource=@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWindowsForms>true</UseWindowsForms><PlatformTarget>x64</PlatformTarget><RuntimeIdentifier>win-x64</RuntimeIdentifier><EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings><AssemblyName>ShortRecordings</AssemblyName></PropertyGroup><ItemGroup><Compile Include="..\..\src\Engine\*.cs" Exclude="..\..\src\Engine\MuxRelay.cs"/><Compile Include="ShortRecordings.cs"/></ItemGroup></Project>
'@
Set-Content -LiteralPath (Join-Path $project 'ShortRecordings.csproj') -Value $projectSource -Encoding UTF8
$source=@'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using Rilmo.Engine;
internal static class ShortRecordings
{
    static void Assert(bool okay,string message){if(!okay)throw new Exception(message);}
    [STAThread] static int Main(string[] args)
    {
        if(RecorderHost.IsHostRequest(args))return RecorderHost.Run(args);
        try
        {
            var results=new List<object>();
            foreach(string preference in new[]{"auto","software"})
            using(var recorder=new RecorderClient())
            {
                var scene=new SceneDefinition {Id="solid",Name="Solid color"};
                scene.Sources.Add(new SceneSource {Id="color",Name="Color",Kind=SceneSourceKind.Color,Color=0xFF2560C0,Transform=new SourceTransform {Width=640,Height=360,Visible=true}});
                var collection=new SceneCollection {CanvasWidth=640,CanvasHeight=360,SelectedSceneId=scene.Id};collection.Scenes.Add(scene);
                recorder.StartSession(new RecorderOptions {Width=640,Height=360,Fps=30,SystemAudio=false,Microphone=false,OutputDirectory=args[0],EncoderPreference=preference,SceneCollection=collection});
                recorder.StartReplayBuffer(new ReplayOptions {Seconds=5,MaximumMegabytes=32,OutputDirectory=args[0]});
                string encoder=recorder.EncoderId;
                foreach(int duration in new[]{0,1000,0})
                {
                    // Deliberately join between keyframes instead of starting a
                    // fresh encoder for each tiny recording.
                    Thread.Sleep(275);
                    var start=Stopwatch.StartNew();recorder.StartRecording();start.Stop();
                    var ready=recorder.GetStats();
                    Assert(ready.TotalFrames>=2,"Start returned before encoded video was present.");
                    Thread.Sleep(duration);
                    var stop=Stopwatch.StartNew();recorder.StopRecording();stop.Stop();
                    Assert(!recorder.IsRecording&&recorder.IsReplayBufferActive&&recorder.IsSessionActive,"Short recording stop interrupted replay capture.");
                    Assert(recorder.EncoderId==encoder,"Shared output unexpectedly replaced the active encoder.");
                    var final=recorder.GetStats();
                    Assert(final.TotalFrames>=2&&new FileInfo(recorder.OutputPath).Length>0,"Short recording has no encoded media.");
                    results.Add(new{preference,encoder,requestedRecordingMilliseconds=duration,startMilliseconds=start.ElapsedMilliseconds,stopMilliseconds=stop.ElapsedMilliseconds,framesAtStart=ready.TotalFrames,finalFrames=final.TotalFrames,path=recorder.OutputPath,bytes=new FileInfo(recorder.OutputPath).Length,replayPreserved=true});
                }
                string replay=recorder.SaveReplay();Assert(File.Exists(replay),"Replay could not be saved after short recordings.");
                recorder.Stop();Assert(!recorder.IsSessionActive,"Capture remained active after Stop.");
            }
            File.WriteAllText(Path.Combine(args[0],"result.json"),JsonSerializer.Serialize(new{passed=true,cases=results},new JsonSerializerOptions {WriteIndented=true}));
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
'@
Set-Content -LiteralPath (Join-Path $project 'ShortRecordings.cs') -Value $source -Encoding UTF8
& $dotnet build (Join-Path $project 'ShortRecordings.csproj') -c Release -o $output --nologo
if($LASTEXITCODE -ne 0){throw 'Short-recording harness compilation failed.'}
& (Join-Path $PSScriptRoot 'stage-runtime.ps1') -AppDirectory $output
$previousDotnetRoot=$env:DOTNET_ROOT
try
{
    $env:DOTNET_ROOT=Split-Path -Parent $dotnet
    $process=Start-Process -FilePath (Join-Path $output 'ShortRecordings.exe') -ArgumentList ('"'+$results+'"') -WorkingDirectory $output -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $results 'stdout.txt') -RedirectStandardError (Join-Path $results 'stderr.txt')
    if(!$process.WaitForExit(120000)){$process.Kill($true);throw 'Owned short-recording test exceeded two minutes.'}
    if($process.ExitCode -ne 0){throw ('Short-recording test failed: '+(Get-Content -LiteralPath (Join-Path $results 'stderr.txt') -Raw))}
}
finally{$env:DOTNET_ROOT=$previousDotnetRoot}
$decoded=@()
foreach($clip in Get-ChildItem -LiteralPath $results -File -Filter '*.mkv')
{
    $probeText=& $Probe -v error -count_frames -show_entries 'stream=codec_name,nb_read_frames,width,height:format=duration,size' -of json $clip.FullName
    if($LASTEXITCODE -ne 0){throw ('Probe failed: '+$clip.FullName)}
    $metadata=$probeText|ConvertFrom-Json
    $video=$metadata.streams|Where-Object { $_.codec_name -eq 'h264' }|Select-Object -First 1
    if(!$video -or [int]$video.nb_read_frames -lt 2){throw ('Encoded video missing: '+$clip.FullName)}
    $errors=& $Decoder -v error -xerror -i $clip.FullName -map '0:v:0' -map '0:a?' -f null NUL 2>&1
    if($LASTEXITCODE -ne 0){throw ('Decode failed: '+$clip.FullName+' '+($errors -join ' '))}
    $decoded+=[pscustomobject]@{path=$clip.FullName;bytes=$clip.Length;duration=$metadata.format.duration;decodedFrames=[int]$video.nb_read_frames;fullDecodePassed=$true}
}
$decoded|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $results 'decode-validation.json') -Encoding UTF8
Get-Content -LiteralPath (Join-Path $results 'result.json')
Write-Output ('FULL_DECODE_PASSED='+$decoded.Count+'; RESULTS='+$results)
