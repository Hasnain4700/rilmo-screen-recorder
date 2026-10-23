#if RILMO_TEST
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Rilmo
{
    public partial class MainWindow
    {
        internal bool SmokeNoAudio { get; set; }
        private async Task RunAutomatedSmoke()
        {
            string directory = Path.GetFullPath(renderPath);
            Directory.CreateDirectory(directory);
            bool success = false;
            try
            {
                await ValidatePreferencesAndLayout(directory);
                if(SmokeNoAudio)
                {
                    // Explicit CI profile for runners with no audio endpoint.
                    // Do not silently change the product's device validation.
                    SystemAudioToggle.IsChecked=false;MicrophoneToggle.IsChecked=false;
                    settings.SystemAudio=false;settings.Microphone=false;
                }
                // This command is compiled only into the explicit test build.
                // It exercises the actual UI lifecycle against the real native engine.
                settings.OutputDirectory = Path.Combine(directory, "recordings");
                Motion.Enabled=false;
                settings.Countdown = false; settings.MinimizeOnRecord = false;
                CountdownToggle.IsChecked = false; MinimizeToggle.IsChecked = false;
                FolderPath.Text = settings.OutputDirectory;
                await ValidateMissingDisplaySelection();
                CaptureTestImage(Path.Combine(directory, "record-idle.png"));
                var before = Process.GetCurrentProcess(); before.Refresh();
                if (before.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("obs.dll", StringComparison.OrdinalIgnoreCase))) throw new Exception("The capture engine loaded while idle.");
                long idleBytes = before.WorkingSet64;
                var cameras = await Task.Run(CameraDeviceCatalog.Enumerate);
                var cameraCapability=Rilmo.Engine.RecorderEngine.GetVirtualCameraCapability();
                RefreshVirtualCamera();
                if(!Rilmo.Engine.AppEdition.OffersVirtualCamera)
                {
                    if(VirtualCameraCard.Visibility!=System.Windows.Visibility.Collapsed||cameraCapability.CanStart||cameraCapability.NeedsSetup)throw new Exception("Store edition exposes a virtual camera action.");
                    await ToggleVirtualCamera();
                    if(virtualCamera||HasActiveSession||recorder!=null)throw new Exception("Excluded Store camera action started capture.");
                }
                if(cameraCapability.NeedsSetup)
                {
                    if(VirtualCameraButton.IsEnabled||SetupCameraButton.Visibility!=System.Windows.Visibility.Visible)throw new Exception("Camera setup state is not represented correctly.");
                    await ToggleVirtualCamera();
                    if(virtualCamera||HasActiveSession||recorder!=null)throw new Exception("Unregistered camera action started capture.");
                }
                before.Refresh();
                if (before.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("obs.dll", StringComparison.OrdinalIgnoreCase))) throw new Exception("Camera discovery loaded the capture engine while idle.");
                await StartRecording();
                if (!recording || !recorder.IsRecording || RecordLabel.Text != "Stop & save") throw new Exception("Recording UI did not enter active state.");
                await Task.Delay(2500);
                await ChangePause();
                if (!paused || StateLabel.Text != "Paused") throw new Exception("Pause UI did not match engine state.");
                await Task.Delay(650);
                await ChangePause();
                if (paused) throw new Exception("Resume UI stayed paused.");
                await Task.Delay(1400);
                CaptureTestImage(Path.Combine(directory, "record-active.png"));
                before.Refresh(); long recordingBytes = before.WorkingSet64;
                string encoder = recorder.EncoderName;
                await StopRecording();
                if (recording || recorder.IsRecording || StateLabel.Text != "Recording saved") throw new Exception("Stop/save UI did not confirm completed output.");
                if (new FileInfo(lastOutput).Length < 1024) throw new Exception("Output file was empty.");
                string mp4 = Path.ChangeExtension(lastOutput, ".mp4");
                await Remuxer.RemuxAsync(lastOutput, mp4, null, System.Threading.CancellationToken.None);
                if (!File.Exists(mp4) || new FileInfo(mp4).Length < 1024) throw new Exception("Combined recording/export lifecycle did not produce MP4.");
                string initialRecording=lastOutput;
                var nativeModules=before.Modules.Cast<ProcessModule>().Where(m=>m.ModuleName.Equals("obs.dll",StringComparison.OrdinalIgnoreCase) || m.ModuleName.Equals("win-capture.dll",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(nativeModules.Length<2 || nativeModules.Any(m=>!m.FileName.StartsWith(AppContext.BaseDirectory,StringComparison.OrdinalIgnoreCase))) throw new Exception("The engine did not load entirely from the bundled app runtime.");
                ReplaySecondsBox.Text="5";
                await ToggleReplay();
                if(!replaying || !recorder.IsReplayBufferActive || !recorder.IsSessionActive) throw new Exception("Replay-only UI failed to start.");
                await Task.Delay(1800);
                await StartRecording();
                if(!recording || !replaying || PauseButton.IsEnabled) throw new Exception("Combined recording/replay state is inconsistent.");
                await ChangePause();
                if(paused) throw new Exception("Shared encoder was paused during replay capture.");
                await Task.Delay(2400);
                await SaveCurrentReplay();
                if(!Directory.EnumerateFiles(settings.OutputDirectory,"*.mkv").Any(p=>Path.GetFileName(p).StartsWith("Rilmo Replay",StringComparison.OrdinalIgnoreCase))) throw new Exception("Replay save did not create a clip. " + StatusLabel.Text);
                await StopRecording();
                if(recording || !replaying || !recorder.IsReplayBufferActive) throw new Exception("Stopping the file interrupted the replay buffer.");
                await ToggleReplay();
                if(HasActiveSession || recorder.IsSessionActive) throw new Exception("Replay stop did not release the session.");
                LiveServerBox.Text="invalid-local-test"; StreamKeyBox.Password="ui-smoke-fake-secret";
                await ToggleLive();
                if(streaming || HasActiveSession || recorder.IsSessionActive) throw new Exception("Invalid streaming destination started capture.");
                if(JsonSerializer.Serialize(settings).Contains(StreamKeyBox.Password,StringComparison.Ordinal)) throw new Exception("Stream key reached serializable settings.");
                StreamKeyBox.Clear(); LiveServerBox.Clear();
                string screenshot=await ScreenshotCapture.SaveAsync(new System.Drawing.Rectangle(monitors[0].Bounds.Left,monitors[0].Bounds.Top,320,180),settings.OutputDirectory);
                if(new FileInfo(screenshot).Length<100) throw new Exception("Screenshot was empty.");
                string combinedRecording=lastOutput;
                string sceneOutput=await ValidateSceneUi(directory);
                Navigate("live"); await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle); CaptureTestImage(Path.Combine(directory,"live.png"));
                Navigate("studio"); await Task.Delay(400); CaptureTestImage(Path.Combine(directory,"studio.png"));
                Width=760;Height=500; UpdateLayout(); CaptureTestImage(Path.Combine(directory,"studio-compact.png"));
                Navigate("record"); CaptureTestImage(Path.Combine(directory,"record-compact.png"));
                Width=1040;Height=720;
                bool dockExcluded;
                var testDock=new QuickDock(command=>{},(x,y)=>{},null,null);
                try
                {
                    testDock.Show();testDock.UpdateState(false,false,false,false,false,"00:00");testDock.UpdateLayout();
                    CaptureWindowImage(testDock,Path.Combine(directory,"floating-button.png"));
                    var dockPeer=System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement((System.Windows.UIElement)testDock.Content);
                    if(dockPeer==null || dockPeer.GetAutomationControlType()!=System.Windows.Automation.Peers.AutomationControlType.Button || dockPeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) is not System.Windows.Automation.Provider.IInvokeProvider invoke) throw new Exception("Floating control does not expose an accessible button action.");
                    invoke.Invoke(); await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    var quickMenu=System.Windows.Application.Current.Windows.Cast<System.Windows.Window>().Single(w=>w.Title=="Quick controls");
                    if(!Rilmo.Engine.AppEdition.OffersVirtualCamera)
                    {
                        var actions=(System.Windows.Controls.StackPanel)((System.Windows.Controls.Border)quickMenu.Content).Child;
                        var cameraAction=actions.Children.OfType<System.Windows.Controls.Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetAutomationId(b)=="QuickAction_camera");
                        if(cameraAction.Visibility!=System.Windows.Visibility.Collapsed)throw new Exception("Store quick controls expose virtual camera.");
                    }
                    if(System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.Button focused || System.Windows.Automation.AutomationProperties.GetAutomationId(focused)!="QuickAction_record") throw new Exception("Quick menu did not focus its first action.");
                    CaptureWindowImage(quickMenu,Path.Combine(directory,"quick-menu.png"));
                    dockExcluded=testDock.CaptureExclusionApplied;
                    if(!dockExcluded) throw new Exception("Floating controls could not be excluded from capture on this supported Windows build.");
                    testDock.CloseMenu();
                }
                finally {testDock.Close();}
                var testPicker=new RegionPicker(monitors[0].Bounds,2,2);
                try { testPicker.Show(); await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle); if(!testPicker.CaptureExclusionApplied) throw new Exception("Screenshot selection overlay could not be excluded from capture."); }
                finally { testPicker.Close(); }
                if (before.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Contains("Qt6", StringComparison.OrdinalIgnoreCase) || m.ModuleName.Contains("libcef", StringComparison.OrdinalIgnoreCase))) throw new Exception("Unexpected Qt/CEF dependency.");
                bool browserUi=await ValidateBrowserUi(directory);
                Navigate("library"); await Task.Delay(300); CaptureTestImage(Path.Combine(directory, "recordings.png"));
                if(RecordingsList.Children.Count<5) throw new Exception("Recording library omitted a saved clip, export or screenshot.");
                Navigate("settings"); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); CaptureTestImage(Path.Combine(directory, "preferences.png"));
                if(App.UnhandledTestException!=null) throw new Exception("An unhandled UI exception occurred during the integration test.",App.UnhandledTestException);
                File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, audioDisabledForTest = SmokeNoAudio, output = initialRecording, combinedOutput=combinedRecording, sceneOutput, browserUi, sceneEditor=true, scenePreviewReleased=true, scenePersistence=true, liveSceneEditing=true, liveFade=true, reducedMotion=true, screenshot, mp4Export = mp4, encoder, idleWorkingSetBytes = idleBytes, recordingWorkingSetBytes = recordingBytes, nativeEngineLazyLoaded = true, appLocalRuntime=true, pauseResume = true, stoppedAndSaved = true, independentReplayAndRecord=true, invalidStreamRejected=true, streamKeyNotPersisted=true, cameraDiscoveryLazy=true, dockExcluded, screenshotLibrary=true, noQtCef = true, missingDisplaySelectionGuard = true, stalePreviewCleared = true, testScope = "Short integrated recording, replay, screenshot and export tests on this machine; not an OBS comparison benchmark." }, new JsonSerializerOptions { WriteIndented = true }));
                success = true;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString()); }
            finally
            {
                try { if (recorder != null) await worker.Run(() => recorder.Dispose()); } catch (Exception ex) { File.AppendAllText(Path.Combine(directory, "failure.txt"), ex.ToString()); success = false; }
                closeAllowed = true;
                System.Windows.Application.Current.Shutdown(success ? 0 : 1);
            }
        }
        private async Task ValidateMissingDisplaySelection()
        {
            int originalIndex = MonitorPicker.SelectedIndex, originalQuality = QualityPicker.SelectedIndex;
            string originalIdentity = settings.MonitorIdentity;
            if (originalIndex < 0) throw new Exception("The UI integration test requires an active display.");
            PreviewImage.Source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            PreviewImage.Visibility = System.Windows.Visibility.Visible;
            PreviewPlaceholder.Visibility = System.Windows.Visibility.Collapsed;
            MonitorPicker.SelectedIndex = -1;
            QualityPicker.SelectedIndex = (originalQuality + 1) % 4;
            if (settings.MonitorIdentity != originalIdentity || MonitorPicker.SelectedIndex != -1) throw new Exception("Changing quality silently replaced a missing display selection.");
            if (PreviewImage.Source != null || PreviewPlaceholder.Visibility != System.Windows.Visibility.Visible) throw new Exception("A preview from the previous display remained visible.");
            bool rejected = false;
            try { await StartRecording(); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || recording || recorder != null) throw new Exception("Recording initialized without an explicit display selection.");
            MonitorPicker.SelectedIndex = originalIndex;
            QualityPicker.SelectedIndex = originalQuality;
            StateLabel.Text = "Ready to record";
            StatusLabel.Text = "Ready. Capture is asleep until you press Record.";
        }
        private void CaptureTestImage(string path)
        {
            CaptureWindowImage(this,path);
        }
        private static void CaptureWindowImage(System.Windows.Window window,string path)
        {
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
#endif
