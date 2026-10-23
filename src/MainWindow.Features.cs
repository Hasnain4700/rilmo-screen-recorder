// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rilmo.Engine;
using Forms = System.Windows.Forms;

namespace Rilmo
{
    public partial class MainWindow
    {
        private QuickDock quickDock;
        private bool streaming, replaying, exitRequested, quickOperation, screenshotBusy, cleanupPending;
        private long captureRevision;
        private int sessionWidth, sessionHeight, sessionFps;
        private bool loadingCameras;
        private readonly System.Windows.Threading.DispatcherTimer audioUpdateTimer = new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(120) };
        private bool audioUpdatePending;
        private bool HasActiveSession => recording || streaming || replaying || virtualCamera || cleanupPending;
        private void InitializeFeatures()
        {
            FloatingToggle.IsChecked = settings.FloatingControls;
            CompressorToggle.IsChecked=settings.AudioCompressor;MonitorMicrophoneToggle.IsChecked=settings.MicrophoneMonitor;SeparateTracksToggle.IsChecked=settings.SeparateAudioTracks;
            SystemSyncBox.Text=settings.SystemSyncOffsetMs.ToString(CultureInfo.InvariantCulture);MicrophoneSyncBox.Text=settings.MicrophoneSyncOffsetMs.ToString(CultureInfo.InvariantCulture);
            SystemVolumeSlider.Value = settings.SystemVolume; MicrophoneVolumeSlider.Value = settings.MicrophoneVolume;
            NoiseSuppressionToggle.IsChecked = settings.NoiseSuppression; NoiseGateToggle.IsChecked = settings.NoiseGate; LimiterToggle.IsChecked = settings.AudioLimiter;
            CameraToggle.IsChecked = settings.CameraEnabled; CameraPositionPicker.SelectedIndex = settings.CameraPosition;
            TitleOverlayBox.Text = settings.OverlayText ?? ""; LogoPathBox.Text = settings.LogoPath ?? "";
            OverlayPositionPicker.SelectedIndex = settings.OverlayPosition; ScenePresetPicker.SelectedIndex = settings.ScenePreset;
            ReplayToggle.IsChecked = settings.ReplayWithRecording; ReplaySecondsBox.Text = settings.ReplaySeconds.ToString(CultureInfo.InvariantCulture);
            LiveServerBox.Text = settings.StreamServer ?? ""; StreamBitrateBox.Text = settings.StreamBitrate.ToString(CultureInfo.InvariantCulture);
            audioUpdateTimer.Tick += async (s,e)=>{ audioUpdateTimer.Stop(); await ApplyAudioLevels(); };
            Closed += (s,e)=>audioUpdateTimer.Stop();
            InitializeScenes(); RefreshVirtualCamera();
        }
        private void SaveFeatureSettings()
        {
            settings.FloatingControls = FloatingToggle.IsChecked == true;
            settings.AudioCompressor=CompressorToggle.IsChecked==true;settings.MicrophoneMonitor=MonitorMicrophoneToggle.IsChecked==true;settings.SeparateAudioTracks=SeparateTracksToggle.IsChecked==true;
            settings.SystemVolume = SystemVolumeSlider.Value; settings.MicrophoneVolume = MicrophoneVolumeSlider.Value;
            settings.NoiseSuppression = NoiseSuppressionToggle.IsChecked == true; settings.NoiseGate = NoiseGateToggle.IsChecked == true; settings.AudioLimiter = LimiterToggle.IsChecked == true;
            settings.CameraEnabled = CameraToggle.IsChecked == true;
            if (CameraPicker.SelectedItem is CameraDevice camera) settings.CameraId = camera.Id;
            settings.CameraPosition = Math.Max(0, CameraPositionPicker.SelectedIndex);
            settings.OverlayText = TitleOverlayBox.Text; settings.LogoPath = LogoPathBox.Text;
            settings.OverlayPosition = Math.Max(0, OverlayPositionPicker.SelectedIndex); settings.ScenePreset = Math.Max(0, ScenePresetPicker.SelectedIndex);
            settings.ReplayWithRecording = ReplayToggle.IsChecked == true;
            if (int.TryParse(ReplaySecondsBox.Text, out int seconds)) settings.ReplaySeconds = Math.Clamp(seconds,5,120);
            // Stream keys deliberately remain in the PasswordBox only; never serialized.
            if (Uri.TryCreate(LiveServerBox.Text.Trim(), UriKind.Absolute, out Uri uri) && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)) settings.StreamServer = uri.AbsoluteUri;
            if (int.TryParse(StreamBitrateBox.Text, out int rate)) settings.StreamBitrate = Math.Clamp(rate,500,20000);
        }
        private async void LoadCameraDevices()
        {
            if(loadingCameras || HasActiveSession) return;
            loadingCameras=true; CameraPicker.IsEnabled=false;
            try
            {
                var cameras=(await Task.Run(CameraDeviceCatalog.Enumerate)).ToList();
                if(!string.IsNullOrEmpty(settings.CameraId) && !cameras.Any(c=>c.Id==settings.CameraId)) cameras.Add(new CameraDevice(settings.CameraId,"Unavailable camera · select another",false));
                CameraPicker.DisplayMemberPath="Name"; CameraPicker.ItemsSource=cameras;
                CameraPicker.SelectedItem=cameras.FirstOrDefault(c=>c.Id==settings.CameraId)??cameras.FirstOrDefault();
                if(cameras.Count==0) CameraPicker.ToolTip="No camera detected. Connect one and reopen Studio to refresh.";
            }
            catch(Exception ex) { StatusLabel.Text="Camera discovery failed: " + ex.Message; }
            finally { loadingCameras=false;CameraPicker.IsEnabled=true; }
        }
        private void EnsureQuickDock()
        {
            if (renderOnly || automatedTest) return;
            if (quickDock == null)
            {
                quickDock = new QuickDock(QuickAction, (x,y) => { settings.FloatingX=x; settings.FloatingY=y; try { settings.Save(); } catch (Exception ex) { App.WriteError(ex); } }, settings.FloatingX, settings.FloatingY);
            }
            if (settings.FloatingControls) { quickDock.Show(); quickDock.KeepOnScreen(false); UpdateQuickState(); }
            else quickDock.Hide();
        }
        private void UpdateQuickState()
        {
            Motion.CaptureActive=HasActiveSession;
            if (quickDock != null) quickDock.UpdateState(recording, paused, streaming, replaying, busy || screenshotBusy || exporting, ElapsedLabel.Text,cleanupPending,virtualCamera);
        }
        private void FloatingToggle_Changed(object sender, RoutedEventArgs e) { if (!ready) return; settings.FloatingControls = FloatingToggle.IsChecked == true; EnsureQuickDock(); UpdateOptions(); }
        private void ShowQuick_Click(object sender, RoutedEventArgs e) { FloatingToggle.IsChecked = true; EnsureQuickDock(); if (quickDock != null) quickDock.OpenMenu(); }
        private async void QuickAction(string command)
        {
            if (screenshotBusy || closeAllowed) return;
            if (command == "open") { ShowWindow(); Navigate("record"); return; }
            if (command == "settings") { ShowWindow(); Navigate("settings"); return; }
            if (command == "studio") {ShowWindow();Navigate("studio");return;}
            if (command == "hide") { FloatingToggle.IsChecked = false; return; }
            if (busy || exporting || screenshotBusy) return;
            quickOperation = true;
            try
            {
                if(command=="camera"){if(!AppEdition.OffersVirtualCamera)return;if(virtualCamera)await ToggleVirtualCamera();else{ShowWindow();Navigate("studio");UpdateLayout();VirtualCameraCard.BringIntoView();}}
                else if (command == "record") { if (cleanupPending) await RetryCaptureCleanup(); else if (recording) await StopRecording(); else await StartRecording(); }
                else if (command == "pause") await ChangePause();
                else if (command == "live")
                {
                    if (streaming) await ToggleLive();
                    else { ShowWindow(); Navigate("live"); StatusLabel.Text = "Choose your destination and press Go live. Streaming never starts just by opening this menu."; }
                }
                else if (command == "screenshot") await TakeScreenshot();
                else if (command == "replay") { if (replaying) await SaveCurrentReplay(); else { ShowWindow(); Navigate("studio"); UpdateLayout(); ReplayControlsCard.BringIntoView(); StatusLabel.Text = "Set the replay length, then enable the replay buffer."; } }
            }
            finally { quickOperation = false; UpdateQuickState(); }
        }
        private async void Screenshot_Click(object sender, RoutedEventArgs e) { await TakeScreenshot(); }
        private async Task TakeScreenshot()
        {
            if (busy || exporting || screenshotBusy) return;
            bool wasVisible = IsVisible, dockVisible = quickDock != null && quickDock.IsVisible, failed=false;
            screenshotBusy = true; UpdateQuickState();
            try
            {
                var display = Forms.Screen.FromPoint(Forms.Cursor.Position);
                Hide(); if (quickDock != null) quickDock.Hide();
                await Task.Delay(180);
                var picker = new RegionPicker(display.Bounds, 2, 2);
                if (picker.ShowDialog() != true) return;
                var region = picker.Selection;
                var bounds = new System.Drawing.Rectangle(display.Bounds.X + region.X, display.Bounds.Y + region.Y, region.Width, region.Height);
                await Task.Delay(100);
                string saved = await ScreenshotCapture.SaveAsync(bounds, settings.OutputDirectory);
                StatusLabel.Text = "Screenshot saved · " + Path.GetFileName(saved);
                if (tray != null) { tray.BalloonTipTitle = "Screenshot saved"; tray.BalloonTipText = Path.GetFileName(saved); tray.ShowBalloonTip(2500); }
                if (selectedPage == "library") RefreshLibrary();
            }
            catch (Exception ex) { App.WriteError(ex); StatusLabel.Text = "Screenshot failed: " + ex.Message; failed=true; }
            finally { screenshotBusy = false; if (failed || wasVisible) ShowWindow(); if (dockVisible && settings.FloatingControls) EnsureQuickDock(); UpdateQuickState(); }
        }
        private AudioMixSettings SystemMix() => new AudioMixSettings { Volume = (float)(SystemVolumeSlider.Value / 100), Muted = SystemVolumeSlider.Value == 0,SyncOffsetMs=settings.SystemSyncOffsetMs };
        private AudioMixSettings MicrophoneMix() => new AudioMixSettings { Volume = (float)(MicrophoneVolumeSlider.Value / 100), Muted = MicrophoneVolumeSlider.Value == 0, NoiseSuppression = NoiseSuppressionToggle.IsChecked == true, NoiseGate = NoiseGateToggle.IsChecked == true, Limiter = LimiterToggle.IsChecked == true,Compressor=settings.AudioCompressor,Monitor=settings.MicrophoneMonitor,SyncOffsetMs=settings.MicrophoneSyncOffsetMs };
        private async void ApplyAudioSync_Click(object sender,RoutedEventArgs e)
        {
            if(!int.TryParse(SystemSyncBox.Text,out int system)||!int.TryParse(MicrophoneSyncBox.Text,out int mic)||system < -1000||system>20000||mic < -1000||mic>20000){AudioSyncStatus.Text="Enter whole milliseconds between -1000 and 20000.";return;}
            settings.SystemSyncOffsetMs=system;settings.MicrophoneSyncOffsetMs=mic;UpdateOptions();await ApplyAudioLevels();AudioSyncStatus.Text="Audio sync offsets saved.";
        }
        private void AudioLevel_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!ready) return;
            settings.SystemVolume = SystemVolumeSlider.Value; settings.MicrophoneVolume = MicrophoneVolumeSlider.Value;
            if (!HasActiveSession || busy) return;
            audioUpdateTimer.Stop();audioUpdateTimer.Start();
        }
        private async Task ApplyAudioLevels()
        {
            if(!HasActiveSession || busy || audioUpdatePending) return;
            audioUpdatePending=true;
            var system = SystemMix(); var microphone = MicrophoneMix();
            try { await worker.Run(() => { if (settings.SystemAudio) recorder.SetAudioControls(AudioChannel.System,system); if (settings.Microphone) recorder.SetAudioControls(AudioChannel.Microphone,microphone); }); }
            catch (Exception ex) { StatusLabel.Text = "Audio adjustment failed: " + ex.Message; }
            finally { audioUpdatePending=false; }
        }
        private void OverlayText_Changed(object sender, TextChangedEventArgs e) { if (ready) SaveFeatureSettings(); }
        private void ReplayToggle_Changed(object sender, RoutedEventArgs e) { if(ready) UpdateOptions(); }
        private void ChooseLogo_Click(object sender, RoutedEventArgs e)
        {
            if (busy || HasActiveSession) return;
            busy = true;
            try { var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a transparent logo or image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif", CheckFileExists = true }; if (dialog.ShowDialog(this) == true) { LogoPathBox.Text = dialog.FileName; UpdateOptions(); } }
            finally { busy = false; UpdateQuickState(); }
        }
        private void ClearLogo_Click(object sender, RoutedEventArgs e) { if (HasActiveSession || busy) return; LogoPathBox.Text = ""; UpdateOptions(); }
        private SourceTransform CornerTransform(int position, int width, int height, int canvasWidth, int canvasHeight)
        {
            return new SourceTransform { X = position % 2 == 0 ? 24 : Math.Max(0,canvasWidth-width-24), Y = position < 2 ? 24 : Math.Max(0,canvasHeight-height-24), Width = width, Height = height, Visible = true };
        }
        private void AddComposition(RecorderOptions options)
        {
            SaveFeatureSettings();
            options.SystemAudioSettings = SystemMix(); options.MicrophoneSettings = MicrophoneMix();
            if(captureMode=="scene"){options.SceneCollection=ResolveScenes(sceneLibrary,options.Width,options.Height);return;}
            options.Overlays = new List<SceneOverlay>();
            if(settings.ScenePreset>0 && !settings.CameraEnabled) throw new InvalidOperationException("Enable a camera for this layout, or select Screen with overlays in Studio.");
            if (settings.CameraEnabled)
            {
                if (string.IsNullOrEmpty(settings.CameraId)) throw new InvalidOperationException("Select an available camera in Studio or turn the camera off.");
                int width = Math.Max(160,options.Width/4), height = width*9/16;
                SourceTransform cameraTransform=CornerTransform(3-settings.CameraPosition,width,height,options.Width,options.Height);
                if(settings.ScenePreset==1) cameraTransform=new SourceTransform { X=options.Width*0.70f,Y=24,Width=options.Width*0.28f,Height=options.Height-48,Visible=true };
                if(settings.ScenePreset==2) cameraTransform=new SourceTransform { X=0,Y=0,Width=options.Width,Height=options.Height,Visible=true };
                options.Overlays.Add(new SceneOverlay { Id="camera", Kind=OverlayKind.Camera, DeviceId=settings.CameraId, Transform=cameraTransform });
            }
            if (!string.IsNullOrWhiteSpace(settings.LogoPath))
            {
                if (!File.Exists(settings.LogoPath)) throw new FileNotFoundException("The overlay image was moved or deleted. Choose it again in Studio.");
                int width = Math.Max(96,options.Width/7);
                int corner=settings.OverlayPosition<2?settings.OverlayPosition+2:settings.OverlayPosition-2;
                options.Overlays.Add(new SceneOverlay { Id="logo", Kind=OverlayKind.Image, FilePath=settings.LogoPath, Transform=CornerTransform(corner,width,width,options.Width,options.Height) });
            }
            if (!string.IsNullOrWhiteSpace(settings.OverlayText))
            {
                int corner=settings.OverlayPosition<2?settings.OverlayPosition+2:settings.OverlayPosition-2;
                var transform=CornerTransform(corner,options.Width/2,56,options.Width,options.Height);
                if (!string.IsNullOrWhiteSpace(settings.LogoPath))
                {
                    int offset=Math.Max(96,options.Width/7)+12;
                    transform.Y=Math.Clamp(transform.Y+(corner<2?offset:-offset),0,Math.Max(0,options.Height-56));
                }
                options.Overlays.Add(new SceneOverlay { Id="title", Kind=OverlayKind.Text, Text=settings.OverlayText.Substring(0,Math.Min(300,settings.OverlayText.Length)), FontSize=Math.Max(20,options.Height/25), Transform=transform });
            }
        }
        private SourceTransform CaptureLayout(RecorderOptions options)
        {
            if(settings.ScenePreset==1) return new SourceTransform { X=16,Y=16,Width=options.Width*0.67f,Height=options.Height-32,Visible=true };
            return new SourceTransform { X=0,Y=0,Width=options.Width,Height=options.Height,Visible=settings.ScenePreset!=2 };
        }
        private async void Live_Click(object sender, RoutedEventArgs e) { await ToggleLive(); }
        private async void Replay_Click(object sender, RoutedEventArgs e) { await ToggleReplay(); }
        private async void SaveReplay_Click(object sender, RoutedEventArgs e) { await SaveCurrentReplay(); }
        private async Task EnsureCaptureSession(bool videoOnly=false)
        {
            if (cleanupPending) throw new InvalidOperationException("Press Finish capture before starting a new output.");
            if (HasActiveSession) return;
            if (monitors.Length == 0) throw new InvalidOperationException("Connect a display before starting capture.");
            var current = Forms.Screen.AllScreens; var identities = current.Select(GetDisplayIdentity).ToArray();
            int index = Array.FindIndex(identities,id=>id==settings.MonitorIdentity);
            if (captureMode != "window" && captureMode != "scene" && (index < 0 || MonitorPicker.SelectedIndex < 0)) throw new InvalidOperationException("Choose a connected display on the Record tab.");
            if (captureMode == "window" && WindowPicker.SelectedItem == null) throw new InvalidOperationException("Choose an open window on the Record tab.");
            if (captureMode == "area" && (selectedArea.Width==0 || areaDisplay!=settings.MonitorIdentity)) throw new InvalidOperationException("Select an area on the Record tab.");
            SyncDisplays(current,identities,Math.Max(0,index)); settings.MonitorIndex=Math.Max(0,index);
            var options = BuildOptions(); AddComposition(options);
            if(videoOnly){options.SystemAudio=false;options.Microphone=false;}
            var layout=CaptureLayout(options);
            await worker.Run(()=>StartNativeSession(options,layout));
        }
        // Called on the recording worker. These dimensions remain fixed until the
        // native session ends, even when the editable scene canvas changes shape.
        private void StartNativeSession(RecorderOptions options,SourceTransform layout)
        {
            if(recorder==null)recorder=new RecorderClient();
            if(recorder.IsSessionActive)return;
            recorder.StartSession(options);
            sessionWidth=options.Width;sessionHeight=options.Height;sessionFps=options.Fps;
            if(options.SceneCollection==null)recorder.UpdateCaptureTransform(layout);
        }
        private async Task ToggleLive()
        {
            if (busy || exporting || screenshotBusy) return;
            busy=true; captureRevision++; UpdateQuickState();
            try
            {
                if (streaming) { await worker.Run(()=>recorder.StopStreaming()); streaming=false; StatusLabel.Text="Streaming stopped."; }
                else
                {
                    string endpoint=LiveServerBox.Text.Trim();
                    if (!Uri.TryCreate(endpoint,UriKind.Absolute,out Uri address) || (address.Scheme!="rtmp" && address.Scheme!="rtmps") || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query)) throw new InvalidOperationException("Enter an RTMP or RTMPS server URL without credentials or query parameters. Keep the stream key in its separate field.");
                    if (string.IsNullOrWhiteSpace(StreamKeyBox.Password)) throw new InvalidOperationException("Enter your private stream key before going live.");
                    if (!int.TryParse(StreamBitrateBox.Text,out int bitrate) || bitrate<500 || bitrate>20000) throw new InvalidOperationException("Choose a stream bitrate between 500 and 20000 kbps.");
                    if (paused) throw new InvalidOperationException("Resume the recording before going live.");
                    await EnsureCaptureSession();
                    var options=new StreamOptions { ServerUrl=endpoint,StreamKey=StreamKeyBox.Password,VideoBitrateKbps=bitrate,AudioBitrateKbps=160 };
                    await worker.Run(()=>recorder.StartStreaming(options)); streaming=true;
                    StatusLabel.Text="Live stream started. Stop streaming to end transmission.";
                }
                await ReleaseUnusedSession();
            }
            catch (Exception ex) { StatusLabel.Text="Stream operation failed. " + SafeStreamMessage(ex.Message); await ReleaseUnusedSession(); ShowWindow(); }
            finally { busy=false; UpdateSessionIndicators(); }
        }
        private string SafeStreamMessage(string message)
        {
            string key=StreamKeyBox.Password;
            return string.IsNullOrEmpty(key)?message:message.Replace(key,"[redacted]",StringComparison.Ordinal);
        }
        private async Task ToggleReplay()
        {
            if (busy || exporting || screenshotBusy) return;
            busy=true; captureRevision++; UpdateQuickState();
            try
            {
                if (replaying) { await worker.Run(()=>recorder.StopReplayBuffer()); replaying=false; StatusLabel.Text="Replay buffer stopped."; }
                else
                {
                    if (paused) throw new InvalidOperationException("Resume recording before starting replay.");
                    if (!int.TryParse(ReplaySecondsBox.Text,out int seconds) || seconds<5 || seconds>120) throw new InvalidOperationException("Choose a replay length from 5 to 120 seconds.");
                    await EnsureCaptureSession();
                    await worker.Run(()=>recorder.StartReplayBuffer(new ReplayOptions { Seconds=seconds,MaximumMegabytes=128,OutputDirectory=settings.OutputDirectory }));
                    replaying=true; StatusLabel.Text="Replay buffer is capturing · up to " + seconds + " seconds / 128 MB. Stop it when no longer needed.";
                }
                await ReleaseUnusedSession();
            }
            catch (Exception ex) { StatusLabel.Text="Replay operation failed: " + ex.Message; await ReleaseUnusedSession(); ShowWindow(); }
            finally { busy=false; UpdateSessionIndicators(); }
        }
        private async Task SaveCurrentReplay()
        {
            if (busy || !replaying) return;
            busy=true; UpdateQuickState();
            try { string path=await worker.Run(()=>recorder.SaveReplay()); StatusLabel.Text="Replay saved · " + Path.GetFileName(path); RefreshLibrary(); }
            catch (Exception ex) { StatusLabel.Text="Replay could not be saved: " + ex.Message; }
            finally { busy=false; UpdateSessionIndicators(); }
        }
        private async Task ReleaseUnusedSession()
        {
            if (recording || streaming || replaying || virtualCamera || recorder==null) return;
            try { await worker.Run(()=>recorder.Stop()); cleanupPending=false; }
            catch (Exception ex) { cleanupPending=await worker.Run(()=>recorder.IsSessionActive); StatusLabel.Text="Capture cleanup needs attention: " + SafeStreamMessage(ex.Message); }
            finally { if(!recorder.IsSessionActive)sessionWidth=sessionHeight=sessionFps=0; }
        }
        private async Task RetryCaptureCleanup()
        {
            if (busy) return;
            busy=true; captureRevision++; UpdateQuickState();
            try { await ReleaseUnusedSession(); if (!cleanupPending) StatusLabel.Text="Capture finished. Ready for a new recording."; }
            finally { busy=false; UpdateSessionIndicators(); }
        }
        private void UpdateSessionIndicators()
        {
            LiveButton.Content=streaming?"Stop streaming":"Go live";
            ReplayButton.Content=replaying?"Stop replay buffer":"Start replay buffer";
            SaveReplayButton.IsEnabled=replaying && !busy;
            PauseButton.IsEnabled=!busy && !streaming && !replaying && !virtualCamera;
            SetControlsEnabled(!HasActiveSession && !busy);
            if (HasActiveSession && !cleanupPending) timer.Start(); else timer.Stop();
            if (!recording)
            {
                StateLabel.Text=streaming ? "Streaming live" : replaying ? "Replay buffer active" : virtualCamera ? "Virtual camera active" : "Ready to record";
                RecordLabel.Text="Start recording"; RecordGlyph.Text="●";
            }
            if (tray!=null) { tray.Icon=HasActiveSession?activeIcon:idleIcon; tray.Text="Rilmo Recorder · " + (recording ? (paused?"PAUSED":"RECORDING") : streaming?"LIVE":replaying?"REPLAY":virtualCamera?"CAMERA":"Ready"); }
            if (cleanupPending) { StateLabel.Text="Capture needs to finish"; RecordLabel.Text="Finish capture"; }
            UpdateQuickState();
        }
        private void UpdateSessionIndicatorsAfterRecording()
        {
            LiveButton.Content=streaming?"Stop streaming":"Go live";
            ReplayButton.Content=replaying?"Stop replay buffer":"Start replay buffer";
            SaveReplayButton.IsEnabled=replaying && !busy;
            PauseButton.IsEnabled=!busy && !streaming && !replaying && !virtualCamera;
            if(!recording && (streaming || replaying || virtualCamera)) StateLabel.Text=streaming?"Saved · streaming continues":replaying?"Saved · replay buffer active":"Saved · virtual camera active";
            if(tray!=null) { tray.Icon=HasActiveSession?activeIcon:idleIcon; tray.Text="Rilmo Recorder · " + (recording?"RECORDING":streaming?"LIVE":replaying?"REPLAY":virtualCamera?"CAMERA":"Ready"); }
            if (cleanupPending) { StateLabel.Text="Capture needs to finish"; RecordLabel.Text="Finish capture"; }
            UpdateQuickState();
        }
    }
}
