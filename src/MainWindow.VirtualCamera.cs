// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Rilmo.Engine;

namespace Rilmo
{
    public partial class MainWindow
    {
        private bool virtualCamera;
        private void RefreshVirtualCamera()
        {
            VirtualCameraCard.Visibility=AppEdition.OffersVirtualCamera?Visibility.Visible:Visibility.Collapsed;
            if(!AppEdition.OffersVirtualCamera)
            {
                VirtualCameraButton.IsEnabled=false;
                SetupCameraButton.Visibility=Visibility.Collapsed;
                return;
            }
            try
            {
                var capability=RecorderEngine.GetVirtualCameraCapability();
                VirtualCameraDescription.Text=virtualCamera?"Camera is sending your video. Choose Rilmo Virtual Camera in your meeting app.":capability.Message;
                VirtualCameraButton.Content=virtualCamera?"Stop camera":"Start camera";
                VirtualCameraButton.IsEnabled=!busy&&!exporting&&(virtualCamera||capability.CanStart);
                SetupCameraButton.Visibility=capability.NeedsSetup?Visibility.Visible:Visibility.Collapsed;
                SetupCameraButton.Content=File.Exists(Path.Combine(AppContext.BaseDirectory,"windows-installer.json"))?"Repair camera":"Set up camera";
                SetupCameraButton.IsEnabled=!HasActiveSession&&!busy&&!exporting;
            }
            catch(Exception ex){VirtualCameraDescription.Text=ex.Message;VirtualCameraButton.IsEnabled=false;}
        }
        private async void VirtualCamera_Click(object sender,RoutedEventArgs e)=>await ToggleVirtualCamera();
        private async Task ToggleVirtualCamera()
        {
            if(!AppEdition.OffersVirtualCamera)return;
            if(busy||exporting||screenshotBusy)return;busy=true;captureRevision++;UpdateQuickState();SetControlsEnabled(false);
            try
            {
                if(virtualCamera){await worker.Run(()=>recorder.StopVirtualCamera());virtualCamera=false;StatusLabel.Text="Virtual camera stopped.";}
                else
                {
                    if(paused)throw new InvalidOperationException("Resume the recording before starting the camera.");
                    var capability=RecorderEngine.GetVirtualCameraCapability();if(!capability.CanStart)throw new InvalidOperationException(capability.Message);
                    await EnsureCaptureSession();await worker.Run(()=>recorder.StartVirtualCamera());virtualCamera=true;
                    StatusLabel.Text="Virtual camera is on. Select Rilmo Virtual Camera in your meeting app. Select your microphone there separately.";
                }
                await ReleaseUnusedSession();
            }
            catch(Exception ex){StatusLabel.Text="Virtual camera: "+ex.Message;await ReleaseUnusedSession();}
            finally{busy=false;RefreshVirtualCamera();UpdateSessionIndicators();}
        }
        private async void SetupCamera_Click(object sender,RoutedEventArgs e)
        {
            if(busy||HasActiveSession||exporting)return;
            if(!AppEdition.OffersVirtualCamera)return;
            string installerMetadata=Path.Combine(AppContext.BaseDirectory,"windows-installer.json");
            if(File.Exists(installerMetadata))
            {
                busy=true;SetControlsEnabled(false);UpdateQuickState();
                try
                {
                    using var metadata=System.Text.Json.JsonDocument.Parse(File.ReadAllText(installerMetadata));
                    string code=metadata.RootElement.GetProperty("productCode").GetString();
                    if(!Guid.TryParseExact(code,"B",out _))throw new InvalidOperationException("Installer repair information is invalid. Re-run the original installer.");
                    if(MessageBox.Show(this,"Repair Rilmo's camera and application registration through Windows Installer?\n\nWindows may ask for administrator permission and the original MSI file. Close video apps using Rilmo Virtual Camera first.","Repair Rilmo",MessageBoxButton.YesNo,MessageBoxImage.Information)!=MessageBoxResult.Yes)return;
                    var repair=new ProcessStartInfo{FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe"),Arguments="/fomus "+code,UseShellExecute=true,Verb="runas"};
                    using var process=Process.Start(repair);
                    if(process==null)throw new InvalidOperationException("Windows did not start repair.");
                    await process.WaitForExitAsync();
                    if(process.ExitCode!=0&&process.ExitCode!=3010)throw new InvalidOperationException("Windows Installer repair did not complete ("+process.ExitCode+").");
                    StatusLabel.Text=process.ExitCode==3010?"Repair completed. Restart Windows when convenient to finish updating files.":"Repair completed. Reopen your meeting app to refresh its cameras.";
                }
                catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){StatusLabel.Text="Camera repair cancelled.";}
                catch(Exception ex){StatusLabel.Text=ex.Message;}
                finally{busy=false;SetControlsEnabled(!HasActiveSession);RefreshVirtualCamera();UpdateQuickState();}
                return;
            }
            string setup=Path.Combine(AppContext.BaseDirectory,"tools","setup-virtual-camera.ps1");
            if(!File.Exists(setup)){StatusLabel.Text="Camera setup is missing. Use the complete standalone build.";return;}
            busy=true;SetControlsEnabled(false);UpdateQuickState();
            try
            {
                if(MessageBox.Show(this,"Add Rilmo Virtual Camera to Windows?\n\nSetup registers Rilmo's camera for 32-bit and 64-bit video apps and requires administrator permission. It does not change OBS. You can remove the device with the supplied camera setup tool.\n\nContinue to the Windows permission prompt?","Set up Rilmo Virtual Camera",MessageBoxButton.YesNo,MessageBoxImage.Information)!=MessageBoxResult.Yes)return;
                var start=new ProcessStartInfo{FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=AppContext.BaseDirectory};
                start.Arguments="-NoProfile -ExecutionPolicy Bypass -File \""+setup+"\" -Action Install -AcknowledgeSystemDeviceChange";
                using(var process=Process.Start(start)){if(process==null)throw new InvalidOperationException("Windows did not open camera setup.");await process.WaitForExitAsync();if(process.ExitCode!=0)throw new InvalidOperationException("Camera setup did not complete. Check the setup log or try again.");}
                StatusLabel.Text="Virtual camera setup finished. Reopen your meeting app if it was already running.";
            }
            catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223){StatusLabel.Text="Camera setup cancelled.";}
            catch(Exception ex){StatusLabel.Text=ex.Message;}
            finally{busy=false;SetControlsEnabled(!HasActiveSession);RefreshVirtualCamera();UpdateQuickState();}
        }
    }
}
