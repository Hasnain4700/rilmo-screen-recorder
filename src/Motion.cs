// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Rilmo
{
    public static class Motion
    {
        internal static bool Enabled {get;set;}=true;
        internal static bool CaptureActive {get;set;}
        internal static bool CanAnimate => Enabled && !CaptureActive && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && (RenderCapability.Tier>>16)>0;
        public static readonly DependencyProperty InteractiveProperty=DependencyProperty.RegisterAttached("Interactive",typeof(bool),typeof(Motion),new PropertyMetadata(false,Changed));
        public static bool GetInteractive(DependencyObject value)=>(bool)value.GetValue(InteractiveProperty);
        public static void SetInteractive(DependencyObject value,bool enabled)=>value.SetValue(InteractiveProperty,enabled);
        private static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs args)
        {
            if(value is not Button button) return;
            if((bool)args.NewValue) { button.PreviewMouseLeftButtonDown+=Press;button.PreviewMouseLeftButtonUp+=Release;button.MouseLeave+=Leave; }
            else {button.PreviewMouseLeftButtonDown-=Press;button.PreviewMouseLeftButtonUp-=Release;button.MouseLeave-=Leave;}
        }
        private static void Press(object sender,System.Windows.Input.MouseButtonEventArgs e)=>Scale((Button)sender,0.99);
        private static void Release(object sender,System.Windows.Input.MouseButtonEventArgs e)=>Scale((Button)sender,1);
        private static void Leave(object sender,System.Windows.Input.MouseEventArgs e)=>Scale((Button)sender,1);
        private static void Scale(Button target,double value)
        {
            if(target.RenderTransform is not ScaleTransform scale) {scale=new ScaleTransform(1,1);target.RenderTransformOrigin=new Point(.5,.5);target.RenderTransform=scale;}
            if(!CanAnimate) {scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);scale.ScaleX=scale.ScaleY=1;return;}
            double from=scale.ScaleX;scale.ScaleX=value;scale.ScaleY=value;
            var animation=new DoubleAnimation(from,value,TimeSpan.FromMilliseconds(150)) { EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop };
            Timeline.SetDesiredFrameRate(animation,30);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,animation,HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,animation,HandoffBehavior.SnapshotAndReplace);
        }
        internal static void Reveal(FrameworkElement target,bool captureActive=false)
        {
            target.BeginAnimation(UIElement.OpacityProperty,null);target.Opacity=1;
            if(!CanAnimate || captureActive || !target.IsVisible) return;
            var animation=new DoubleAnimation(.35,1,TimeSpan.FromMilliseconds(170)) {EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
            Timeline.SetDesiredFrameRate(animation,30);
            target.BeginAnimation(UIElement.OpacityProperty,animation,HandoffBehavior.SnapshotAndReplace);
        }
    }
}
