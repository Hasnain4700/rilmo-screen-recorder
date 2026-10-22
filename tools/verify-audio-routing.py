import array,json,math,pathlib,subprocess
import imageio_ffmpeg
root=pathlib.Path(__file__).resolve().parents[1]
directory=sorted((root/'artifacts/audio-routing').glob('results-*'))[-1]
result=json.loads((directory/'result.json').read_text());ffmpeg=imageio_ffmpeg.get_ffmpeg_exe();file=result['file']
decode=subprocess.run([ffmpeg,'-hide_banner','-xerror','-i',file,'-map','0','-f','null','NUL'],capture_output=True,text=True,timeout=60)
(directory/'decode.txt').write_text(decode.stderr)
if decode.returncode:raise RuntimeError(decode.stderr)
tracks=[]
for track in range(3):
    data=subprocess.run([ffmpeg,'-v','error','-ss','1','-i',file,'-map',f'0:a:{track}','-t','1','-ar','48000','-ac','1','-f','f32le','pipe:1'],capture_output=True,check=True,timeout=30).stdout
    samples=array.array('f');samples.frombytes(data)
    if len(samples)<47000:raise RuntimeError('Audio sample too short')
    amplitudes=[]
    for frequency in [440,880]:
        re=sum(v*math.cos(2*math.pi*frequency*i/48000) for i,v in enumerate(samples))
        im=sum(v*math.sin(2*math.pi*frequency*i/48000) for i,v in enumerate(samples))
        amplitudes.append(2*math.hypot(re,im)/len(samples))
    tracks.append({'track':track+1,'amplitude440Hz':amplitudes[0],'amplitude880Hz':amplitudes[1]})
if min(tracks[0]['amplitude440Hz'],tracks[0]['amplitude880Hz'])<.01:raise RuntimeError('Mixed track lost a tone')
if tracks[1]['amplitude440Hz']<.01 or tracks[1]['amplitude880Hz']>tracks[1]['amplitude440Hz']/100:raise RuntimeError('Computer track not isolated')
if tracks[2]['amplitude880Hz']<.01 or tracks[2]['amplitude440Hz']>tracks[2]['amplitude880Hz']/100:raise RuntimeError('Microphone track not isolated')
result['frequencyAnalysisPassed']=True;result['tracks']=tracks
(directory/'verified.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
