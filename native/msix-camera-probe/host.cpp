// SPDX-License-Identifier: GPL-2.0-or-later
// Test-only program; no production registration, captures, network, or frames.
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <devpropdef.h>
#include <appmodel.h>
#include <sddl.h>
#include <stdio.h>
#include "probe-identity.h"

// ABI of the documented IMFVirtualCamera interface from mfvirtualcamera.h.
// Only methods through GetMediaSource and Shutdown are called by this probe.
struct SyncObject;
struct VirtualCamera : IMFAttributes {
    virtual HRESULT STDMETHODCALLTYPE AddDeviceSourceInfo(LPCWSTR)=0;
    virtual HRESULT STDMETHODCALLTYPE AddProperty(const DEVPROPKEY*,DEVPROPTYPE,const BYTE*,ULONG)=0;
    virtual HRESULT STDMETHODCALLTYPE AddRegistryEntry(LPCWSTR,LPCWSTR,DWORD,const BYTE*,ULONG)=0;
    virtual HRESULT STDMETHODCALLTYPE Start(IMFAsyncCallback*)=0;
    virtual HRESULT STDMETHODCALLTYPE Stop()=0;
    virtual HRESULT STDMETHODCALLTYPE Remove()=0;
    virtual HRESULT STDMETHODCALLTYPE GetMediaSource(IMFMediaSource**)=0;
    virtual HRESULT STDMETHODCALLTYPE SendCameraProperty(REFGUID,ULONG,ULONG,void*,ULONG,void*,ULONG,ULONG*)=0;
    virtual HRESULT STDMETHODCALLTYPE CreateSyncEvent(REFGUID,ULONG,ULONG,HANDLE,SyncObject**)=0;
    virtual HRESULT STDMETHODCALLTYPE CreateSyncSemaphore(REFGUID,ULONG,ULONG,HANDLE,LONG,SyncObject**)=0;
    virtual HRESULT STDMETHODCALLTYPE Shutdown()=0;
};
using CreateCamera=HRESULT(WINAPI*)(int,int,int,LPCWSTR,LPCWSTR,const GUID*,ULONG,VirtualCamera**);
using CheckSupport=HRESULT(WINAPI*)(int,BOOL*);
static HANDLE marker(const wchar_t *name){
    PSECURITY_DESCRIPTOR descriptor=nullptr;
    // Only this CI user, Local Service (camera host), and SYSTEM may signal.
    if(!ConvertStringSecurityDescriptorToSecurityDescriptorW(L"D:(A;;GA;;;OW)(A;;0x100002;;;LS)(A;;GA;;;SY)",SDDL_REVISION_1,&descriptor,nullptr))return nullptr;
    SECURITY_ATTRIBUTES attributes={sizeof(attributes),descriptor,FALSE};
    HANDLE event=CreateEventW(&attributes,TRUE,FALSE,name);LocalFree(descriptor);return event;
}
int wmain(int argc,wchar_t **argv){
    FILE *out=stdout;
    if(argc==3&&wcscmp(argv[1],L"--report")==0){out=_wfopen(argv[2],L"wb");if(!out)return 2;}
    UINT32 count=0;LONG identity=GetCurrentPackageFullName(&count,nullptr);
    fprintf(out,"PACKAGE_IDENTITY=%s\n",identity==ERROR_INSUFFICIENT_BUFFER?"true":"false");fflush(out);
    HRESULT co=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    HRESULT mf=MFStartup(MF_VERSION,MFSTARTUP_FULL);fprintf(out,"MFSTARTUP=0x%08lx\n",(unsigned long)mf);
    HANDLE factory=marker(PROBE_FACTORY_EVENT),activation=marker(PROBE_ACTIVATION_EVENT);
    HMODULE module=LoadLibraryExW(L"mfsensorgroup.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    auto supported=module?(CheckSupport)GetProcAddress(module,"MFIsVirtualCameraTypeSupported"):nullptr;
    auto create=module?(CreateCamera)GetProcAddress(module,"MFCreateVirtualCamera"):nullptr;
    fprintf(out,"WINDOWS_CAMERA_API=%s\n",create&&supported?"true":"false");
    BOOL isSupported=FALSE;HRESULT status=supported?supported(0,&isSupported):HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND);
    fprintf(out,"TYPE_SUPPORT_HRESULT=0x%08lx\nTYPE_SUPPORTED=%s\n",(unsigned long)status,isSupported?"true":"false");
    IMFActivate *direct=nullptr;
    status=CoCreateInstance(PROBE_CLSID,nullptr,CLSCTX_INPROC_SERVER,IID_IMFActivate,(void**)&direct);
    fprintf(out,"UNPACKAGED_COM_HRESULT=0x%08lx\n",(unsigned long)status);
    if(direct)direct->Release();
    if(factory)ResetEvent(factory);if(activation)ResetEvent(activation);
    VirtualCamera *camera=nullptr;
    HRESULT made=create?create(0,0,0,L"Rilmo package activation probe",PROBE_CLSID_STRING,nullptr,0,&camera):HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND);
    fprintf(out,"CREATE_HRESULT=0x%08lx\n",(unsigned long)made);fflush(out);
    if(SUCCEEDED(made)&&camera){
        status=camera->Start(nullptr);fprintf(out,"START_HRESULT=0x%08lx\n",(unsigned long)status);fflush(out);
        IMFMediaSource *source=nullptr;
        if(SUCCEEDED(status)){HRESULT got=camera->GetMediaSource(&source);fprintf(out,"GET_SOURCE_HRESULT=0x%08lx\n",(unsigned long)got);if(source){source->Shutdown();source->Release();}}
        if(factory)WaitForSingleObject(factory,1500);
        fprintf(out,"SERVICE_LOADED_FACTORY=%s\n",factory&&WaitForSingleObject(factory,0)==WAIT_OBJECT_0?"true":"false");
        fprintf(out,"SERVICE_ACTIVATED_SOURCE=%s\n",activation&&WaitForSingleObject(activation,0)==WAIT_OBJECT_0?"true":"false");
        HRESULT stopped=camera->Stop(),removed=camera->Remove(),shutdown=camera->Shutdown();
        fprintf(out,"STOP_HRESULT=0x%08lx\nREMOVE_HRESULT=0x%08lx\nSHUTDOWN_HRESULT=0x%08lx\n",(unsigned long)stopped,(unsigned long)removed,(unsigned long)shutdown);
        camera->Release();
    }
    if(factory)CloseHandle(factory);if(activation)CloseHandle(activation);
    if(module)FreeLibrary(module);if(SUCCEEDED(mf))MFShutdown();if(SUCCEEDED(co))CoUninitialize();
    fprintf(out,"MEDIA_STREAM_TESTED=false\n");if(out!=stdout)fclose(out);
    return 0;
}
