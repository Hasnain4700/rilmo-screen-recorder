/* SPDX-License-Identifier: GPL-3.0-or-later
 * Read-only MSI launch check. Never closes an app or interrupts recording. */
#include <windows.h>
#include <msi.h>
#include <msiquery.h>
__declspec(dllexport) UINT __stdcall CheckRilmoIdle(MSIHANDLE session) {
    SetLastError(ERROR_SUCCESS);
    HANDLE mutex=OpenMutexW(SYNCHRONIZE,FALSE,L"Local\\RilmoRecorder.Desktop.v2");
    DWORD error=GetLastError();
    BOOL busy=mutex!=NULL || error==ERROR_ACCESS_DENIED;
    if(mutex)CloseHandle(mutex);
    return MsiSetPropertyW(session,L"RILMO_RUNNING",busy?L"1":L"");
}
