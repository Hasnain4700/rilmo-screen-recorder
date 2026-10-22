// SPDX-License-Identifier: GPL-2.0-or-later
// Independent deployment test source: synthetic NV12 only, never screen content.
// ABI constants are from Microsoft's Windows SDK mfidl.h/mfapi.h.
#pragma once
#include <mferror.h>
#include <ks.h>
#include <ksproxy.h>
#include <mutex>
#include <atomic>

static const GUID stream2Id={0xc5bc37d6,0x75c7,0x46a1,{0xa1,0x32,0x81,0xb5,0xf7,0x23,0xc2,0x0f}};
static const GUID allocatorId={0xda62b958,0x3a38,0x4a97,{0xbd,0x27,0x14,0x9c,0x64,0x0c,0x07,0x71}};
static const GUID ksControlId={0x28f54685,0x06fd,0x11d2,{0xb2,0x7a,0x00,0xa0,0xc9,0x22,0x31,0x96}};
static const GUID categoryKey={0x2939e7b8,0xa62e,0x4579,{0xb6,0x74,0xd4,0x07,0x3d,0xfa,0xbb,0xba}};
static const GUID streamIdKey={0x11bd5120,0xd124,0x446b,{0x88,0xe6,0x17,0x06,0x02,0x57,0xff,0xf9}};
static const GUID sharedKey={0x1cb378e9,0xb279,0x41d4,{0xaf,0x97,0x34,0xa2,0x43,0xe6,0x83,0x20}};
static const GUID colorKey={0x17145fd1,0x1b2b,0x423c,{0x80,0x01,0x2b,0x68,0x33,0xed,0x35,0x88}};
static const GUID captureCategory={0xfb6c4281,0x0353,0x11d1,{0x90,0x5f,0x00,0x00,0xc0,0xcc,0x16,0xba}};
// Narrow ABI adapters also compile with Zig's older MinGW headers.
struct ProbeStream2 : IMFMediaStream {
    virtual HRESULT STDMETHODCALLTYPE SetStreamState(int)=0;
    virtual HRESULT STDMETHODCALLTYPE GetStreamState(int*)=0;
};
struct ProbeAllocator : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE SetDefaultAllocator(DWORD,IUnknown*)=0;
    virtual HRESULT STDMETHODCALLTYPE GetAllocatorUsage(DWORD,DWORD*,int*)=0;
};
template<class T> struct Ptr {
    T *p=nullptr;
    ~Ptr(){if(p)p->Release();}
    T* operator->()const{return p;}
    operator T*()const{return p;}
    T** put(){return &p;}
};
#define TRY(call) do{HRESULT result=(call);if(FAILED(result))return result;}while(0)
#define EVENTS \
    HRESULT STDMETHODCALLTYPE GetEvent(DWORD f,IMFMediaEvent** e) override{return events?events->GetEvent(f,e):MF_E_SHUTDOWN;} \
    HRESULT STDMETHODCALLTYPE BeginGetEvent(IMFAsyncCallback*c,IUnknown*s) override{return events?events->BeginGetEvent(c,s):MF_E_SHUTDOWN;} \
    HRESULT STDMETHODCALLTYPE EndGetEvent(IMFAsyncResult*r,IMFMediaEvent**e) override{return events?events->EndGetEvent(r,e):MF_E_SHUTDOWN;} \
    HRESULT STDMETHODCALLTYPE QueueEvent(MediaEventType t,REFGUID g,HRESULT h,const PROPVARIANT*v) override{return events?events->QueueEventParamVar(t,g,h,v):MF_E_SHUTDOWN;}

