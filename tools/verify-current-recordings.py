"""Decode every stream in this release's owned test recordings, using FFmpeg."""
import json,pathlib,subprocess,re,hashlib
import imageio_ffmpeg
root=pathlib.Path(__file__).resolve().parents[1]
benchmark=root/'artifacts/matched-benchmark-20261022-070302'
feature=root/'artifacts/feature-expansion/results-20261022-071553'
ui=root/'artifacts/ui-smoke/results-20261022-021800-234'
files=[pathlib.Path(t['output']) for t in json.loads((benchmark/'results.json').read_text())['trials']]
files+=list(feature.glob('*.mkv'))+list(ui.rglob('*.mkv'))+list(ui.rglob('*.mp4'))
ffmpeg=imageio_ffmpeg.get_ffmpeg_exe();results=[]
for file in dict.fromkeys(files):
    check=subprocess.run([ffmpeg,'-hide_banner','-xerror','-threads','2','-i',str(file),'-map','0','-f','null','NUL'],capture_output=True,text=True,timeout=90)
    log=file.with_suffix(file.suffix+'.decode.txt');log.write_text(check.stderr,encoding='utf-8')
    audio=len(re.findall(r'Stream #0:\d+.*Audio:',check.stderr.split('Stream mapping:')[0]))
    frames=re.findall(r'frame=\s*(\d+)',check.stderr)
    if check.returncode or not frames or int(frames[-1])<1:raise RuntimeError(f'Decode failed: {file}\n{check.stderr}')
    if file.parent==feature and audio!=3:raise RuntimeError(f'Expected mixed/computer/microphone tracks: {file}: {audio}')
    item={'file':str(file),'passed':True,'decodedFrames':int(frames[-1]),'audioTracks':audio,'sha256':hashlib.sha256(file.read_bytes()).hexdigest()}
    if file in [pathlib.Path(t['output']) for t in json.loads((benchmark/'results.json').read_text())['trials']]:
        sample=subprocess.run([ffmpeg,'-v','error','-ss','3','-i',str(file),'-t','2','-vf','fps=2,scale=32:18','-pix_fmt','rgb24','-f','rawvideo','pipe:1'],capture_output=True,timeout=30,check=True).stdout
        chunks=[sample[i:i+1728] for i in range(0,len(sample),1728)]
        if not chunks or max(sample)-min(sample)<50 or len(set(chunks))<2:raise RuntimeError('Blank or static benchmark video: '+str(file))
        item['movingNonblankContent']=True
    results.append(item);print(file.name,':',item['decodedFrames'],'frames,',audio,'audio tracks',flush=True)
(root/'artifacts/recording-validation-0.4.json').write_text(json.dumps({'passed':True,'files':results},indent=2))
