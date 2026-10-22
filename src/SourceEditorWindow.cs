// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Rilmo.Engine;
using Forms = System.Windows.Forms;

namespace Rilmo
{
    internal sealed class SourceEditorWindow : Window
    {
        internal SceneSource Result { get; private set; }
        private readonly SceneSource original;
        private readonly int canvasWidth,canvasHeight;
        private readonly StackPanel fields=new StackPanel();
        private readonly Dictionary<string,TextBox> text=new Dictionary<string,TextBox>();
        private readonly ComboBox kind=new ComboBox(),devices=new ComboBox();
        private readonly CheckBox visible=new CheckBox{Content="Show this layer"},cursor=new CheckBox{Content="Include mouse pointer"};
        private readonly TextBlock status=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(255,128,149)),Margin=new Thickness(0,12,0,0)};
        private Forms.Screen[] displays=Array.Empty<Forms.Screen>();
        private bool discovering;
        private readonly SceneCollection context;
        private readonly string parentSceneId;
        private readonly CheckBox mediaLoop=new CheckBox{Content="Loop video"},mediaAudio=new CheckBox{Content="Include video audio"},colorCorrection=new CheckBox{Content="Enable color correction"},chromaKey=new CheckBox{Content="Remove green background"};
        private int discoveryVersion;
        internal SourceEditorWindow(SceneSource source,int width,int height,SceneCollection context=null,string parentSceneId=null)
        {
            original=SceneLibrary.Clone(new SceneCollection{Scenes=new List<SceneDefinition>{new SceneDefinition{Id="edit",Name="Edit",Sources=new List<SceneSource>{source}}},SelectedSceneId="edit"}).Scenes[0].Sources[0];
            this.context=context;this.parentSceneId=parentSceneId;
            canvasWidth=width;canvasHeight=height;
            Title="Edit source · Rilmo";Width=620;Height=Math.Min(760,SystemParameters.WorkArea.Height);MinWidth=440;MinHeight=Math.Min(500,Height);MaxHeight=SystemParameters.WorkArea.Height;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=(Brush)Application.Current.FindResource("BackgroundBrush");Foreground=(Brush)Application.Current.FindResource("TextBrush");FontFamily=(FontFamily)Application.Current.FindResource("UiFont");FontSize = 14;FontWeight=FontWeights.SemiBold;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;System.Windows.Shell.WindowChrome.SetWindowChrome(this,new System.Windows.Shell.WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(5),GlassFrameThickness=new Thickness(0)});
            var shell=new Grid{Margin=new Thickness(22)};shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});shell.RowDefinitions.Add(new RowDefinition());shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Content=shell;
            var heading=new StackPanel{Margin=new Thickness(0,0,0,18)};heading.Children.Add(new TextBlock{Text="Shape your source.",FontSize=25,FontWeight=FontWeights.Bold});heading.Children.Add(new TextBlock{Text="Choose what appears, then set its position on the canvas.",Margin=new Thickness(0,6,0,0),Foreground=(Brush)FindResource("MutedBrush"),TextWrapping=TextWrapping.Wrap});shell.Children.Add(heading);
            heading.MouseLeftButtonDown+=(s,e)=>DragMove();KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Close();};
            var scroll=new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);shell.Children.Add(scroll);
            AddText("name","Layer name",source.Name??"",120);
            fields.Children.Add(Label("Source type"));kind.ItemsSource=Enum.GetValues<SceneSourceKind>();kind.SelectedItem=source.Kind;AutomationProperties.SetName(kind,"Source type");fields.Children.Add(kind);
            var dynamicFields=new StackPanel{Margin=new Thickness(0,14,0,0)};fields.Children.Add(dynamicFields);
            fields.Children.Add(Label("Position & size · canvas pixels"));
            var t=source.Transform??new SourceTransform{Width=width,Height=height};
            var geometry=new System.Windows.Controls.Primitives.UniformGrid{Columns=4};fields.Children.Add(geometry);
            AddNumeric(geometry,"x","X",t.X);AddNumeric(geometry,"y","Y",t.Y);AddNumeric(geometry,"width","Width",t.Width);AddNumeric(geometry,"height","Height",t.Height);
            var transformRow=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};fields.Children.Add(transformRow);AddNumeric(transformRow,"rotation","Rotation (degrees)",t.Rotation);
            var fit=new Button{Content="Fit canvas",Margin=new Thickness(6,26,0,0)};fit.Click+=(s,e)=>{text["x"].Text="0";text["y"].Text="0";text["width"].Text=width.ToString();text["height"].Text=height.ToString();text["rotation"].Text="0";};transformRow.Children.Add(fit);
            fields.Children.Add(Label("Crop · source pixels"));var crops=new System.Windows.Controls.Primitives.UniformGrid{Columns=4};fields.Children.Add(crops);var crop=source.Crop??new SourceCrop();AddNumeric(crops,"left","Left",crop.Left);AddNumeric(crops,"top","Top",crop.Top);AddNumeric(crops,"right","Right",crop.Right);AddNumeric(crops,"bottom","Bottom",crop.Bottom);
            visible.IsChecked=t.Visible;fields.Children.Add(visible);
            var footer=new StackPanel();Grid.SetRow(footer,2);shell.Children.Add(footer);footer.Children.Add(status);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};footer.Children.Add(actions);
            var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(0,0,8,0)};actions.Children.Add(cancel);var save=new Button{Content="Save layer",Style=(Style)FindResource("PrimaryButton"),IsDefault=true};save.Click+=Save;actions.Children.Add(save);
            kind.SelectionChanged+=async(s,e)=>await ShowKind(dynamicFields);
            Loaded+=async(s,e)=>await ShowKind(dynamicFields);
            Closed+=(s,e)=>discoveryVersion++;
        }
        private TextBlock Label(string value)=>new TextBlock{Text=value,Foreground=(Brush)FindResource("MutedBrush"),Margin=new Thickness(0,14,0,8),FontSize = 13};
        private TextBox AddText(string key,string label,string value,int max=4096,Panel parent=null)
        {
            parent??=fields;parent.Children.Add(Label(label));var box=new TextBox{Text=value??"",MaxLength=max};AutomationProperties.SetName(box,label);parent.Children.Add(box);text[key]=box;return box;
        }
        private void AddNumeric(Panel parent,string key,string label,double value)
        {
            var cell=new StackPanel{Margin=new Thickness(0,0,6,0)};parent.Children.Add(cell);AddText(key,label,value.ToString("0.##",CultureInfo.InvariantCulture),12,cell);
        }
        private void Explain(Panel parent,string value)=>parent.Children.Add(new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("MutedBrush"),FontSize = 13,Margin=new Thickness(0,10,0,2)});
        private async Task ShowKind(StackPanel parent)
        {
            if(colorCorrection.Parent is Panel previousColor)previousColor.Children.Remove(colorCorrection);
            if(chromaKey.Parent is Panel previousKey)previousKey.Children.Remove(chromaKey);
            int version=++discoveryVersion;parent.Children.Clear();status.Text="";devices.ItemsSource=null;devices.Items.Clear();devices.SelectedIndex=-1;devices.DisplayMemberPath="";
            var value=(SceneSourceKind)kind.SelectedItem;
            if(text["name"].Text=="New source" || Enum.GetNames<SceneSourceKind>().Contains(text["name"].Text))text["name"].Text=value.ToString();
            if(value==SceneSourceKind.Display || value==SceneSourceKind.Window || value==SceneSourceKind.Game || value==SceneSourceKind.Camera)
            {
                parent.Children.Add(Label(value==SceneSourceKind.Camera?"Camera device":value==SceneSourceKind.Display?"Display":"Open window"));parent.Children.Add(devices);AutomationProperties.SetName(devices,"Source device or window");
                var refresh=new Button{Content="Refresh list",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0),Style=(Style)FindResource("QuietButton")};refresh.Click+=async(s,e)=>await ShowKind(parent);parent.Children.Add(refresh);
                if(value==SceneSourceKind.Display){displays=Forms.Screen.AllScreens;for(int i=0;i<displays.Length;i++)devices.Items.Add("Display "+(i+1)+" · "+displays[i].Bounds.Width+" × "+displays[i].Bounds.Height);int index=Array.FindIndex(displays,d=>DisplayIdentity(d)==original.MonitorIdentity);devices.SelectedIndex=index>=0?index:(string.IsNullOrEmpty(original.MonitorIdentity)&&displays.Length>0?Math.Clamp(original.MonitorIndex,0,displays.Length-1):-1);}
                else if(value==SceneSourceKind.Window || value==SceneSourceKind.Game){var windows=RecorderEngine.GetCaptureWindows();devices.ItemsSource=windows;devices.SelectedItem=windows.FirstOrDefault(w=>w.Title==original.WindowTitle && w.ClassName==original.WindowClass && w.AppName==original.WindowExe);Explain(parent,value==SceneSourceKind.Game?"Choose the game you want to capture. If a game blocks capture or runs at a higher privilege level, use Display or Window instead.":"Open and restore the window before selecting it. A missing window is never replaced with a different one.");}
                else
                {
                    discovering=true;devices.IsEnabled=false;try{var cameras=await Task.Run(CameraDeviceCatalog.Enumerate);if(version!=discoveryVersion)return;devices.ItemsSource=cameras;devices.DisplayMemberPath="Name";devices.SelectedItem=cameras.FirstOrDefault(c=>c.Id==original.DeviceId);if(!cameras.Any())status.Text="Connect a camera, then refresh this list.";}catch(Exception ex){status.Text=ex.Message;}finally{if(version==discoveryVersion){discovering=false;devices.IsEnabled=true;}}
                }
                if(value!=SceneSourceKind.Camera){cursor.IsChecked=original.CaptureCursor;parent.Children.Add(cursor);}
            }
            if(value==SceneSourceKind.Image || value==SceneSourceKind.Browser || value==SceneSourceKind.Media)
            {
                var path=AddText("file",value==SceneSourceKind.Browser?"Local HTML file (optional)":value==SceneSourceKind.Media?"Video file":"Image file",original.FilePath,4096,parent);
                var browse=new Button{Content="Choose file…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};browse.Click+=(s,e)=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter=value==SceneSourceKind.Image?"Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif":value==SceneSourceKind.Media?"Videos|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.m4v|All files|*.*":"HTML pages|*.html;*.htm",CheckFileExists=true};if(picker.ShowDialog(this)==true)path.Text=picker.FileName;};parent.Children.Add(browse);
            }
            if(value==SceneSourceKind.Text){var box=AddText("title","Text",original.Text,4096,parent);box.AcceptsReturn=true;box.TextWrapping=TextWrapping.Wrap;box.MinHeight=72;AddText("font","Font size",original.FontSize.ToString(),4,parent);AddText("textColor","Text color · #RRGGBB", "#"+(original.TextColor&0xFFFFFF).ToString("X6"),7,parent);}
            if(value==SceneSourceKind.Color)AddText("color","Color · #RRGGBB","#"+(original.Color&0xFFFFFF).ToString("X6"),7,parent);
            if(value==SceneSourceKind.Browser)
            {
                AddText("url","Website URL (used when local file is blank)",original.BrowserUrl??"https://",4096,parent);
                var size=new System.Windows.Controls.Primitives.UniformGrid{Columns=3};parent.Children.Add(size);AddNumeric(size,"browserWidth","Page width",original.BrowserWidth);AddNumeric(size,"browserHeight","Page height",original.BrowserHeight);AddNumeric(size,"fps","Frames / second",original.BrowserFps);
                Explain(parent,"Browser pages connect when capture or preview starts. Use pages you trust. Browser audio is muted. Inactive pages sleep; 30 fps uses fewer resources than 60.");
                if(!RecorderEngine.BrowserRuntimeAvailable)Explain(parent,"This build needs the optional browser component before it can capture a page.");
            }
            if(value==SceneSourceKind.Media)
            {
                mediaLoop.IsChecked=original.MediaLoop;mediaAudio.IsChecked=original.MediaAudio;parent.Children.Add(mediaLoop);parent.Children.Add(mediaAudio);
                Explain(parent,"Local videos restart when their scene becomes active. Hidden videos release their decoder. Hardware decoding is preferred when available.");
            }
            if(value==SceneSourceKind.Slideshow)
            {
                var paths=AddText("slides","Images · one full path per line",string.Join("\n",original.SlideFiles??new List<string>()),65536,parent);paths.AcceptsReturn=true;paths.MinHeight=90;paths.TextWrapping=TextWrapping.Wrap;
                var choose=new Button{Content="Choose images…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};choose.Click+=(s,e)=>{var picker=new Microsoft.Win32.OpenFileDialog{Multiselect=true,Filter="Images|*.png;*.jpg;*.jpeg;*.bmp",CheckFileExists=true};if(picker.ShowDialog(this)==true)paths.Text=string.Join("\n",picker.FileNames);};parent.Children.Add(choose);
                AddText("slideSeconds","Seconds per slide",original.SlideSeconds.ToString(),4,parent);Explain(parent,"Up to 20 images. The slideshow pauses while its scene is hidden.");
            }
            if(value==SceneSourceKind.Scene)
            {
                parent.Children.Add(Label("Nested scene"));parent.Children.Add(devices);devices.DisplayMemberPath="Name";
                devices.ItemsSource=context?.Scenes.Where(s=>s.Id!=parentSceneId).ToArray()??Array.Empty<SceneDefinition>();devices.SelectedItem=context?.Scenes.FirstOrDefault(s=>s.Id==original.NestedSceneId);
                Explain(parent,"Reuse another scene as one layer. Move, resize, crop or hide it as a group. Recursive scene references are rejected.");
            }
            if(value!=SceneSourceKind.Scene)
            {
                var filterFields=new StackPanel();var expander=new Expander{Header="Video filters",Content=filterFields,Margin=new Thickness(0,16,0,0)};parent.Children.Add(expander);
                colorCorrection.IsChecked=original.ColorCorrection;chromaKey.IsChecked=original.ChromaKey;filterFields.Children.Add(colorCorrection);
                AddText("brightness","Brightness · -1 to 1",original.Brightness.ToString(CultureInfo.InvariantCulture),12,filterFields);
                AddText("contrast","Contrast · -1 to 1",original.Contrast.ToString(CultureInfo.InvariantCulture),12,filterFields);
                AddText("saturation","Saturation · -1 to 1",original.Saturation.ToString(CultureInfo.InvariantCulture),12,filterFields);
                AddText("opacity","Opacity · 0 to 1",original.Opacity.ToString(CultureInfo.InvariantCulture),12,filterFields);
                filterFields.Children.Add(chromaKey);AddText("similarity","Green similarity · 1 to 1000",original.ChromaSimilarity.ToString(),4,filterFields);
                Explain(filterFields,"Filters are off by default to save GPU work. Apply filters inside a nested scene to edit its individual layers.");
            }
            if(value!=SceneSourceKind.Camera){discovering=false;devices.IsEnabled=true;}
        }
        private static string DisplayIdentity(Forms.Screen screen)
        {
            var device=new NativeObs.DisplayDevice{Size=(uint)Marshal.SizeOf<NativeObs.DisplayDevice>()};return NativeObs.EnumDisplayDevices(screen.DeviceName,0,ref device,1)&&!string.IsNullOrEmpty(device.DeviceId)?device.DeviceId:screen.DeviceName;
        }
        private float Number(string key,float minimum,float maximum)
        {
            if(!float.TryParse(text[key].Text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||!float.IsFinite(value)||value<minimum||value>maximum)throw new ArgumentException(key+" must be between "+minimum+" and "+maximum+".");return value;
        }
        private int Integer(string key,int min,int max){float value=Number(key,min,max);if(value!=Math.Truncate(value))throw new ArgumentException(key+" must be a whole number.");return (int)value;}
        private uint ColorValue(string key)
        {
            string value=text[key].Text.Trim().TrimStart('#');if(value.Length!=6||!uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint rgb))throw new ArgumentException("Use a six-digit color, for example #DF3048.");return 0xFF000000|rgb;
        }
        private void Save(object sender,RoutedEventArgs e)
        {
            try
            {
                if(discovering)throw new InvalidOperationException("Wait for camera discovery to finish.");
                var source=original;source.Name=text["name"].Text.Trim();if(source.Name.Length==0)throw new ArgumentException("Give this layer a name.");source.Kind=(SceneSourceKind)kind.SelectedItem;
                source.Transform=new SourceTransform{X=Number("x",-16384,16384),Y=Number("y",-16384,16384),Width=Number("width",1,16384),Height=Number("height",1,16384),Rotation=Number("rotation",-360,360),Visible=visible.IsChecked==true};
                source.Crop=new SourceCrop{Left=Integer("left",0,8192),Top=Integer("top",0,8192),Right=Integer("right",0,8192),Bottom=Integer("bottom",0,8192)};
                source.FilePath=null;source.BrowserUrl=null;source.CaptureCursor=cursor.IsChecked==true;
                if(source.Kind==SceneSourceKind.Display){if(devices.SelectedIndex<0)throw new ArgumentException("Select a connected display.");source.MonitorIndex=devices.SelectedIndex;source.MonitorIdentity=DisplayIdentity(displays[source.MonitorIndex]);}
                if(source.Kind==SceneSourceKind.Window || source.Kind==SceneSourceKind.Game){if(devices.SelectedItem is not CaptureWindow window)throw new ArgumentException("Select an open window.");source.WindowHandle=window.Handle;source.WindowTitle=window.Title;source.WindowClass=window.ClassName;source.WindowExe=window.AppName;source.GameCaptureMode="window";}
                if(source.Kind==SceneSourceKind.Camera){if(devices.SelectedItem is not CameraDevice camera||!camera.IsAvailable)throw new ArgumentException("Select an available camera.");source.DeviceId=camera.Id;}
                if(source.Kind==SceneSourceKind.Image || source.Kind==SceneSourceKind.Browser || source.Kind==SceneSourceKind.Media){source.FilePath=text["file"].Text.Trim();if(source.FilePath.Length>0){source.FilePath=Path.GetFullPath(source.FilePath);if(!File.Exists(source.FilePath))throw new ArgumentException("Choose a file that exists.");}else if(source.Kind!=SceneSourceKind.Browser)throw new ArgumentException("Choose a file.");}
                if(source.Kind==SceneSourceKind.Text){source.Text=text["title"].Text;source.FontSize=Integer("font",8,256);source.TextColor=ColorValue("textColor")&0xFFFFFF;}
                if(source.Kind==SceneSourceKind.Color)source.Color=ColorValue("color");
                if(source.Kind==SceneSourceKind.Browser){source.BrowserWidth=Integer("browserWidth",16,8192);source.BrowserHeight=Integer("browserHeight",16,8192);source.BrowserFps=Integer("fps",1,60);source.BrowserUrl=text["url"].Text.Trim();if(string.IsNullOrEmpty(source.FilePath)&&(!Uri.TryCreate(source.BrowserUrl,UriKind.Absolute,out var url)||(url.Scheme!="https"&&url.Scheme!="http")||!string.IsNullOrEmpty(url.UserInfo)))throw new ArgumentException("Enter an HTTP or HTTPS URL without embedded credentials, or choose local HTML.");}
                if(source.Kind==SceneSourceKind.Media){source.MediaLoop=mediaLoop.IsChecked==true;source.MediaAudio=mediaAudio.IsChecked==true;}
                if(source.Kind==SceneSourceKind.Slideshow){source.SlideFiles=text["slides"].Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(p=>Path.GetFullPath(p.Trim())).ToList();source.SlideSeconds=Integer("slideSeconds",1,3600);if(source.SlideFiles.Any(p=>!File.Exists(p)))throw new ArgumentException("A selected slide is missing.");}
                source.ColorCorrection=source.Kind!=SceneSourceKind.Scene&&colorCorrection.IsChecked==true;source.ChromaKey=source.Kind!=SceneSourceKind.Scene&&chromaKey.IsChecked==true;
                if(source.Kind!=SceneSourceKind.Scene){source.Brightness=Number("brightness",-1,1);source.Contrast=Number("contrast",-1,1);source.Saturation=Number("saturation",-1,1);source.Opacity=Number("opacity",0,1);source.ChromaSimilarity=Integer("similarity",1,1000);}
                if(source.Kind==SceneSourceKind.Scene){if(devices.SelectedItem is not SceneDefinition nested)throw new ArgumentException("Choose another scene.");source.NestedSceneId=nested.Id;}
                var validation=context==null?new SceneCollection{Scenes=new List<SceneDefinition>{new SceneDefinition{Id="validate",Name="Validate"}},SelectedSceneId="validate"}:SceneLibrary.Clone(context);
                var parentScene=validation.Scenes.First(s=>s.Id==(parentSceneId??"validate"));parentScene.Sources.RemoveAll(s=>s.Id==source.Id);parentScene.Sources.Add(source);
                RecorderEngine.ValidateSceneCollectionDefinition(validation,false);
                Result=source;DialogResult=true;
            }
            catch(Exception ex){status.Text=ex.Message;}
        }
    }
}
