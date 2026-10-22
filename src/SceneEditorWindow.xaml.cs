// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rilmo.Engine;

namespace Rilmo
{
    public partial class SceneEditorWindow : Window
    {
        private SceneCollection collection;
        private readonly Func<SceneCollection,SceneTransition,int,Task> apply;
        private readonly Func<Task<ScenePreviewFrame>> preview;
        private int canvasWidth,canvasHeight;
        private bool refreshing,changed,working;
        private SceneSource dragging;
        private Point dragOrigin;
        private float dragX,dragY;
        private SceneDefinition SelectedScene => ScenesList.SelectedItem as SceneDefinition;
        private SceneSource SelectedSource => SourcesList.SelectedItem as SceneSource;
        public SceneEditorWindow(SceneCollection initial,int width,int height,Func<SceneCollection,SceneTransition,int,Task> apply,Func<Task<ScenePreviewFrame>> preview)
        {
            collection=SceneLibrary.Clone(initial);this.apply=apply;this.preview=preview;canvasWidth=width;canvasHeight=height;
            InitializeComponent();Width=Math.Min(Width,SystemParameters.WorkArea.Width);Height=Math.Min(Height,SystemParameters.WorkArea.Height);
            MinWidth=Math.Min(MinWidth,Width);MinHeight=Math.Min(MinHeight,Height);
            LayoutCanvas.Width=width;LayoutCanvas.Height=height;
            LayoutCanvas.MouseMove+=(s,e)=>
            {
                if(dragging==null||e.LeftButton!=MouseButtonState.Pressed)return;
                var t=dragging.Transform;var point=e.GetPosition(LayoutCanvas);
                t.X=Math.Clamp(dragX+(float)(point.X-dragOrigin.X),-t.Width+16,canvasWidth-16);t.Y=Math.Clamp(dragY+(float)(point.Y-dragOrigin.Y),-t.Height+16,canvasHeight-16);
                var box=LayoutCanvas.Children.OfType<Border>().FirstOrDefault(v=>ReferenceEquals(v.Tag,dragging));if(box!=null){Canvas.SetLeft(box,t.X);Canvas.SetTop(box,t.Y);}changed=true;
            };
            LayoutCanvas.MouseLeftButtonUp+=(s,e)=>{dragging=null;LayoutCanvas.ReleaseMouseCapture();};
            LayoutCanvas.LostMouseCapture+=(s,e)=>dragging=null;
            RefreshScenes(collection.SelectedSceneId);
            Closing+=OnClosing;Loaded+=(s,e)=>Motion.Reveal((FrameworkElement)Content);
        }
        private void Title_Drag(object sender,MouseButtonEventArgs e) {if(e.ChangedButton==MouseButton.Left)DragMove();}
        private void Close_Click(object sender,RoutedEventArgs e)=>Close();
        private void OnClosing(object sender,CancelEventArgs e)
        {
            if(working){e.Cancel=true;return;}
            if(changed && MessageBox.Show(this,"Close without applying these layout edits?","Unsaved layout",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;
        }
        private void RefreshScenes(string id)
        {
            refreshing=true;ScenesList.ItemsSource=null;ScenesList.ItemsSource=collection.Scenes;
            ScenesList.SelectedItem=collection.Scenes.FirstOrDefault(s=>s.Id==id)??collection.Scenes.First();
            collection.SelectedSceneId=SelectedScene.Id;
            refreshing=false;RefreshSources();
        }
        private void RefreshSources(string id=null)
        {
            refreshing=true;
            SceneNameBox.Text=SelectedScene?.Name??"";
            SourcesList.ItemsSource=SelectedScene?.Sources.AsEnumerable().Reverse().ToList();
            SourcesList.SelectedItem=SelectedScene?.Sources.FirstOrDefault(s=>s.Id==id);
            refreshing=false;DrawLayout();
        }
        private void Scene_Selected(object sender,SelectionChangedEventArgs e) {if(!refreshing){collection.SelectedSceneId=SelectedScene?.Id;RefreshSources();changed=true;}}
        private void SceneName_Changed(object sender,TextChangedEventArgs e)
        {
            if(refreshing||SelectedScene==null)return;SelectedScene.Name=SceneNameBox.Text;ScenesList.Items.Refresh();changed=true;
        }
        private void Source_Selected(object sender,SelectionChangedEventArgs e){if(!refreshing)DrawLayout();}
        private void AddScene_Click(object sender,RoutedEventArgs e)
        {
            if(collection.Scenes.Count>=16){EditorStatus.Text="A collection supports up to 16 scenes.";return;}
            var scene=new SceneDefinition{Id=Guid.NewGuid().ToString("N"),Name="Scene "+(collection.Scenes.Count+1),Sources=new List<SceneSource>()};
            collection.Scenes.Add(scene);collection.SelectedSceneId=scene.Id;changed=true;RefreshScenes(scene.Id);SceneNameBox.Focus();SceneNameBox.SelectAll();
        }
        private void CopyScene_Click(object sender,RoutedEventArgs e)
        {
            if(SelectedScene==null||collection.Scenes.Count>=16)return;
            var copy=SceneLibrary.Clone(new SceneCollection{Scenes=new List<SceneDefinition>{SelectedScene},SelectedSceneId=SelectedScene.Id}).Scenes[0];
            copy.Id=Guid.NewGuid().ToString("N");copy.Name+=" copy";foreach(var source in copy.Sources)source.Id=Guid.NewGuid().ToString("N");
            collection.Scenes.Add(copy);changed=true;RefreshScenes(copy.Id);
        }
        private void RemoveScene_Click(object sender,RoutedEventArgs e)
        {
            if(collection.Scenes.Count<=1){EditorStatus.Text="Keep at least one scene.";return;}
            if(SelectedScene==null)return;
            if(collection.Scenes.Any(s=>s.Sources.Any(v=>v.Kind==SceneSourceKind.Scene&&v.NestedSceneId==SelectedScene.Id))){EditorStatus.Text="Remove references to this nested scene first.";return;}
            collection.Scenes.Remove(SelectedScene);changed=true;RefreshScenes(collection.Scenes[0].Id);
        }
        private void AddSource_Click(object sender,RoutedEventArgs e)
        {
            if(SelectedScene==null || SelectedScene.Sources.Count>=32)return;
            var source=new SceneSource{Id=Guid.NewGuid().ToString("N"),Name="New source",Kind=SceneSourceKind.Display,Transform=new SourceTransform{Width=canvasWidth,Height=canvasHeight,Visible=true},Crop=new SourceCrop()};
            var dialog=new SourceEditorWindow(source,canvasWidth,canvasHeight,collection,SelectedScene.Id){Owner=this};
            if(dialog.ShowDialog()==true){SelectedScene.Sources.Add(dialog.Result);changed=true;RefreshSources(dialog.Result.Id);}
        }
        private void EditSource_Click(object sender,RoutedEventArgs e)
        {
            if(SelectedSource==null)return;var old=SelectedSource;
            var dialog=new SourceEditorWindow(old,canvasWidth,canvasHeight,collection,SelectedScene.Id){Owner=this};
            if(dialog.ShowDialog()==true){int index=SelectedScene.Sources.IndexOf(old);SelectedScene.Sources[index]=dialog.Result;changed=true;RefreshSources(dialog.Result.Id);}
        }
        private void RemoveSource_Click(object sender,RoutedEventArgs e){if(SelectedSource==null)return;SelectedScene.Sources.Remove(SelectedSource);changed=true;RefreshSources();}
        private void LayerUp_Click(object sender,RoutedEventArgs e)=>MoveLayer(1);
        private void LayerDown_Click(object sender,RoutedEventArgs e)=>MoveLayer(-1);
        private void MoveLayer(int delta)
        {
            if(SelectedSource==null)return;var source=SelectedSource;int from=SelectedScene.Sources.IndexOf(source),to=from+delta;
            if(to<0||to>=SelectedScene.Sources.Count)return;SelectedScene.Sources.RemoveAt(from);SelectedScene.Sources.Insert(to,source);changed=true;RefreshSources(source.Id);
        }
        private static Brush BrushFor(uint rgb)=>new SolidColorBrush(Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb));
        private void DrawLayout()
        {
            LayoutCanvas.Children.Clear();FramePreview.Visibility=Visibility.Collapsed;BackToLayoutButton.Visibility=Visibility.Collapsed;LayoutView.Visibility=Visibility.Visible;CanvasHint.Text="LAYOUT MAP · DRAG / RESIZE · DOUBLE-CLICK TO EDIT";
            if(SelectedScene==null)return;
            foreach(var source in SelectedScene.Sources)
            {
                var t=source.Transform??new SourceTransform();
                var box=new Border{Width=Math.Max(1,t.Width),Height=Math.Max(1,t.Height),BorderThickness=new Thickness(SelectedSource?.Id==source.Id?3:0),BorderBrush=(Brush)FindResource("AccentTextBrush"),Background=source.Kind==SceneSourceKind.Color?BrushFor(source.Color):source.Kind==SceneSourceKind.Text?Brushes.Transparent:new SolidColorBrush(Color.FromRgb(29,29,36)),Opacity=t.Visible?1:.3,Tag=source,Focusable=true,Cursor=Cursors.SizeAll};
                Canvas.SetLeft(box,t.X);Canvas.SetTop(box,t.Y);box.RenderTransform=new RotateTransform(t.Rotation);
                var grid=new Grid();box.Child=grid;
                if(source.Kind==SceneSourceKind.Image && System.IO.File.Exists(source.FilePath))
                {
                    try {var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=640;bitmap.UriSource=new Uri(System.IO.Path.GetFullPath(source.FilePath));bitmap.EndInit();bitmap.Freeze();grid.Children.Add(new Image{Source=bitmap,Stretch=Stretch.Uniform});}catch{}
                }
                if(source.Kind==SceneSourceKind.Text)
                {
                    var drawing=new DrawingGroup();
                    using(var context=drawing.Open())context.DrawText(new FormattedText(source.Text??"",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),source.FontSize,BrushFor(source.TextColor),1),new Point(0,0));
                    drawing.Freeze();grid.Children.Add(new Image{Source=new DrawingImage(drawing),Stretch=Stretch.Uniform,Margin=new Thickness(6)});
                }
                if(SelectedSource?.Id==source.Id || (source.Kind!=SceneSourceKind.Text&&source.Kind!=SceneSourceKind.Color&&source.Kind!=SceneSourceKind.Image))
                {
                    var label=new Border{Background=new SolidColorBrush(Color.FromArgb(220,12,12,16)),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(12,8,12,8),Child=new TextBlock{Text=source.Name+" · "+source.Kind,FontSize=32,Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis}};
                    grid.Children.Add(label);
                }
                if(SelectedSource?.Id==source.Id)
                {
                    var thumb=new Thumb{Width=18,Height=18,Background=(Brush)FindResource("AccentBrush"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Cursor=Cursors.SizeNWSE,ToolTip="Resize this layer"};
                    thumb.DragDelta+=(s,e)=>{t.Width=Math.Clamp(t.Width+(float)e.HorizontalChange,16,canvasWidth*2);t.Height=Math.Clamp(t.Height+(float)e.VerticalChange,16,canvasHeight*2);box.Width=t.Width;box.Height=t.Height;changed=true;};grid.Children.Add(thumb);
                }
                box.MouseLeftButtonDown+=(s,e)=>
                {
                    if(e.OriginalSource is Thumb)return;
                    if(e.ClickCount==2){SourcesList.SelectedItem=source;EditSource_Click(this,new RoutedEventArgs());e.Handled=true;return;}
                    if(SelectedSource?.Id!=source.Id)SourcesList.SelectedItem=source;
                    dragging=source;dragOrigin=e.GetPosition(LayoutCanvas);dragX=t.X;dragY=t.Y;LayoutCanvas.CaptureMouse();e.Handled=true;
                };
                box.KeyDown+=(s,e)=>{int step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?10:1;switch(e.Key){case Key.Left:t.X-=step;break;case Key.Right:t.X+=step;break;case Key.Up:t.Y-=step;break;case Key.Down:t.Y+=step;break;default:return;}Canvas.SetLeft(box,t.X);Canvas.SetTop(box,t.Y);changed=true;e.Handled=true;};
                LayoutCanvas.Children.Add(box);
            }
        }
        private async void Apply_Click(object sender,RoutedEventArgs e)=>await ApplyChanges();
        private async Task<bool> ApplyChanges()
        {
            if(working)return false;
            working=true;ApplyButton.IsEnabled=false;EditorWorkspace.IsEnabled=false;
            try
            {
                if(SelectedScene==null)throw new InvalidOperationException("Select a scene.");
                if(collection.Scenes.Any(s=>string.IsNullOrWhiteSpace(s.Name)))throw new InvalidOperationException("Give every scene a name.");
                if(!int.TryParse(DurationBox.Text,out int duration)||duration<50||duration>2000)throw new InvalidOperationException("Use a transition duration from 50 to 2000 milliseconds.");
                collection.SelectedSceneId=SelectedScene.Id;
                await apply(SceneLibrary.Clone(collection),(SceneTransition)TransitionPicker.SelectedIndex,duration);
                changed=false;EditorStatus.Text="Scene applied and saved. New captures use this scene collection.";return true;
            }
            catch(Exception ex){EditorStatus.Text=ex.Message;return false;}
            finally{working=false;ApplyButton.IsEnabled=true;EditorWorkspace.IsEnabled=true;}
        }
        private async void Preview_Click(object sender,RoutedEventArgs e)
        {
            if(!await ApplyChanges())return;working=true;EditorWorkspace.IsEnabled=false;ApplyButton.IsEnabled=false;
            try {var frame=await preview();var bitmap=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null,frame.BgraPixels,frame.Stride);bitmap.Freeze();FramePreview.Source=bitmap;FramePreview.Visibility=Visibility.Visible;LayoutView.Visibility=Visibility.Collapsed;BackToLayoutButton.Visibility=Visibility.Visible;CanvasHint.Text="CAPTURED FRAME · "+DateTime.Now.ToString("HH:mm:ss");EditorStatus.Text="One actual frame. Preview stays asleep between requests.";}
            catch(Exception ex){EditorStatus.Text="Preview failed: "+ex.Message;}
            finally{working=false;EditorWorkspace.IsEnabled=true;ApplyButton.IsEnabled=true;}
        }
        private void Layout_Click(object sender,RoutedEventArgs e)=>DrawLayout();
        private void Files_Click(object sender,RoutedEventArgs e)
        {
            var menu=new ContextMenu();
            var import=new MenuItem{Header="Import scene collection…"};import.Click+=(s,args)=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Rilmo scene collection|*.json",CheckFileExists=true};if(picker.ShowDialog(this)!=true)return;try{collection=SceneLibrary.Read(picker.FileName);canvasWidth=collection.CanvasWidth;canvasHeight=collection.CanvasHeight;LayoutCanvas.Width=canvasWidth;LayoutCanvas.Height=canvasHeight;changed=true;RefreshScenes(collection.SelectedSceneId);EditorStatus.Text="Imported for review. Browser sources connect only when applied or previewed.";}catch(Exception ex){EditorStatus.Text=ex.Message;}};
            var export=new MenuItem{Header="Export scene collection…"};export.Click+=(s,args)=>{var picker=new Microsoft.Win32.SaveFileDialog{Filter="Rilmo scene collection|*.json",FileName="Rilmo scenes.json",AddExtension=true,DefaultExt=".json",OverwritePrompt=true};if(picker.ShowDialog(this)!=true)return;try{SceneLibrary.Save(collection,picker.FileName);EditorStatus.Text="Scene collection exported.";}catch(Exception ex){EditorStatus.Text=ex.Message;}};
            menu.Items.Add(import);menu.Items.Add(export);menu.PlacementTarget=(Button)sender;menu.IsOpen=true;
        }
    }
}
