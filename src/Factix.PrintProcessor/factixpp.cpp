#define WINVER 0x0601
#define _WIN32_WINNT 0x0601

#include <windows.h>
#include <winspool.h>
#include <shlobj.h>
#include <sddl.h>
#include <objbase.h>
#include <stdio.h>
#include <string.h>

static const DWORD kSignature = 0x46545850;

typedef struct _PRINTPROCESSOROPENDATA
{
    void* pDevMode;
    LPWSTR pDatatype;
    LPWSTR pParameters;
    LPWSTR pDocumentName;
    DWORD JobId;
    LPWSTR pOutputFile;
    LPWSTR pPrinterName;
} PRINTPROCESSOROPENDATA;

typedef struct _PROCESSOR
{
    DWORD signature;
    WCHAR printerName[260];
    WCHAR documentName[512];
    WCHAR datatype[128];
    DWORD jobId;
    volatile LONG cancelled;
} PROCESSOR;

typedef HANDLE (WINAPI* PFN_GdiGetSpoolFileHandle)(LPWSTR);
typedef BOOL (WINAPI* PFN_GdiDeleteSpoolFileHandle)(HANDLE);

static void CopyText(WCHAR* dest, size_t cch, const WCHAR* src)
{
    size_t i = 0;
    if (!dest || cch == 0)
        return;
    if (!src)
    {
        dest[0] = 0;
        return;
    }
    for (; i + 1 < cch && src[i]; ++i)
        dest[i] = src[i];
    dest[i] = 0;
}

static void LogLine(const WCHAR* message)
{
    WCHAR programData[MAX_PATH];
    WCHAR dir[MAX_PATH];
    WCHAR path[MAX_PATH];
    HANDLE file;
    char utf8[1024];
    DWORD written = 0;
    int bytes;

    if (FAILED(SHGetFolderPathW(NULL, CSIDL_COMMON_APPDATA, NULL, SHGFP_TYPE_CURRENT, programData)))
        return;
    _snwprintf(dir, MAX_PATH, L"%s\\Factix\\logs", programData);
    dir[MAX_PATH - 1] = 0;
    CreateDirectoryW(programData, NULL);
    _snwprintf(path, MAX_PATH, L"%s\\Factix", programData);
    path[MAX_PATH - 1] = 0;
    CreateDirectoryW(path, NULL);
    CreateDirectoryW(dir, NULL);
    _snwprintf(path, MAX_PATH, L"%s\\processor.log", dir);
    path[MAX_PATH - 1] = 0;

    file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE)
        return;
    bytes = WideCharToMultiByte(CP_UTF8, 0, message, -1, utf8, (int)sizeof(utf8) - 2, NULL, NULL);
    if (bytes > 1)
    {
        utf8[bytes - 1] = '\n';
        utf8[bytes] = 0;
        WriteFile(file, utf8, (DWORD)bytes, &written, NULL);
    }
    CloseHandle(file);
}

static void CreateJobsRoot(WCHAR* jobs, size_t cch)
{
    WCHAR programData[MAX_PATH];
    WCHAR root[MAX_PATH];
    PSECURITY_DESCRIPTOR sd = NULL;
    SECURITY_ATTRIBUTES sa;

    if (FAILED(SHGetFolderPathW(NULL, CSIDL_COMMON_APPDATA, NULL, SHGFP_TYPE_CURRENT, programData)))
        CopyText(programData, MAX_PATH, L"C:\\ProgramData");

    _snwprintf(root, MAX_PATH, L"%s\\Factix", programData);
    root[MAX_PATH - 1] = 0;
    CreateDirectoryW(root, NULL);
    _snwprintf(jobs, cch, L"%s\\jobs", root);
    jobs[cch - 1] = 0;
    if (GetFileAttributesW(jobs) != INVALID_FILE_ATTRIBUTES)
        return;

    if (ConvertStringSecurityDescriptorToSecurityDescriptorW(
            L"D:(A;OICI;GA;;;SY)(A;OICI;GA;;;BA)(A;OICI;M;;;BU)",
            SDDL_REVISION_1,
            &sd,
            NULL))
    {
        sa.nLength = sizeof(sa);
        sa.lpSecurityDescriptor = sd;
        sa.bInheritHandle = FALSE;
        CreateDirectoryW(jobs, &sa);
        LocalFree(sd);
    }
    else
    {
        CreateDirectoryW(jobs, NULL);
    }
}

