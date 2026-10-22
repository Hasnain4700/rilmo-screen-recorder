using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Rilmo
{
    public sealed class HotkeyBinding
    {
        public uint Modifiers { get; set; }
        public uint KeyCode { get; set; }
        public static readonly string[] Actions={"Start / stop recording","Pause / resume","Quick controls","Screenshot","Replay / save clip"};
        public static HotkeyBinding[] Defaults()=>new uint[]{0x52,0x50,0x51,0x53,0x42}.Select(k=>new HotkeyBinding{Modifiers=6,KeyCode=k}).ToArray();
        public HotkeyBinding Copy()=>new HotkeyBinding{Modifiers=Modifiers,KeyCode=KeyCode};
        public override string ToString()
        {
            if(KeyCode==0)return "Disabled";
            string key=KeyInterop.KeyFromVirtualKey((int)KeyCode).ToString();
            return ((Modifiers&2)!=0?"Ctrl + ":"")+((Modifiers&1)!=0?"Alt + ":"")+((Modifiers&4)!=0?"Shift + ":"")+key;
        }
        public static void Validate(HotkeyBinding[] bindings)
        {
            if(bindings==null||bindings.Length!=5)throw new ArgumentException("Choose five shortcut bindings.");
            var used=new HashSet<string>();
            foreach(var binding in bindings)
            {
                if(binding==null || binding.Modifiers>7 || binding.KeyCode>254)throw new ArgumentException("Invalid shortcut.");
                if(binding.KeyCode==0)continue;
                bool function=binding.KeyCode>=0x70&&binding.KeyCode<=0x87;
                bool letterOrDigit=binding.KeyCode>=0x30&&binding.KeyCode<=0x5A;
                if(!function&&!letterOrDigit)throw new ArgumentException("Use a letter, number or F1–F24.");
                if(!function&&(binding.Modifiers&3)==0)throw new ArgumentException("Include Ctrl or Alt with a letter or number.");
                if(binding.Modifiers==1&&binding.KeyCode==0x73)throw new ArgumentException("Alt + F4 is reserved for closing windows.");
                if(!used.Add(binding.Modifiers+":"+binding.KeyCode))throw new ArgumentException("Each enabled action needs a different shortcut.");
            }
        }
    }
}
