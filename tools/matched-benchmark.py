import os,json,time,subprocess,pathlib,hashlib,base64,secrets,statistics,ctypes,argparse
from ctypes import wintypes
import psutil,websocket
ROOT=pathlib.Path(__file__).resolve().parents[1]
U=ctypes.windll.user32
U.GetWindowThreadProcessId.argtypes=[wintypes.HWND,ctypes.POINTER(wintypes.DWORD)]
U.GetWindowTextW.argtypes=[wintypes.HWND,wintypes.LPWSTR,ctypes.c_int]
U.GetClassNameW.argtypes=[wintypes.HWND,wintypes.LPWSTR,ctypes.c_int]
U.PostMessageW.argtypes=[wintypes.HWND,wintypes.UINT,wintypes.WPARAM,wintypes.LPARAM]
CALLBACK=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HWND,wintypes.LPARAM)
def windows(pid):
 result=[]
 @CALLBACK
 def visit(h,p):
  value=wintypes.DWORD();U.GetWindowThreadProcessId(h,ctypes.byref(value))
  if value.value==pid:
   title=ctypes.create_unicode_buffer(4096);U.GetWindowTextW(h,title,4096)
   if title.value:result.append((int(h),title.value))
  return True
 U.EnumWindows(visit,0);return result

def wait_file(path,timeout=60):
 until=time.monotonic()+timeout
 while time.monotonic()<until:
  failure=path.parent/'failure.json'
  if failure.exists():raise RuntimeError(failure.read_text())
  if path.exists():return json.loads(path.read_text(encoding='utf-8-sig'))
  time.sleep(.1)
 raise TimeoutError(str(path))
class Obs:
 def __init__(self,port,password):
  deadline=time.monotonic()+60
  while True:
   try:self.ws=websocket.create_connection(f'ws://127.0.0.1:{port}',timeout=8);break
   except (OSError,websocket.WebSocketException):
    if time.monotonic()>deadline:raise
    time.sleep(.3)
  hello=json.loads(self.ws.recv())['d'];d={'rpcVersion':1,'eventSubscriptions':0}
  if 'authentication' in hello:
   a=hello['authentication'];secret=base64.b64encode(hashlib.sha256((password+a['salt']).encode()).digest()).decode();d['authentication']=base64.b64encode(hashlib.sha256((secret+a['challenge']).encode()).digest()).decode()
  self.ws.send(json.dumps({'op':1,'d':d}));reply=json.loads(self.ws.recv());assert reply['op']==2,reply
  deadline=time.monotonic()+45
  while True:
   try:self.call('GetVersion');break
   except RuntimeError as error:
    if '207' not in str(error) or time.monotonic()>deadline:raise
    time.sleep(.25)
 def call(self,name,data=None):
  rid=secrets.token_hex(8);self.ws.send(json.dumps({'op':6,'d':{'requestType':name,'requestId':rid,'requestData':data or {}}}))
  while True:
   reply=json.loads(self.ws.recv())
   if reply['op']==7 and reply['d']['requestId']==rid:
    value=reply['d']
    if not value['requestStatus']['result']:raise RuntimeError(name+': '+str(value['requestStatus']))
    return value.get('responseData',{})
 def wait_record(self,active):
  deadline=time.monotonic()+15
  while time.monotonic()<deadline:
   status=self.call('GetRecordStatus')
   if status['outputActive']==active:return status
   time.sleep(.1)
  raise TimeoutError('OBS recording state did not become '+str(active))
 def close(self):self.ws.close()
def snapshot(pid):
 p=psutil.Process(pid);processes=[p]+p.children(recursive=True);cpu={};rss=private=0
 for child in processes:
  try:
   t=child.cpu_times();cpu[child.pid]=t.user+t.system;m=child.memory_info();rss+=m.rss;private+=getattr(m,'private',m.vms)
  except psutil.Error:pass
 return cpu,rss,private