static BOOL GetSpoolDirectory(const WCHAR* printer, WCHAR* dest, size_t cch)
{
    WCHAR keyPath[512];
    WCHAR windows[MAX_PATH];
    HKEY key = NULL;
    DWORD cb = (DWORD)(cch * sizeof(WCHAR));
    DWORD type = 0;

    _snwprintf(keyPath, 512, L"SYSTEM\\CurrentControlSet\\Control\\Print\\Printers\\%s", printer ? printer : L"");
    keyPath[511] = 0;
    dest[0] = 0;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, keyPath, 0, KEY_READ | KEY_WOW64_64KEY, &key) == ERROR_SUCCESS)
    {
        if (RegQueryValueExW(key, L"SpoolDirectory", NULL, &type, (LPBYTE)dest, &cb) == ERROR_SUCCESS && dest[0])
        {
            RegCloseKey(key);
            return TRUE;
        }
        RegCloseKey(key);
        dest[0] = 0;
    }

    if (!GetWindowsDirectoryW(windows, MAX_PATH))
        return FALSE;
    _snwprintf(dest, cch, L"%s\\System32\\spool\\PRINTERS", windows);
    dest[cch - 1] = 0;
    return TRUE;
}

static BOOL FileExists(const WCHAR* path)
{
    return GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES;
}

static BOOL FindNamedSpool(const WCHAR* dir, DWORD jobId, WCHAR* dest, size_t cch)
{
    const WCHAR* formats[] = { L"%s\\%05u.SPL", L"%s\\%u.SPL", L"%s\\FP%05u.SPL" };
    int i;
    for (i = 0; i < 3; ++i)
    {
        _snwprintf(dest, cch, formats[i], dir, jobId);
        dest[cch - 1] = 0;
        if (FileExists(dest))
            return TRUE;
    }
    return FALSE;
}

static BOOL FindNewestSpool(const WCHAR* dir, WCHAR* dest, size_t cch)
{
    WCHAR pattern[MAX_PATH];
    WIN32_FIND_DATAW data;
    HANDLE find;
    FILETIME best;
    BOOL found = FALSE;
    ULARGE_INTEGER now;
    FILETIME nowFt;
    ULONGLONG cutoff;

    _snwprintf(pattern, MAX_PATH, L"%s\\*.SPL", dir);
    pattern[MAX_PATH - 1] = 0;
    GetSystemTimeAsFileTime(&nowFt);
    now.LowPart = nowFt.dwLowDateTime;
    now.HighPart = nowFt.dwHighDateTime;
    cutoff = now.QuadPart - (15ULL * 10000000ULL);
    best.dwLowDateTime = 0;
    best.dwHighDateTime = 0;
    find = FindFirstFileW(pattern, &data);
    if (find == INVALID_HANDLE_VALUE)
        return FALSE;

    do
    {
        ULARGE_INTEGER stamp;
        stamp.LowPart = data.ftLastWriteTime.dwLowDateTime;
        stamp.HighPart = data.ftLastWriteTime.dwHighDateTime;
        if (stamp.QuadPart < cutoff)
            continue;
        if (!found || CompareFileTime(&data.ftLastWriteTime, &best) > 0)
        {
            best = data.ftLastWriteTime;
            _snwprintf(dest, cch, L"%s\\%s", dir, data.cFileName);
            dest[cch - 1] = 0;
            found = TRUE;
        }
    } while (FindNextFileW(find, &data));

    FindClose(find);
    return found;
}

