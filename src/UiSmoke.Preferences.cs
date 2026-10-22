#if RILMO_TEST
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Rilmo
{
    public partial class MainWindow
    {
        private async Task ValidatePreferencesAndLayout(string directory)
        {
            // Keep evidence captures at their final opacity; animation behavior is tested separately.
            Motion.Enabled=false;
            var face=new Typeface(FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
            if(!face.TryGetGlyphTypeface(out var glyph)||!glyph.FontUri.ToString().Contains("DoppioOne",StringComparison.OrdinalIgnoreCase))throw new Exception("Bundled Doppio One did not resolve.");
            var defaults=HotkeyBinding.Defaults();HotkeyBinding.Validate(defaults);
            using(var first=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Rilmo shortcut test A"){Width=1,Height=1,WindowStyle=0}))
            using(var second=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Rilmo shortcut test B"){Width=1,Height=1,WindowStyle=0}))
            {
                try
                {
                    if(!RegisterHotKey(first.Handle,90,7|0x4000,0x87))throw new Exception("Owned Ctrl+Alt+Shift+F24 shortcut could not register.");
                    if(RegisterHotKey(second.Handle,91,7|0x4000,0x87))throw new Exception("Windows accepted a conflicting global shortcut.");
                    UnregisterHotKey(first.Handle,90);
                    if(!RegisterHotKey(second.Handle,91,7|0x4000,0x87))throw new Exception("Released shortcut could not be registered again.");
                }
                finally{UnregisterHotKey(first.Handle,90);UnregisterHotKey(second.Handle,91);}
            }
            var duplicate=HotkeyBinding.Defaults();duplicate[1]=duplicate[0].Copy();bool rejected=false;try{HotkeyBinding.Validate(duplicate);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Duplicate shortcuts accepted.");
            var plain=HotkeyBinding.Defaults();plain[0].Modifiers=0;rejected=false;try{HotkeyBinding.Validate(plain);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Plain typing shortcut accepted.");
            var custom=HotkeyBinding.Defaults();custom[0]=new HotkeyBinding{Modifiers=3,KeyCode=0x4D};custom[3]=new HotkeyBinding();HotkeyBinding.Validate(custom);
            var roundtrip=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{Hotkeys=custom}));HotkeyBinding.Validate(roundtrip.Hotkeys);if(roundtrip.Hotkeys[0].ToString()!="Ctrl + Alt + M"||roundtrip.Hotkeys[3].KeyCode!=0)throw new Exception("Hotkey persistence failed.");
            pendingHotkeys=duplicate;SaveHotkeys_Click(this,new RoutedEventArgs());if(settings.Hotkeys[0].KeyCode!=0x52)throw new Exception("Invalid hotkey save changed previous settings.");
            pendingHotkeys=custom;SaveHotkeys_Click(this,new RoutedEventArgs());if(settings.Hotkeys[0].KeyCode!=0x4D)throw new Exception("Custom hotkey save failed.");
            ResetHotkeys_Click(this,new RoutedEventArgs());SaveHotkeys_Click(this,new RoutedEventArgs());
            Width=Math.Min(1900,SystemParameters.WorkArea.Width);Height=Math.Min(1000,SystemParameters.WorkArea.Height);Navigate("record");UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            File.WriteAllText(Path.Combine(directory,"layout-measurements.json"),JsonSerializer.Serialize(new{Width,Height,ActualWidth,ActualHeight,viewport=RecordPage.ViewportHeight,layout=RecordLayout.ActualHeight,controls=CaptureControls.ActualHeight,preview=PreviewSurface.ActualHeight},new JsonSerializerOptions{WriteIndented=true}));
            CaptureTestImage(Path.Combine(directory,"record-expanded.png"));
            // Check the visible gap and resize behavior, not one font-dependent pixel height.
            double controlsBottom=CaptureControls.TranslatePoint(new Point(0,CaptureControls.ActualHeight),RecordLayout).Y;
            if(Width>=1800&&Height>=950&&(Math.Abs(RecordLayout.ActualHeight-RecordPage.ViewportHeight)>2||Math.Abs(controlsBottom-RecordLayout.ActualHeight)>2||PreviewSurface.ActualHeight<RecordPage.ViewportHeight/2))throw new Exception("Expanded preview does not fill the available recording workspace.");
            if(DisplayModeButton.ActualHeight<48||NavRecord.ActualHeight<44)throw new Exception("Navigation controls are too short.");
            CaptureTestImage(Path.Combine(directory,"record-expanded.png"));
            double largePreview=PreviewSurface.ActualHeight;
            Width=1040;Height=720;Navigate("record");UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            if(largePreview>400&&largePreview-PreviewSurface.ActualHeight<150)throw new Exception("Preview did not expand with the recording workspace.");
            Width=1040;Height=720;Navigate("settings");UpdateLayout();HotkeyEditors.BringIntoView();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);CaptureTestImage(Path.Combine(directory,"custom-shortcuts.png"));
            SettingsPage.ScrollToEnd();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);CaptureTestImage(Path.Combine(directory,"about-developer.png"));
            var layer=new Rilmo.Engine.SceneSource{Id="ui-source",Name="New source",Kind=Rilmo.Engine.SceneSourceKind.Color,Transform=new Rilmo.Engine.SourceTransform{Width=640,Height=360}};
            var editor=new SourceEditorWindow(layer,640,360){Owner=this};
            try
            {
                editor.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                var selector=(System.Windows.Controls.ComboBox)typeof(SourceEditorWindow).GetField("kind",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(editor);
                foreach(var kind in new[]{Rilmo.Engine.SceneSourceKind.Media,Rilmo.Engine.SceneSourceKind.Slideshow,Rilmo.Engine.SceneSourceKind.Scene,Rilmo.Engine.SceneSourceKind.Text}){selector.SelectedItem=kind;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
                if(App.UnhandledTestException!=null)throw App.UnhandledTestException;
                CaptureWindowImage(editor,Path.Combine(directory,"source-filters.png"));
            }
            finally{editor.Close();}
            Navigate("record");
            File.WriteAllText(Path.Combine(directory,"preferences-layout.json"),JsonSerializer.Serialize(new{passed=true,font=glyph.FontUri.ToString(),expandedPreviewHeight=largePreview,duplicateRejected=true,plainKeyRejected=true,customRoundtrip=true,disabledBinding=true,invalidSaveRollback=true},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
#endif