def sample(pid,seconds,phase,stats=None):
 rows=[];previous,_,_=snapshot(pid);last=time.monotonic();psutil.cpu_percent()
 for i in range(seconds):
  time.sleep(1);now=time.monotonic();cpu,rss,private=snapshot(pid);percent=sum(max(0,t-previous.get(p,t)) for p,t in cpu.items())/(now-last)/psutil.cpu_count()*100
  row={'second':i+1,'phase':phase,'cpuPercent':percent,'workingSetMiB':rss/1048576,'privateMiB':private/1048576,'systemCpuPercent':psutil.cpu_percent()}
  if stats:row['stats']=stats()
  rows.append(row);previous=cpu;last=now
 return rows

def summary(rows,skip):
 rows=rows[skip:];return {k:round(statistics.mean(r[k] for r in rows),3) for k in ['cpuPercent','workingSetMiB','privateMiB','systemCpuPercent']}|{'samples':len(rows),'cpuMaxPercent':round(max(r['cpuPercent'] for r in rows),3)}
def stop_owned(process):
 if process.poll() is not None:return
 for hwnd,title in windows(process.pid):
  if title.startswith(('OBS ','Rilmo Scene Performance Target')):U.PostMessageW(hwnd,0x10,0,0)
 try:process.wait(timeout=12)
 except subprocess.TimeoutExpired:
  p=psutil.Process(process.pid)
  for c in p.children(recursive=True):
   try:c.terminate()
   except psutil.Error:pass
  p.terminate();process.wait(timeout=10)
def config_obs(directory,encoder,port,password):
 app=ROOT/'artifacts/obs-benchmark-app'
 old=app/'config';saved=directory/'previous-obs-test-config'
 if old.exists():
  assert old.resolve().is_relative_to((ROOT/'artifacts').resolve()) and saved.resolve().is_relative_to((ROOT/'artifacts').resolve())
  assert not old.is_symlink() and not saved.exists()
  old.rename(saved)
 cfg=app/'config/obs-studio';profile=cfg/'basic/profiles/Benchmark';profile.mkdir(parents=True,exist_ok=True)
 (cfg/'basic/scenes').mkdir(parents=True,exist_ok=True)
 (cfg/'global.ini').write_text('[General]\nLastVersion=536870912\nEnableAutoUpdates=false\n')
 (cfg/'user.ini').write_text('[General]\nFirstRun=true\n[Basic]\nProfile=Benchmark\nProfileDir=Benchmark\nSceneCollection=Benchmark\nSceneCollectionFile=Benchmark\n[BasicWindow]\nPreviewEnabled=false\nSysTrayEnabled=true\nSysTrayWhenStarted=true\nStudioMode=false\n')
 (profile/'basic.ini').write_text(f'''[General]
Name=Benchmark
[Video]
BaseCX=1280
BaseCY=720
OutputCX=1280
OutputCY=720
FPSType=0
FPSCommon=30
ScaleType=bicubic
ColorFormat=NV12
ColorSpace=709
ColorRange=Partial
[Audio]
SampleRate=48000
ChannelSetup=Stereo
[Output]
Mode=Advanced
[AdvOut]
RecType=Standard
RecEncoder={encoder}
RecAudioEncoder=ffmpeg_aac
AudioEncoder=ffmpeg_aac
Encoder=obs_x264
RecFilePath={str(directory).replace(chr(92),'/')}
RecFormat2=mkv
RecTracks=1
RecUseRescale=false
RecRB=false
Track1Bitrate=160
[SimpleOutput]
FilePath={str(directory).replace(chr(92),'/')}
''')
 values={'keyint_sec':2,'profile':'high','rate_control':'CRF' if encoder=='obs_x264' else 'CQP','bitrate':4500,'crf':23,'cqp':23}
 if encoder=='obs_x264':values|={'preset':'ultrafast','tune':'zerolatency','x264opts':'threads=2'}
 else:values|={'bf':0,'pre_analysis':False,'lookahead':False,'preset':'speed'}
 (profile/'recordEncoder.json').write_text(json.dumps(values));(directory/'encoder-settings.json').write_text(json.dumps(values,indent=2))
 ws=cfg/'plugin_config/obs-websocket';ws.mkdir(parents=True,exist_ok=True);(ws/'config.json').write_text(json.dumps({'first_load':False,'server_enabled':True,'server_port':port,'auth_required':True,'server_password':password,'alerts_enabled':False}))
 return app

