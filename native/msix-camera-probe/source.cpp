// SPDX-License-Identifier: GPL-2.0-or-later
// Test-only activation object and synthetic media source. No screen capture.
// A named event proves whether the camera service actually loaded this package.
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <new>
#include "probe-identity.h"

static LONG objects=0;
#include "synthetic.h"
static void signal(const wchar_t *name) {
    // Prove activation by a service identity, not a client-side CoCreate call.
    HANDLE token=nullptr;BYTE info[256];DWORD needed=0;
    if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token))return;
    bool service=GetTokenInformation(token,TokenUser,info,sizeof(info),&needed)&&
        (IsWellKnownSid(((TOKEN_USER*)info)->User.Sid,WinLocalServiceSid)||IsWellKnownSid(((TOKEN_USER*)info)->User.Sid,WinLocalSystemSid));
    CloseHandle(token);if(!service)return;
    wchar_t executable[MAX_PATH]={};GetModuleFileNameW(nullptr,executable,MAX_PATH);
    const wchar_t* leaf=wcsrchr(executable,L'\\');leaf=leaf?leaf+1:executable;
    if(_wcsicmp(leaf,L"CameraProbe.exe")==0)return;
    HANDLE event=OpenEventW(EVENT_MODIFY_STATE,FALSE,name);
    if(event){SetEvent(event);CloseHandle(event);}
}
class Activation final : public IMFActivate {
    LONG refs=1;
    IMFAttributes *store=nullptr;
    SyntheticSource *source=nullptr;
    std::recursive_mutex lock;
public:
    Activation(){InterlockedIncrement(&objects);}
    ~Activation(){ShutdownObject();if(store)store->Release();InterlockedDecrement(&objects);}
    HRESULT Initialize(){return MFCreateAttributes(&store,4);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void **out) override {
        if(!out)return E_POINTER;*out=nullptr;
        if(iid==IID_IUnknown||iid==IID_IMFActivate||iid==IID_IMFAttributes){*out=static_cast<IMFActivate*>(this);AddRef();return S_OK;}
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return InterlockedIncrement(&refs);}
    ULONG STDMETHODCALLTYPE Release() override{LONG n=InterlockedDecrement(&refs);if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE ActivateObject(REFIID iid,void **out) override {
        if(!out)return E_POINTER;*out=nullptr;signal(PROBE_ACTIVATION_EVENT);
        std::lock_guard<std::recursive_mutex> guard(lock);
        if(!source){source=new(std::nothrow)SyntheticSource;if(!source)return E_OUTOFMEMORY;
            HRESULT status=source->Initialize(store);if(FAILED(status)){source->Shutdown();source->Release();source=nullptr;return status;}}
        return source->QueryInterface(iid,out);
    }
    HRESULT STDMETHODCALLTYPE ShutdownObject() override{std::lock_guard<std::recursive_mutex> guard(lock);if(source){source->Shutdown();source->Release();source=nullptr;}return S_OK;}
    HRESULT STDMETHODCALLTYPE DetachObject() override{std::lock_guard<std::recursive_mutex> guard(lock);if(source){source->Release();source=nullptr;}return S_OK;}
#define FORWARD(name,args,call) HRESULT STDMETHODCALLTYPE name args override{return store->name call;}
    FORWARD(GetItem,(REFGUID k,PROPVARIANT*v),(k,v))
    FORWARD(GetItemType,(REFGUID k,MF_ATTRIBUTE_TYPE*t),(k,t))
    FORWARD(CompareItem,(REFGUID k,REFPROPVARIANT v,BOOL*r),(k,v,r))
    FORWARD(Compare,(IMFAttributes*a,MF_ATTRIBUTES_MATCH_TYPE t,BOOL*r),(a,t,r))
    FORWARD(GetUINT32,(REFGUID k,UINT32*v),(k,v))
    FORWARD(GetUINT64,(REFGUID k,UINT64*v),(k,v))
    FORWARD(GetDouble,(REFGUID k,double*v),(k,v))
    FORWARD(GetGUID,(REFGUID k,GUID*v),(k,v))
    FORWARD(GetStringLength,(REFGUID k,UINT32*v),(k,v))
    FORWARD(GetString,(REFGUID k,LPWSTR v,UINT32 c,UINT32*n),(k,v,c,n))
    FORWARD(GetAllocatedString,(REFGUID k,LPWSTR*v,UINT32*n),(k,v,n))
    FORWARD(GetBlobSize,(REFGUID k,UINT32*n),(k,n))
    FORWARD(GetBlob,(REFGUID k,UINT8*v,UINT32 c,UINT32*n),(k,v,c,n))
    FORWARD(GetAllocatedBlob,(REFGUID k,UINT8**v,UINT32*n),(k,v,n))
    FORWARD(GetUnknown,(REFGUID k,REFIID i,LPVOID*v),(k,i,v))
    FORWARD(SetItem,(REFGUID k,REFPROPVARIANT v),(k,v))
    FORWARD(DeleteItem,(REFGUID k),(k))
    FORWARD(DeleteAllItems,(),())
    FORWARD(SetUINT32,(REFGUID k,UINT32 v),(k,v))
    FORWARD(SetUINT64,(REFGUID k,UINT64 v),(k,v))
    FORWARD(SetDouble,(REFGUID k,double v),(k,v))
    FORWARD(SetGUID,(REFGUID k,REFGUID v),(k,v))
    FORWARD(SetString,(REFGUID k,LPCWSTR v),(k,v))
    FORWARD(SetBlob,(REFGUID k,const UINT8*v,UINT32 n),(k,v,n))
    FORWARD(SetUnknown,(REFGUID k,IUnknown*v),(k,v))
    FORWARD(LockStore,(),())
    FORWARD(UnlockStore,(),())
    FORWARD(GetCount,(UINT32*n),(n))
    FORWARD(GetItemByIndex,(UINT32 i,GUID*k,PROPVARIANT*v),(i,k,v))
    FORWARD(CopyAllItems,(IMFAttributes*d),(d))
#undef FORWARD
};
class Factory final : public IClassFactory {
    LONG refs=1;
public:
    Factory(){InterlockedIncrement(&objects);}
    ~Factory(){InterlockedDecrement(&objects);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void **out) override {
        if(!out)return E_POINTER;*out=nullptr;
        if(iid==IID_IUnknown||iid==IID_IClassFactory){*out=static_cast<IClassFactory*>(this);AddRef();return S_OK;}
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return InterlockedIncrement(&refs);}
    ULONG STDMETHODCALLTYPE Release() override{LONG n=InterlockedDecrement(&refs);if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown *outer,REFIID iid,void **out) override {
        if(!out)return E_POINTER;*out=nullptr;if(outer)return CLASS_E_NOAGGREGATION;
        auto instance=new(std::nothrow) Activation;if(!instance)return E_OUTOFMEMORY;
        HRESULT result=instance->Initialize();if(SUCCEEDED(result))result=instance->QueryInterface(iid,out);
        instance->Release();return result;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override {if(lock)InterlockedIncrement(&objects);else InterlockedDecrement(&objects);return S_OK;}
};
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid,REFIID iid,void **out){
    if(!out)return E_POINTER;*out=nullptr;if(clsid!=PROBE_CLSID)return CLASS_E_CLASSNOTAVAILABLE;
    signal(PROBE_FACTORY_EVENT);
    auto factory=new(std::nothrow) Factory;if(!factory)return E_OUTOFMEMORY;
    HRESULT result=factory->QueryInterface(iid,out);factory->Release();return result;
}
extern "C" HRESULT __stdcall DllCanUnloadNow(){return objects?S_FALSE:S_OK;}
