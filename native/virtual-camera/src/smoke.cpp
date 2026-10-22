// SPDX-License-Identifier: GPL-2.0-or-later
// DirectShow integration test. --registered activates the installed device
// through the same category enumeration and moniker binding used by clients.
#include <windows.h>
#include <dshow.h>
#include <qedit.h>
#include <stdio.h>
#include <stdint.h>
#include <vector>
#include "virtualcam-guid.h"
#include "shared-memory-queue.h"
#include <source/ComPtr.hpp>
static const GUID CLSID_RilmoTestNullRenderer={0xc1f400a4,0x3f08,0x11d3,{0x9f,0x0b,0x00,0x60,0x08,0x03,0x9e,0x37}};

static void check(HRESULT result, const char *step) { if (FAILED(result)) { fprintf(stderr, "%s failed: 0x%08lx\n",step,(unsigned long)result); ExitProcess(2); } }
class Samples : public ISampleGrabberCB {
    LONG refs = 1;
public:
    volatile LONG frames=0, pattern=0, slate=0;
    STDMETHODIMP QueryInterface(REFIID iid, void **out) { if(!out) return E_POINTER; *out=nullptr; if(iid==IID_IUnknown||iid==IID_ISampleGrabberCB){*out=this; AddRef(); return S_OK;} return E_NOINTERFACE; }
    STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&refs); }
    STDMETHODIMP_(ULONG) Release() { return InterlockedDecrement(&refs); }
    STDMETHODIMP SampleCB(double, IMediaSample*) { return S_OK; }
    STDMETHODIMP BufferCB(double, BYTE *buffer, long length) {
        InterlockedIncrement(&frames);
        if(length>642*360 && buffer[0]==43 && buffer[641]==43 && buffer[642*360-1]==43 && buffer[642*360]==125) InterlockedIncrement(&pattern);
        if(length>640 && buffer[0]==24) InterlockedIncrement(&slate);
        return S_OK;
    }
};
static IPin *pin(IBaseFilter *filter, PIN_DIRECTION direction) {
    IEnumPins *enumerator=nullptr; check(filter->EnumPins(&enumerator),"EnumPins");
    IPin *next=nullptr; while(enumerator->Next(1,&next,nullptr)==S_OK) { PIN_DIRECTION actual; next->QueryDirection(&actual); if(actual==direction) {enumerator->Release(); return next;} next->Release(); }
    enumerator->Release(); fprintf(stderr,"Pin missing\n"); ExitProcess(2); return nullptr;
}
static IBaseFilter *registered_camera() {
    ICreateDevEnum *devices=nullptr;
    check(CoCreateInstance(CLSID_SystemDeviceEnum,nullptr,CLSCTX_INPROC_SERVER,IID_ICreateDevEnum,(void**)&devices),"SystemDeviceEnum");
    IEnumMoniker *enumerator=nullptr;
    HRESULT result=devices->CreateClassEnumerator(CLSID_VideoInputDeviceCategory,&enumerator,0);
    devices->Release();
    if(result!=S_OK) {fprintf(stderr,"Camera category is empty: 0x%08lx\n",(unsigned long)result);ExitProcess(9);}
    IMoniker *moniker=nullptr; IBaseFilter *camera=nullptr; unsigned matches=0;
    while(enumerator->Next(1,&moniker,nullptr)==S_OK) {
        IPropertyBag *bag=nullptr;
        if(SUCCEEDED(moniker->BindToStorage(nullptr,nullptr,IID_IPropertyBag,(void**)&bag))) {
            VARIANT id;VariantInit(&id);
            if(SUCCEEDED(bag->Read(L"CLSID",&id,nullptr))&&id.vt==VT_BSTR) {
                CLSID clsid={};
                if(SUCCEEDED(CLSIDFromString(id.bstrVal,&clsid))&&clsid==CLSID_OBS_VirtualVideo) {
                    ++matches;
                    if(!camera) check(moniker->BindToObject(nullptr,nullptr,IID_IBaseFilter,(void**)&camera),"Bind registered camera");
                }
            }
            VariantClear(&id);bag->Release();
        }
        moniker->Release();
    }
    enumerator->Release();
    if(matches!=1||!camera) {fprintf(stderr,"Expected one registered Rilmo camera; found %u\n",matches);ExitProcess(9);}
    printf("CATEGORY_ENUMERATION=true; MONIKER_BIND=true\n");
    return camera;
}
int wmain(int argc, wchar_t **argv)
{
    setvbuf(stdout,nullptr,_IONBF,0);
    if(argc<2) return 1;
    bool registered=false,self=true;
    for(int i=2;i<argc;i++) {
        if(wcscmp(argv[i],L"--consume-only")==0)self=false;
        else if(wcscmp(argv[i],L"--registered")==0)registered=true;
        else {fprintf(stderr,"Unknown test option\n");return 1;}
    }
    check(CoInitializeEx(nullptr,COINIT_MULTITHREADED),"CoInitializeEx");
    video_queue_t *writer=nullptr;
    const uint32_t width=642,height=360,pitch=672;
    std::vector<uint8_t> luma(pitch*height,43),chroma(pitch*height/2,125);
    uint8_t *planes[2]={luma.data(),chroma.data()}; uint32_t strides[2]={pitch,pitch};
    if(self) {
        writer=video_queue_create(width,height,333333);
        if(!writer) {fprintf(stderr,"Rilmo queue unavailable; another producer may be active\n"); return 3;}
        if(video_queue_create(width,height,333333)) {fprintf(stderr,"Duplicate producer accepted\n");return 4;}
        video_queue_write(writer,planes,strides,0);
    }
    HMODULE dll=LoadLibraryW(argv[1]); if(!dll){fprintf(stderr,"LoadLibrary error %lu\n",GetLastError());return 5;}
    printf("DLL_LOADED=true\n");
    typedef HRESULT (STDAPICALLTYPE *get_fn)(REFCLSID,REFIID,void**);
    auto get=(get_fn)GetProcAddress(dll,"DllGetClassObject"); if(!get)return 6;
    IBaseFilter *camera=nullptr;
    if(registered) camera=registered_camera();
    else {
        IClassFactory *factory=nullptr; check(get(CLSID_OBS_VirtualVideo,IID_IClassFactory,(void**)&factory),"DllGetClassObject");
        check(factory->CreateInstance(nullptr,IID_IBaseFilter,(void**)&camera),"CreateInstance"); factory->Release();
    }
    printf("FILTER_CREATED=true\n");
    FILTER_INFO info={};check(camera->QueryFilterInfo(&info),"QueryFilterInfo"); printf("FILTER_INFO=true\n");
    IGraphBuilder *graph=nullptr; check(CoCreateInstance(CLSID_FilterGraph,nullptr,CLSCTX_INPROC_SERVER,IID_IGraphBuilder,(void**)&graph),"FilterGraph");
    printf("GRAPH_CREATED=true\n");
    IBaseFilter *grabFilter=nullptr,*nullFilter=nullptr;
    check(CoCreateInstance(CLSID_SampleGrabber,nullptr,CLSCTX_INPROC_SERVER,IID_IBaseFilter,(void**)&grabFilter),"SampleGrabber");
    check(CoCreateInstance(CLSID_RilmoTestNullRenderer,nullptr,CLSCTX_INPROC_SERVER,IID_IBaseFilter,(void**)&nullFilter),"NullRenderer");
    ISampleGrabber *grabber=nullptr; check(grabFilter->QueryInterface(IID_ISampleGrabber,(void**)&grabber),"ISampleGrabber");
    printf("SINK_CREATED=true\n");
    Samples samples; check(grabber->SetCallback(&samples,1),"SetCallback");
    check(graph->AddFilter(camera,L"Rilmo camera under test"),"Add camera");
    check(graph->AddFilter(grabFilter,L"Sample observer"),"Add grabber");
    check(graph->AddFilter(nullFilter,L"Null renderer"),"Add sink");
    printf("FILTERS_ADDED=true\n");
    IPin *cameraOut=pin(camera,PINDIR_OUTPUT), *grabIn=pin(grabFilter,PINDIR_INPUT),*grabOut=pin(grabFilter,PINDIR_OUTPUT),*sinkIn=pin(nullFilter,PINDIR_INPUT);
    check(graph->ConnectDirect(cameraOut,grabIn,nullptr),"Connect camera");check(graph->ConnectDirect(grabOut,sinkIn,nullptr),"Connect sink");
    printf("CONNECTED=true\n");
    AM_MEDIA_TYPE media={}; check(cameraOut->ConnectionMediaType(&media),"Media type");
    if(media.subtype!=MEDIASUBTYPE_NV12){fprintf(stderr,"Expected NV12\n");return 7;}
    VIDEOINFOHEADER *header=(VIDEOINFOHEADER*)media.pbFormat;
    printf("DEVICE=Rilmo Virtual Camera; FORMAT=NV12; WIDTH=%ld; HEIGHT=%ld; REGISTERED=%s\n",header->bmiHeader.biWidth,header->bmiHeader.biHeight,registered?"true":"false");
    CoTaskMemFree(media.pbFormat);if(media.pUnk)media.pUnk->Release();
    IMediaControl *control=nullptr;check(graph->QueryInterface(IID_IMediaControl,(void**)&control),"Control");check(control->Run(),"Run");
    for(int i=0;i<45;i++){if(writer)video_queue_write(writer,planes,strides,(uint64_t)i*33333333);Sleep(33);}
    if(writer){video_queue_close(writer);writer=nullptr;Sleep(500);}
    check(control->Stop(),"Stop");grabber->SetCallback(nullptr,1);
    printf("FRAMES=%ld; PADDED_NV12_FRAMES=%ld; STOPPED_SLATE_FRAMES=%ld\n",samples.frames,samples.pattern,samples.slate);
    bool passed=samples.frames>=15 && (!self||(samples.pattern>=15 && samples.slate>=3));
    cameraOut->Release();grabIn->Release();grabOut->Release();sinkIn->Release();
    control->Release();grabber->Release();graph->Release();camera->Release();grabFilter->Release();nullFilter->Release();
    typedef HRESULT (STDAPICALLTYPE *unload_fn)(); auto unload=(unload_fn)GetProcAddress(dll,"DllCanUnloadNow");
    bool released=unload&&unload()==S_OK; printf("FILTER_RELEASED=%s\n",released?"true":"false");
    FreeLibrary(dll);CoUninitialize();return passed&&released?0:8;
}