def run_trial(kind,mode,index,root,seconds):
 directory=root/f'{index:02}-{kind}-{mode}'
 if (directory/'summary.json').exists():return json.loads((directory/'summary.json').read_text())
 for completed in sorted(root.glob(directory.name+'-retry-*/summary.json')):
  return json.loads(completed.read_text())
 if directory.exists():directory=root/(directory.name+'-retry-'+time.strftime('%H%M%S'))
 directory.mkdir();env=os.environ.copy();env['DOTNET_ROOT']=str(ROOT/'.tools/dotnet')
 target=subprocess.Popen([str(ROOT/'artifacts/scene-performance/out/ScenePerformance.exe'),'--window-target'],env=env,creationflags=subprocess.CREATE_NO_WINDOW)
 process=obs=None
 try:
  deadline=time.monotonic()+15;found=[]
  while time.monotonic()<deadline:
   found=[w for w in windows(target.pid) if w[1].startswith('Rilmo Scene Performance Target')]
   if found:break
   time.sleep(.1)
  if not found:raise RuntimeError('Owned test window did not start')
  hwnd,title=found[0];class_name=ctypes.create_unicode_buffer(1024);U.GetClassNameW(hwnd,class_name,1024)
  settings={'window':':'.join(v.replace('#','#22').replace(':','#3A') for v in [title,class_name.value,'ScenePerformance.exe']),'method':2,'priority':1,'cursor':False,'client_area':True,'capture_audio':False}
  (directory/'capture-settings.json').write_text(json.dumps(settings,indent=2))
  if kind=='rilmo':
   request=directory/'request.json';request.write_text(json.dumps({'WindowHandle':hwnd,'Directory':str(directory),'Software':mode=='cpu'}))
   exe=ROOT/'artifacts/benchmark-app/Rilmo Recorder.exe';process=subprocess.Popen([str(exe),'--benchmark-ui',str(request)],cwd=exe.parent,creationflags=subprocess.CREATE_NO_WINDOW)
   idle_ready=wait_file(directory/'idle-ready.json');idle=sample(process.pid,10,'idle');(directory/'start.flag').touch();ready=wait_file(directory/'recording-ready.json');recording=sample(process.pid,seconds,'recording');(directory/'stop.flag').touch();result=wait_file(directory/'result.json');process.wait(timeout=20);output=result['output'];stats=result['stats'];startup=ready['startupMs'];encoder=ready['encoder']
  else:
   encoder='obs_x264' if mode=='cpu' else 'h264_texture_amf';port=4469;password=secrets.token_urlsafe(24);app=config_obs(directory,encoder,port,password);exe=app/'bin/64bit/obs64.exe'
   process=subprocess.Popen([str(exe),'--portable','--profile','Benchmark','--disable-updater','--disable-missing-files-check','--minimize-to-tray','--multi'],cwd=exe.parent,creationflags=subprocess.CREATE_NO_WINDOW)
   obs=Obs(port,password);version=obs.call('GetVersion');(directory/'obs-version.json').write_text(json.dumps(version,indent=2))
   # GetVersion can succeed before the frontend's first scene/audio setup has settled.
   time.sleep(5)
   for name in obs.call('GetSpecialInputs').values():
    if name:obs.call('RemoveInput',{'inputName':name})
   special=obs.call('GetSpecialInputs');assert not any(special.values()),special
   scene='Owned benchmark '+root.name+'-'+str(index);obs.call('CreateScene',{'sceneName':scene});response=obs.call('CreateInput',{'sceneName':scene,'inputName':'Owned motion '+root.name+'-'+str(index),'inputKind':'window_capture','inputSettings':settings,'sceneItemEnabled':True});obs.call('SetCurrentProgramScene',{'sceneName':scene});obs.call('SetSceneItemTransform',{'sceneName':scene,'sceneItemId':response['sceneItemId'],'sceneItemTransform':{'positionX':0,'positionY':0,'boundsType':'OBS_BOUNDS_SCALE_INNER','boundsWidth':1280,'boundsHeight':720,'alignment':5}})
   for other in obs.call('GetSceneList')['scenes']:
    if other['sceneName']!=scene:obs.call('RemoveScene',{'sceneName':other['sceneName']})
   for other in obs.call('GetInputList')['inputs']:
    if other['inputName']!='Owned motion '+root.name+'-'+str(index):obs.call('RemoveInput',{'inputName':other['inputName']})
   time.sleep(.5)
   actual_inputs=obs.call('GetInputList')['inputs'];assert len(actual_inputs)==1,actual_inputs
   video=obs.call('GetVideoSettings');assert video['baseWidth']==1280 and video['outputWidth']==1280 and video['fpsNumerator']==30,video
   (directory/'video-settings.json').write_text(json.dumps(video,indent=2));(directory/'actual-capture-settings.json').write_text(json.dumps(obs.call('GetInputSettings',{'inputName':'Owned motion '+root.name+'-'+str(index)}),indent=2))
   idle=sample(process.pid,10,'idle');start=time.monotonic();obs.call('StartRecord');status=obs.wait_record(True);startup=(time.monotonic()-start)*1000;recording=sample(process.pid,seconds,'recording',lambda:obs.call('GetStats'));stats=obs.call('GetStats');stop=obs.call('StopRecord');output=stop['outputPath'];obs.wait_record(False);obs.close();obs=None;stop_owned(process)
  expected='obs_x264' if mode=='cpu' else 'h264_texture_amf';assert encoder==expected,encoder
  result={'app':kind,'mode':mode,'encoder':encoder,'secondsRequested':seconds,'idle':summary(idle,2),'recording':summary(recording,5),'startupMs':round(startup,1),'stats':stats,'output':output,'outputBytes':pathlib.Path(output).stat().st_size,'scope':'Frontend plus all child processes; preview/floating controls off, window minimized, no audio input, WGC client-area synthetic moving window. CPU normalized over all logical cores. Shared working-set pages can be counted twice.'}
  (directory/'samples.json').write_text(json.dumps(idle+recording,indent=2));(directory/'summary.json').write_text(json.dumps(result,indent=2));print(json.dumps(result),flush=True);return result
 finally:
  if obs:
   try:
    if obs.call('GetRecordStatus')['outputActive']:obs.call('StopRecord')
    obs.close()
   except Exception:pass
  if process:stop_owned(process)
  stop_owned(target)

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--pilot',action='store_true');parser.add_argument('--seconds',type=int,default=45);parser.add_argument('--resume');args=parser.parse_args();root=pathlib.Path(args.resume) if args.resume else ROOT/'artifacts'/('matched-benchmark-'+time.strftime('%Y%m%d-%H%M%S'));root.mkdir(exist_ok=True);(ROOT/'artifacts/latest-benchmark.txt').write_text(str(root))
 trials=[('obs','gpu')] if args.pilot else [('rilmo','gpu'),('obs','gpu'),('obs','gpu'),('rilmo','gpu'),('rilmo','cpu'),('obs','cpu')]
 result=[]
 try:
  for i,(app,mode) in enumerate(trials):print(f'RUN {i+1}/{len(trials)} {app} {mode}',flush=True);result.append(run_trial(app,mode,i,root,args.seconds))
  (root/'results.json').write_text(json.dumps({'logicalCores':psutil.cpu_count(),'memoryBytes':psutil.virtual_memory().total,'trials':result},indent=2))
 except Exception as error:(root/'failure.txt').write_text(repr(error));raise
 print('BENCHMARK='+str(root),flush=True)
if __name__=='__main__':main()