static BOOL CopyShared(const WCHAR* from, const WCHAR* to)
{
    HANDLE input;
    HANDLE output;
    BYTE buffer[65536];
    DWORD read = 0;
    DWORD written = 0;
    BOOL ok = TRUE;

    input = CreateFileW(from, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (input == INVALID_HANDLE_VALUE)
        return FALSE;
    output = CreateFileW(to, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (output == INVALID_HANDLE_VALUE)
    {
        CloseHandle(input);
        return FALSE;
    }

    while (ReadFile(input, buffer, sizeof(buffer), &read, NULL) && read > 0)
    {
        if (!WriteFile(output, buffer, read, &written, NULL) || written != read)
        {
            ok = FALSE;
            break;
        }
    }

    CloseHandle(input);
    CloseHandle(output);
    return ok;
}

static BOOL ReadCurrentJob(const WCHAR* printer, const WCHAR* dest)
{
    HANDLE printerHandle = NULL;
    HANDLE output;
    WCHAR name[260];
    BYTE buffer[65536];
    DWORD read = 0;
    DWORD written = 0;
    BOOL any = FALSE;

    CopyText(name, 260, printer);
    if (!OpenPrinterW(name, &printerHandle, NULL))
        return FALSE;
    output = CreateFileW(dest, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (output == INVALID_HANDLE_VALUE)
    {
        ClosePrinter(printerHandle);
        return FALSE;
    }

    while (ReadPrinter(printerHandle, buffer, sizeof(buffer), &read) && read > 0)
    {
        any = TRUE;
        if (!WriteFile(output, buffer, read, &written, NULL) || written != read)
        {
            any = FALSE;
            break;
        }
    }

    CloseHandle(output);
    ClosePrinter(printerHandle);
    return any;
}

static void JsonAppend(char* dest, size_t cap, size_t* used, const char* text)
{
    size_t length = strlen(text);
    if (*used + length >= cap)
        return;
    memcpy(dest + *used, text, length);
    *used += length;
    dest[*used] = 0;
}

static void AppendEscaped(char* dest, size_t cap, size_t* used, const WCHAR* value)
{
    int bytes;
    char* utf8;
    char* cursor;
    if (!value)
        value = L"";
    bytes = WideCharToMultiByte(CP_UTF8, 0, value, -1, NULL, 0, NULL, NULL);
    if (bytes <= 0)
    {
        JsonAppend(dest, cap, used, "\"\"");
        return;
    }
    utf8 = (char*)HeapAlloc(GetProcessHeap(), 0, (SIZE_T)bytes);
    if (!utf8)
    {
        JsonAppend(dest, cap, used, "\"\"");
        return;
    }
    WideCharToMultiByte(CP_UTF8, 0, value, -1, utf8, bytes, NULL, NULL);
    JsonAppend(dest, cap, used, "\"");
    for (cursor = utf8; *cursor; ++cursor)
    {
        unsigned char ch = (unsigned char)*cursor;
        char tmp[8];
        if (ch == '\\' || ch == '"')
        {
            tmp[0] = '\\';
            tmp[1] = (char)ch;
            tmp[2] = 0;
            JsonAppend(dest, cap, used, tmp);
        }
        else if (ch == '\n')
            JsonAppend(dest, cap, used, "\\n");
        else if (ch == '\r')
            JsonAppend(dest, cap, used, "\\r");
        else if (ch == '\t')
            JsonAppend(dest, cap, used, "\\t");
        else if (ch < 0x20)
        {
            sprintf(tmp, "\\u%04X", ch);
            JsonAppend(dest, cap, used, tmp);
        }
        else
        {
            tmp[0] = (char)ch;
            tmp[1] = 0;
            JsonAppend(dest, cap, used, tmp);
        }
    }
    JsonAppend(dest, cap, used, "\"");
    HeapFree(GetProcessHeap(), 0, utf8);
}

static BOOL WriteJobJson(const WCHAR* folder, const WCHAR* id, const PROCESSOR* processor)
{
    char json[4096];
    size_t used = 0;
    char created[40];
    SYSTEMTIME st;
    WCHAR tempPath[MAX_PATH];
    WCHAR finalPath[MAX_PATH];
    HANDLE file;
    DWORD written = 0;
    BOOL ok;

    GetSystemTime(&st);
    sprintf(created, "%04u-%02u-%02uT%02u:%02u:%02uZ", st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
    json[0] = 0;
    JsonAppend(json, sizeof(json), &used, "{\"id\":");
    AppendEscaped(json, sizeof(json), &used, id);
    JsonAppend(json, sizeof(json), &used, ",\"documentName\":");
    AppendEscaped(json, sizeof(json), &used, processor->documentName);
    JsonAppend(json, sizeof(json), &used, ",\"datatype\":");
    AppendEscaped(json, sizeof(json), &used, processor->datatype);
    JsonAppend(json, sizeof(json), &used, ",\"file\":\"spool.spl\",\"createdUtc\":\"");
    JsonAppend(json, sizeof(json), &used, created);
    JsonAppend(json, sizeof(json), &used, "\"}");
    if (used == 0 || used + 1 >= sizeof(json) || json[used - 1] != '}')
        return FALSE;

    _snwprintf(tempPath, MAX_PATH, L"%s\\job.json.tmp", folder);
    tempPath[MAX_PATH - 1] = 0;
    _snwprintf(finalPath, MAX_PATH, L"%s\\job.json", folder);
    finalPath[MAX_PATH - 1] = 0;
    file = CreateFileW(tempPath, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE)
        return FALSE;
    ok = WriteFile(file, json, (DWORD)used, &written, NULL) && written == used;
    CloseHandle(file);
    if (!ok)
        return FALSE;
    return MoveFileExW(tempPath, finalPath, MOVEFILE_REPLACE_EXISTING);
}

static void DeleteTree(const WCHAR* folder)
{
    WCHAR path[MAX_PATH];
    _snwprintf(path, MAX_PATH, L"%s\\spool.spl", folder);
    path[MAX_PATH - 1] = 0;
    DeleteFileW(path);
    _snwprintf(path, MAX_PATH, L"%s\\job.json.tmp", folder);
    path[MAX_PATH - 1] = 0;
    DeleteFileW(path);
    _snwprintf(path, MAX_PATH, L"%s\\job.json", folder);
    path[MAX_PATH - 1] = 0;
    DeleteFileW(path);
    RemoveDirectoryW(folder);
}

static HANDLE AcquireSpoolHandle(WCHAR* printerName)
{
    HMODULE gdi = GetModuleHandleW(L"gdi32.dll");
    PFN_GdiGetSpoolFileHandle getHandle;
    if (!gdi)
        gdi = LoadLibraryW(L"gdi32.dll");
    if (!gdi)
        return NULL;
    getHandle = (PFN_GdiGetSpoolFileHandle)GetProcAddress(gdi, "GdiGetSpoolFileHandle");
    if (!getHandle)
        return NULL;
    return getHandle(printerName);
}

static void ReleaseSpoolHandle(HANDLE spool)
{
    HMODULE gdi = GetModuleHandleW(L"gdi32.dll");
    PFN_GdiDeleteSpoolFileHandle deleteHandle;
    if (!spool || spool == INVALID_HANDLE_VALUE || !gdi)
        return;
    deleteHandle = (PFN_GdiDeleteSpoolFileHandle)GetProcAddress(gdi, "GdiDeleteSpoolFileHandle");
    if (deleteHandle)
        deleteHandle(spool);
}

static BOOL CaptureJob(PROCESSOR* processor)
{
    WCHAR jobs[MAX_PATH];
    WCHAR folder[MAX_PATH];
    WCHAR spoolDir[MAX_PATH];
    WCHAR source[MAX_PATH];
    WCHAR target[MAX_PATH];
    WCHAR guidText[64];
    GUID guid;
    int attempt;
    LARGE_INTEGER size;
    HANDLE captured;
    HANDLE heldSpool;
    BOOL copied = FALSE;

    if (CoCreateGuid(&guid) != S_OK)
    {
        _snwprintf(guidText, 64, L"%08lX-%lu", GetTickCount(), processor->jobId);
        guidText[63] = 0;
    }
    else
    {
        _snwprintf(
            guidText,
            64,
            L"%08lX-%04X-%04X-%02X%02X-%02X%02X%02X%02X%02X%02X",
            guid.Data1,
            guid.Data2,
            guid.Data3,
            guid.Data4[0],
            guid.Data4[1],
            guid.Data4[2],
            guid.Data4[3],
            guid.Data4[4],
            guid.Data4[5],
            guid.Data4[6],
            guid.Data4[7]);
        guidText[63] = 0;
    }

    CreateJobsRoot(jobs, MAX_PATH);
    _snwprintf(folder, MAX_PATH, L"%s\\%s", jobs, guidText);
    folder[MAX_PATH - 1] = 0;
    if (!CreateDirectoryW(folder, NULL) && GetLastError() != ERROR_ALREADY_EXISTS)
    {
        LogLine(L"No se pudo crear la carpeta del trabajo.");
        return FALSE;
    }

    _snwprintf(target, MAX_PATH, L"%s\\spool.spl", folder);
    target[MAX_PATH - 1] = 0;
    heldSpool = AcquireSpoolHandle(processor->printerName);
    if (!GetSpoolDirectory(processor->printerName, spoolDir, MAX_PATH))
        CopyText(spoolDir, MAX_PATH, L"C:\\Windows\\System32\\spool\\PRINTERS");

    for (attempt = 0; attempt < 20 && !copied && !processor->cancelled; ++attempt)
    {
        if (FindNamedSpool(spoolDir, processor->jobId, source, MAX_PATH))
            copied = CopyShared(source, target);
        else if (attempt >= 15 && FindNewestSpool(spoolDir, source, MAX_PATH))
            copied = CopyShared(source, target);
        if (!copied)
            Sleep(100);
    }

    if (!copied && !processor->cancelled)
        copied = ReadCurrentJob(processor->printerName, target);

    captured = CreateFileW(target, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    size.QuadPart = 0;
    if (captured != INVALID_HANDLE_VALUE)
    {
        GetFileSizeEx(captured, &size);
        CloseHandle(captured);
    }

    if (processor->cancelled || !copied || size.QuadPart <= 0)
    {
        LogLine(L"No se pudo copiar el trabajo de impresión.");
        DeleteTree(folder);
        ReleaseSpoolHandle(heldSpool);
        return FALSE;
    }

    if (!WriteJobJson(folder, guidText, processor))
    {
        LogLine(L"No se pudo escribir job.json.");
        DeleteTree(folder);
        ReleaseSpoolHandle(heldSpool);
        return FALSE;
    }

    ReleaseSpoolHandle(heldSpool);
    LogLine(L"Trabajo capturado.");
    return TRUE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    UNREFERENCED_PARAMETER(instance);
    UNREFERENCED_PARAMETER(reason);
    UNREFERENCED_PARAMETER(reserved);
    return TRUE;
}

BOOL WINAPI EnumPrintProcessorDatatypesW(
    LPWSTR pName,
    LPWSTR pPrintProcessorName,
    DWORD Level,
    LPBYTE pDatatypes,
    DWORD cbBuf,
    LPDWORD pcbNeeded,
    LPDWORD pcReturned)
{
    const WCHAR* types[] = { L"NT EMF 1.008", L"RAW" };
    DWORD count = 2;
    DWORD needed = 0;
    DWORD i;
    WCHAR* strings;
    UNREFERENCED_PARAMETER(pName);
    UNREFERENCED_PARAMETER(pPrintProcessorName);

    if (Level != 1 || !pcbNeeded || !pcReturned)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    for (i = 0; i < count; ++i)
        needed += (DWORD)sizeof(LPWSTR) + (DWORD)((lstrlenW(types[i]) + 1) * sizeof(WCHAR));
    *pcbNeeded = needed;
    *pcReturned = 0;
    if (!pDatatypes || cbBuf < needed)
    {
        SetLastError(ERROR_INSUFFICIENT_BUFFER);
        return FALSE;
    }

    strings = (WCHAR*)(pDatatypes + (count * sizeof(LPWSTR)));
    for (i = 0; i < count; ++i)
    {
        ((LPWSTR*)pDatatypes)[i] = strings;
        lstrcpyW(strings, types[i]);
        strings += lstrlenW(types[i]) + 1;
    }
    *pcReturned = count;
    return TRUE;
}

HANDLE WINAPI OpenPrintProcessor(LPWSTR pPrinterName, PRINTPROCESSOROPENDATA* pPrintProcessorOpenData)
{
    PROCESSOR* processor;
    if (!pPrintProcessorOpenData)
    {
        SetLastError(ERROR_INVALID_PARAMETER);
        return NULL;
    }

    processor = (PROCESSOR*)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(PROCESSOR));
    if (!processor)
    {
        SetLastError(ERROR_NOT_ENOUGH_MEMORY);
        return NULL;
    }

    processor->signature = kSignature;
    processor->jobId = pPrintProcessorOpenData->JobId;
    CopyText(processor->printerName, 260, pPrinterName ? pPrinterName : pPrintProcessorOpenData->pPrinterName);
    CopyText(processor->documentName, 512, pPrintProcessorOpenData->pDocumentName);
    CopyText(processor->datatype, 128, pPrintProcessorOpenData->pDatatype);
    return processor;
}

BOOL WINAPI PrintDocumentOnPrintProcessor(HANDLE hPrintProcessor, LPWSTR pDocumentName)
{
    PROCESSOR* processor = (PROCESSOR*)hPrintProcessor;
    UNREFERENCED_PARAMETER(pDocumentName);
    if (!processor || processor->signature != kSignature)
    {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    return CaptureJob(processor);
}

BOOL WINAPI ClosePrintProcessor(HANDLE hPrintProcessor)
{
    PROCESSOR* processor = (PROCESSOR*)hPrintProcessor;
    if (!processor || processor->signature != kSignature)
    {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    processor->signature = 0;
    HeapFree(GetProcessHeap(), 0, processor);
    return TRUE;
}

BOOL WINAPI ControlPrintProcessor(HANDLE hPrintProcessor, DWORD Command)
{
    PROCESSOR* processor = (PROCESSOR*)hPrintProcessor;
    if (!processor || processor->signature != kSignature)
    {
        SetLastError(ERROR_INVALID_HANDLE);
        return FALSE;
    }
    if (Command == 3 || Command == 5)
        InterlockedExchange(&processor->cancelled, 1);
    return TRUE;
}

DWORD WINAPI GetPrintProcessorCapabilities(
    LPWSTR pValueName,
    DWORD dwAttributes,
    LPBYTE pData,
    DWORD nSize,
    LPDWORD pcbNeeded)
{
    UNREFERENCED_PARAMETER(pValueName);
    UNREFERENCED_PARAMETER(dwAttributes);
    UNREFERENCED_PARAMETER(pData);
    UNREFERENCED_PARAMETER(nSize);
    if (pcbNeeded)
        *pcbNeeded = 0;
    return ERROR_SUCCESS;
}
