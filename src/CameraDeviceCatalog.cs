// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Rilmo
{
    internal sealed class CameraDevice
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public bool IsAvailable { get; private set; }
        public CameraDevice(string id,string name,bool available=true) { Id=id;Name=name;IsAvailable=available; }
        public override string ToString() => Name;
    }
    internal static class CameraDeviceCatalog
    {
        [ComImport,Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum { [PreserveSig] int CreateClassEnumerator([In] ref Guid category,out IEnumMoniker monikers,int flags); }
        [ComImport,Guid("55272A00-42CB-11CE-8135-00AA004BB851"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig] int Read([MarshalAs(UnmanagedType.LPWStr)] string name,[MarshalAs(UnmanagedType.Struct)] out object value,IntPtr errorLog);
            [PreserveSig] int Write([MarshalAs(UnmanagedType.LPWStr)] string name,[MarshalAs(UnmanagedType.Struct)] ref object value);
        }
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved,uint flags);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        private static void Release(object value) { if (value!=null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        private static string Read(IPropertyBag bag,string key) { return bag.Read(key,out object value,IntPtr.Zero)>=0 ? value as string : null; }
        private static string Encode(string value) => (value??"").Replace("#","#22").Replace(":","#3A");
        public static CameraDevice[] Enumerate()
        {
            int com=CoInitializeEx(IntPtr.Zero,0);
            if (com<0 && com!=unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(com);
            object enumeratorObject=null; IEnumMoniker monikers=null;
            var result=new List<CameraDevice>();
            try
            {
                enumeratorObject=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86"),true));
                var enumerator=(ICreateDevEnum)enumeratorObject;
                var category=new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
                int status=enumerator.CreateClassEnumerator(ref category,out monikers,0);
                if(status==1 || monikers==null) return result.ToArray();
                Marshal.ThrowExceptionForHR(status);
                var item=new IMoniker[1];
                while(monikers.Next(1,item,IntPtr.Zero)==0)
                {
                    object storage=null;
                    try
                    {
                        var bagId=new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
                        item[0].BindToStorage(null,null,ref bagId,out storage);
                        var bag=(IPropertyBag)storage;
                        string name=Read(bag,"FriendlyName")??Read(bag,"Description");
                        string path=Read(bag,"DevicePath")??"";
                        if(!string.IsNullOrEmpty(name)) result.Add(new CameraDevice(Encode(name)+":"+Encode(path),name));
                    }
                    catch(COMException) { }
                    finally { Release(storage);Release(item[0]);item[0]=null; }
                }
                return result.ToArray();
            }
            finally { Release(monikers);Release(enumeratorObject);if(com>=0)CoUninitialize(); }
        }
    }
}