class SyntheticStream final : public ProbeStream2 {
    LONG refs=1;
    std::recursive_mutex lock;
    Ptr<IMFMediaEventQueue> events;
    Ptr<IMFStreamDescriptor> descriptor;
    IMFMediaSource* parent=nullptr;
    int state=0; // MF_STREAM_STATE_STOPPED=0, PAUSED=1, RUNNING=2.
    bool dead=false;
    LONGLONG frame=0;
public:
    SyntheticStream(){InterlockedIncrement(&objects);}
    ~SyntheticStream(){Shutdown();InterlockedDecrement(&objects);}
    HRESULT Initialize(IMFMediaSource* source){
        TRY(MFCreateEventQueue(events.put()));
        Ptr<IMFMediaType> type;TRY(MFCreateMediaType(type.put()));
        TRY(type->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));
        TRY(type->SetGUID(MF_MT_SUBTYPE,MFVideoFormat_NV12));
        TRY(MFSetAttributeSize(type,MF_MT_FRAME_SIZE,640,360));
        TRY(MFSetAttributeRatio(type,MF_MT_FRAME_RATE,30,1));
        TRY(MFSetAttributeRatio(type,MF_MT_PIXEL_ASPECT_RATIO,1,1));
        TRY(type->SetUINT32(MF_MT_INTERLACE_MODE,MFVideoInterlace_Progressive));
        TRY(type->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT,TRUE));
        TRY(type->SetUINT32(MF_MT_FIXED_SIZE_SAMPLES,TRUE));
        TRY(type->SetUINT32(MF_MT_SAMPLE_SIZE,640*360*3/2));
        TRY(type->SetUINT32(MF_MT_DEFAULT_STRIDE,640));
        IMFMediaType* types[]={type};TRY(MFCreateStreamDescriptor(0,1,types,descriptor.put()));
        Ptr<IMFMediaTypeHandler> handler;TRY(descriptor->GetMediaTypeHandler(handler.put()));
        TRY(handler->SetCurrentMediaType(type));
        TRY(descriptor->SetGUID(categoryKey,captureCategory));
        TRY(descriptor->SetUINT32(streamIdKey,0));
        TRY(descriptor->SetUINT32(sharedKey,1));
        TRY(descriptor->SetUINT32(colorKey,1));
        parent=source;parent->AddRef();return S_OK;
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** out)override{
        if(!out)return E_POINTER;*out=nullptr;
        if(iid==IID_IUnknown||iid==IID_IMFMediaEventGenerator||iid==IID_IMFMediaStream||iid==stream2Id){*out=static_cast<ProbeStream2*>(this);AddRef();return S_OK;}return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef()override{return InterlockedIncrement(&refs);}
    ULONG STDMETHODCALLTYPE Release()override{LONG n=InterlockedDecrement(&refs);if(!n)delete this;return n;}
    EVENTS
    HRESULT STDMETHODCALLTYPE GetMediaSource(IMFMediaSource** out)override{
        std::lock_guard<std::recursive_mutex> guard(lock);if(!out)return E_POINTER;*out=nullptr;if(dead)return MF_E_SHUTDOWN;*out=parent;parent->AddRef();return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetStreamDescriptor(IMFStreamDescriptor** out)override{
        std::lock_guard<std::recursive_mutex> guard(lock);if(!out)return E_POINTER;*out=nullptr;if(dead)return MF_E_SHUTDOWN;*out=descriptor;descriptor->AddRef();return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetStreamState(int value)override{
        std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return MF_E_SHUTDOWN;if(value<0||value>2)return E_INVALIDARG;state=value;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetStreamState(int* out)override{
        std::lock_guard<std::recursive_mutex> guard(lock);if(!out)return E_POINTER;if(dead)return MF_E_SHUTDOWN;*out=state;return S_OK;
    }
    HRESULT Start(){std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return MF_E_SHUTDOWN;state=2;return events->QueueEventParamVar(MEStreamStarted,GUID_NULL,S_OK,nullptr);}
    HRESULT Stop(){std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return MF_E_SHUTDOWN;state=0;return events->QueueEventParamVar(MEStreamStopped,GUID_NULL,S_OK,nullptr);}
    void Shutdown(){std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return;dead=true;state=0;if(events)events->Shutdown();if(parent){auto old=parent;parent=nullptr;old->Release();}}
    HRESULT STDMETHODCALLTYPE RequestSample(IUnknown* token)override{
        std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return MF_E_SHUTDOWN;if(state!=2)return MF_E_INVALIDREQUEST;
        Ptr<IMFSample> sample;Ptr<IMFMediaBuffer> buffer;
        TRY(MFCreateSample(sample.put()));TRY(MFCreateMemoryBuffer(640*360*3/2,buffer.put()));
        BYTE* bytes=nullptr;TRY(buffer->Lock(&bytes,nullptr,nullptr));
        // Limited-range greyscale changes once per requested sample. No capture.
        memset(bytes,16+(frame%200),640*360);memset(bytes+640*360,128,640*360/2);
        buffer->Unlock();TRY(buffer->SetCurrentLength(640*360*3/2));
        TRY(sample->AddBuffer(buffer));TRY(sample->SetSampleTime(frame*10000000/30));
        TRY(sample->SetSampleDuration((frame+1)*10000000/30-frame*10000000/30));
        if(token)TRY(sample->SetUnknown(MFSampleExtension_Token,token));
        TRY(sample->SetUINT32(MFSampleExtension_CleanPoint,TRUE));++frame;
        return events->QueueEventParamUnk(MEMediaSample,GUID_NULL,S_OK,sample);
    }
};

class SyntheticSource final : public IMFMediaSourceEx,public IMFGetService,public IKsControl,public ProbeAllocator {
    LONG refs=1;
    std::recursive_mutex lock;
    Ptr<IMFMediaEventQueue> events;
    Ptr<IMFPresentationDescriptor> descriptor;
    Ptr<IMFAttributes> attributes;
    SyntheticStream* stream=nullptr;
    bool dead=false,announced=false;
public:
    SyntheticSource(){InterlockedIncrement(&objects);}
    ~SyntheticSource(){Shutdown();if(stream)stream->Release();InterlockedDecrement(&objects);}
    HRESULT Initialize(IMFAttributes* activation){
        TRY(MFCreateEventQueue(events.put()));TRY(MFCreateAttributes(attributes.put(),8));
        if(activation)TRY(activation->CopyAllItems(attributes));
        stream=new(std::nothrow)SyntheticStream;if(!stream)return E_OUTOFMEMORY;
        TRY(stream->Initialize(this));
        Ptr<IMFStreamDescriptor> sd;TRY(stream->GetStreamDescriptor(sd.put()));
        IMFStreamDescriptor* streams[]={sd};TRY(MFCreatePresentationDescriptor(1,streams,descriptor.put()));
        return descriptor->SelectStream(0);
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void**out)override{
        if(!out)return E_POINTER;*out=nullptr;
        if(iid==IID_IUnknown||iid==IID_IMFMediaEventGenerator||iid==IID_IMFMediaSource||iid==IID_IMFMediaSourceEx)*out=static_cast<IMFMediaSourceEx*>(this);
        else if(iid==IID_IMFGetService)*out=static_cast<IMFGetService*>(this);
        else if(iid==ksControlId)*out=static_cast<IKsControl*>(this);
        else if(iid==allocatorId)*out=static_cast<ProbeAllocator*>(this);
        else return E_NOINTERFACE;AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef()override{return InterlockedIncrement(&refs);}
    ULONG STDMETHODCALLTYPE Release()override{LONG n=InterlockedDecrement(&refs);if(!n)delete this;return n;}
    EVENTS
    HRESULT STDMETHODCALLTYPE GetCharacteristics(DWORD*out)override{if(!out)return E_POINTER;std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return MF_E_SHUTDOWN;*out=MFMEDIASOURCE_IS_LIVE;return S_OK;}
    HRESULT STDMETHODCALLTYPE CreatePresentationDescriptor(IMFPresentationDescriptor**out)override{if(!out)return E_POINTER;*out=nullptr;std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return MF_E_SHUTDOWN;return descriptor->Clone(out);}
    HRESULT STDMETHODCALLTYPE Start(IMFPresentationDescriptor*pd,const GUID*format,const PROPVARIANT*position)override{
        if(!pd||!position)return E_POINTER;std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return MF_E_SHUTDOWN;
        if(format&&*format!=GUID_NULL)return MF_E_UNSUPPORTED_TIME_FORMAT;
        if(position->vt!=VT_EMPTY&&(position->vt!=VT_I8||position->hVal.QuadPart!=0))return MF_E_INVALIDREQUEST;
        DWORD count=0;TRY(pd->GetStreamDescriptorCount(&count));if(count!=1)return MF_E_INVALIDREQUEST;
        BOOL selected=FALSE;Ptr<IMFStreamDescriptor> sd;TRY(pd->GetStreamDescriptorByIndex(0,&selected,sd.put()));
        DWORD id=1;TRY(sd->GetStreamIdentifier(&id));if(!selected||id!=0)return MF_E_INVALIDREQUEST;
        Ptr<IMFMediaTypeHandler> handler;Ptr<IMFMediaType> type;TRY(sd->GetMediaTypeHandler(handler.put()));TRY(handler->GetCurrentMediaType(type.put()));
        GUID subtype;UINT32 w,h;TRY(type->GetGUID(MF_MT_SUBTYPE,&subtype));TRY(MFGetAttributeSize(type,MF_MT_FRAME_SIZE,&w,&h));
        if(subtype!=MFVideoFormat_NV12||w!=640||h!=360)return MF_E_INVALIDMEDIATYPE;
        TRY(events->QueueEventParamUnk(announced?MEUpdatedStream:MENewStream,GUID_NULL,S_OK,static_cast<ProbeStream2*>(stream)));announced=true;
        TRY(stream->Start());return events->QueueEventParamVar(MESourceStarted,GUID_NULL,S_OK,position);
    }
    HRESULT STDMETHODCALLTYPE Stop()override{std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return MF_E_SHUTDOWN;TRY(stream->Stop());return events->QueueEventParamVar(MESourceStopped,GUID_NULL,S_OK,nullptr);}
    HRESULT STDMETHODCALLTYPE Pause()override{return MF_E_INVALID_STATE_TRANSITION;}
    HRESULT STDMETHODCALLTYPE Shutdown()override{
        std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return S_OK;dead=true;if(events)events->Shutdown();if(stream)stream->Shutdown();return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSourceAttributes(IMFAttributes**out)override{if(!out)return E_POINTER;*out=nullptr;std::lock_guard<std::recursive_mutex> guard(lock);if(dead)return MF_E_SHUTDOWN;*out=attributes;attributes->AddRef();return S_OK;}
    HRESULT STDMETHODCALLTYPE GetStreamAttributes(DWORD id,IMFAttributes**out)override{if(!out)return E_POINTER;*out=nullptr;if(id)return MF_E_INVALIDSTREAMNUMBER;Ptr<IMFStreamDescriptor>sd;TRY(stream->GetStreamDescriptor(sd.put()));return sd->QueryInterface(IID_IMFAttributes,(void**)out);}
    HRESULT STDMETHODCALLTYPE SetD3DManager(IUnknown*)override{return S_OK;} // CPU NV12 test frames.
    HRESULT STDMETHODCALLTYPE GetService(REFGUID,REFIID,void**out)override{if(!out)return E_POINTER;*out=nullptr;return MF_E_UNSUPPORTED_SERVICE;}
    HRESULT STDMETHODCALLTYPE KsProperty(PKSPROPERTY,ULONG,LPVOID,ULONG,ULONG*out)noexcept override{if(!out)return E_POINTER;*out=0;return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);}
    HRESULT STDMETHODCALLTYPE KsMethod(PKSMETHOD,ULONG,LPVOID,ULONG,ULONG*out)noexcept override{if(!out)return E_POINTER;*out=0;return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);}
    HRESULT STDMETHODCALLTYPE KsEvent(PKSEVENT,ULONG,LPVOID,ULONG,ULONG*out)noexcept override{if(out)*out=0;return HRESULT_FROM_WIN32(ERROR_SET_NOT_FOUND);}
    HRESULT STDMETHODCALLTYPE SetDefaultAllocator(DWORD id,IUnknown*)override{return id?MF_E_INVALIDSTREAMNUMBER:S_OK;}
    HRESULT STDMETHODCALLTYPE GetAllocatorUsage(DWORD id,DWORD*input,int*usage)override{if(!input||!usage)return E_POINTER;if(id)return MF_E_INVALIDSTREAMNUMBER;*input=0;*usage=1;return S_OK;}
};
#undef EVENTS
#undef TRY
