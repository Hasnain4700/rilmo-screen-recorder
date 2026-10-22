using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Rilmo
{
    public partial class MainWindow
    {
        private HotkeyBinding[] pendingHotkeys;
        private readonly TextBox[] hotkeyBoxes=new TextBox[5];
        private void InitializeHotkeyEditors()
        {
            pendingHotkeys=settings.Hotkeys.Select(h=>h.Copy()).ToArray();
            for(int i=0;i<5;i++)
            {
                int index=i;
                var row=new Grid{Margin=new Thickness(0,4,0,4)};
                row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220)});
                row.Children.Add(new TextBlock{Text=HotkeyBinding.Actions[i],VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,16,0)});
                var box=new TextBox{Text=pendingHotkeys[i].ToString(),IsReadOnly=true,Cursor=Cursors.Hand};hotkeyBoxes[i]=box;
                Grid.SetColumn(box,1);row.Children.Add(box);HotkeyEditors.Children.Add(row);AutomationProperties.SetName(box,HotkeyBinding.Actions[i]+" shortcut");
                box.GotKeyboardFocus+=(s,e)=>{SuspendHotkeys();box.SelectAll();HotkeyStatus.Text="Press Ctrl/Alt + a letter or number, or an F key. Escape cancels this edit.";};
                box.LostKeyboardFocus+=(s,e)=>Dispatcher.BeginInvoke(new Action(()=>{if(!HotkeyEditors.IsKeyboardFocusWithin)RegisterConfiguredHotkeys();}));
                box.PreviewKeyDown+=(s,e)=>
                {
                    var key=e.Key==Key.System?e.SystemKey:e.Key;
                    if(key==Key.Tab)return;
                    e.Handled=true;
                    if(key==Key.Escape){pendingHotkeys[index]=settings.Hotkeys[index].Copy();box.Text=pendingHotkeys[index].ToString();Keyboard.ClearFocus();RegisterConfiguredHotkeys();return;}
                    if(key==Key.Back||key==Key.Delete){pendingHotkeys[index]=new HotkeyBinding();box.Text="Disabled";return;}
                    if(key==Key.LeftCtrl||key==Key.RightCtrl||key==Key.LeftShift||key==Key.RightShift||key==Key.LeftAlt||key==Key.RightAlt||key==Key.LWin||key==Key.RWin)return;
                    if((Keyboard.Modifiers&ModifierKeys.Windows)!=0){HotkeyStatus.Text="Windows-key shortcuts are reserved for Windows.";return;}
                    uint modifiers=0;if((Keyboard.Modifiers&ModifierKeys.Control)!=0)modifiers|=2;if((Keyboard.Modifiers&ModifierKeys.Alt)!=0)modifiers|=1;if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)modifiers|=4;
                    pendingHotkeys[index]=new HotkeyBinding{Modifiers=modifiers,KeyCode=(uint)KeyInterop.VirtualKeyFromKey(key)};box.Text=pendingHotkeys[index].ToString();HotkeyStatus.Text="Press Save shortcuts to apply.";
                };
            }
        }
        private void SuspendHotkeys(){if(hwndSource!=null)for(int i=1;i<=5;i++)UnregisterHotKey(hwndSource.Handle,i);}
        private bool TryRegisterHotkeys(HotkeyBinding[] bindings,out string unavailable)
        {
            unavailable="";
            if(renderOnly||automatedTest||hwndSource==null)return true;
            for(int i=0;i<bindings.Length;i++)
                if(bindings[i].KeyCode!=0&&!RegisterHotKey(hwndSource.Handle,i+1,bindings[i].Modifiers|0x4000,bindings[i].KeyCode))
                    unavailable+=(unavailable.Length>0?", ":"")+HotkeyBinding.Actions[i];
            return unavailable.Length==0;
        }
        private void RegisterConfiguredHotkeys()
        {
            SuspendHotkeys();
            if(!TryRegisterHotkeys(settings.Hotkeys,out string unavailable))HotkeyStatus.Text="Shortcut in use: "+unavailable+". Choose another combination.";
        }
        private void SaveHotkeys_Click(object sender,RoutedEventArgs e)
        {
            var old=settings.Hotkeys;
            try
            {
                HotkeyBinding.Validate(pendingHotkeys);SuspendHotkeys();
                if(!TryRegisterHotkeys(pendingHotkeys,out string unavailable))throw new InvalidOperationException("Already in use: "+unavailable+". Previous shortcuts restored.");
                settings.Hotkeys=pendingHotkeys.Select(h=>h.Copy()).ToArray();if(!renderOnly&&!automatedTest)settings.Save();
                HotkeyStatus.Text="Shortcuts saved. They work while Rilmo is running.";
            }
            catch(Exception ex){settings.Hotkeys=old;RegisterConfiguredHotkeys();HotkeyStatus.Text=ex.Message;}
        }
        private void ResetHotkeys_Click(object sender,RoutedEventArgs e)
        {
            pendingHotkeys=HotkeyBinding.Defaults();for(int i=0;i<5;i++)hotkeyBoxes[i].Text=pendingHotkeys[i].ToString();HotkeyStatus.Text="Defaults selected. Press Save shortcuts to apply.";
        }
    }
}
