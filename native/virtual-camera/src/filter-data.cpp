// SPDX-License-Identifier: GPL-2.0-or-later
// Build-time helper: serialize the DirectShow pin declaration without writing
// registry keys or loading/registering Rilmo. Windows supplies the format.
#include <windows.h>
#include <dshow.h>
#include <stdio.h>

static const GUID IID_RilmoFilterData={0x97f7c4d4,0x547b,0x4a5f,{0x83,0x32,0x53,0x64,0x30,0xad,0x2e,0x4d}};
struct RilmoFilterData : public IUnknown {
    virtual HRESULT STDMETHODCALLTYPE ParseFilterData(BYTE*,ULONG,BYTE**)=0;
    virtual HRESULT STDMETHODCALLTYPE CreateFilterData(REGFILTER2*,BYTE**,ULONG*)=0;
};
int main() {
    HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    if(FAILED(hr))return 1;
    RilmoFilterData *mapper=nullptr;
    hr=CoCreateInstance(CLSID_FilterMapper2,nullptr,CLSCTX_INPROC_SERVER,IID_RilmoFilterData,(void**)&mapper);
    if(FAILED(hr)){CoUninitialize();return 2;}
    REGPINTYPES type={&MEDIATYPE_Video,&MEDIASUBTYPE_NV12};
    REGFILTERPINS pin={nullptr,FALSE,TRUE,FALSE,FALSE,&CLSID_NULL,nullptr,1,&type};
    REGFILTER2 filter={};filter.dwVersion=1;filter.dwMerit=MERIT_DO_NOT_USE;filter.cPins=1;filter.rgPins=&pin;
    BYTE *data=nullptr;ULONG size=0;
    hr=mapper->CreateFilterData(&filter,&data,&size);
    if(SUCCEEDED(hr)&&data&&size) {
        for(ULONG i=0;i<size;i++)printf("%02X",data[i]);
        printf("\n");
    }
    CoTaskMemFree(data);mapper->Release();CoUninitialize();
    return SUCCEEDED(hr)&&size?0:3;
}
