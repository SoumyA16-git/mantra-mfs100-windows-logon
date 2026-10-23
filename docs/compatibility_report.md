# Mantra MFS100 Windows 11 Compatibility & Architecture Report

Date: 2026-10-23  
Target Platform: Windows 11 Pro x64 (Build 10.0.26100+)  
Device: Mantra MFS100 Optical Fingerprint Scanner (USB\VID_2C0F&PID_1005)  
Serial: 49007331  
SDK Version: 9025  

---

## 1. Executive Summary

The Mantra MFS100 optical fingerprint scanner can be successfully operated on Windows 11 x64 to perform biometric capture and ISO 19794-2 template matching. However, because the Mantra native C/C++ driver DLLs (`MFS100Dll.dll` and `iengine_ansi_iso.dll`) and managed wrapper (`MANTRA.MFS100.dll`) are strictly **32-bit (x86)** binaries, they **cannot be loaded directly into the 64-bit Windows LogonUI.exe process**.

A robust out-of-process architecture has been designed and implemented:
1. **64-bit Credential Provider COM DLL** (`MFS100CredentialProvider.dll`): Runs in-process inside `LogonUI.exe` on the Winlogon desktop (Session 0).
2. **32-bit Bridge Service** (`MFS100BridgeSvc.exe`): Runs as a Windows Service in Session 0 (`LocalSystem`), hosting a secure Named Pipe (`\\.\pipe\MFS100Bridge`).
3. **32-bit Worker Process** (`MFS100Worker.exe`): Dedicated subprocess isolated from the bridge, hosting the 32-bit Mantra SDK. Communicates via clean line-delimited JSON on redirected streams. Enforces real process-level timeouts and recovery.

---

## 2. Hardware & Driver Findings

| Attribute | Verified Value | Evidence / Command |
|---|---|---|
| Device Status | OK | Device Manager / PnP query |
| Hardware ID | `USB\VID_2C0F&PID_1005\8E46E90C49AC` | `Get-PnpDevice -FriendlyName "*MFS100*"` |
| Kernel Driver | `MFS100.sys` (KMDF Driver) | Installed under `System32\drivers\` |
| Resolution | 316 x 354 pixels | `MFS100.GetDeviceInfo()` |
| Sensor Make / Model | `MANTRA` `MFS100` | `MFS100.GetDeviceInfo()` |

---

## 3. Binary Architecture & Portability

| DLL / Component | Machine Architecture | PE Header | Can Load in 64-bit Process? |
|---|---|---|---|
| `MFS100Dll.dll` | x86 (32-bit) | `0x014C` (IMAGE_FILE_MACHINE_I386) | **NO** |
| `iengine_ansi_iso.dll` | x86 (32-bit) | `0x014C` (IMAGE_FILE_MACHINE_I386) | **NO** |
| `MANTRA.MFS100.dll` | x86 (32-bit) | `0x014C` (IMAGE_FILE_MACHINE_I386) | **NO** |
| Windows `LogonUI.exe` | x64 (64-bit) | `0x8664` (IMAGE_FILE_MACHINE_AMD64) | Must load x64 DLLs only |
| Credential Provider DLL | x64 (64-bit) | `0x8664` (IMAGE_FILE_MACHINE_AMD64) | Required by LogonUI |

Full disk scan of the host filesystem revealed no 64-bit build of `MFS100Dll.dll`. Therefore, inter-process communication (IPC) via Windows Named Pipes between the 64-bit Credential Provider and a 32-bit service is the **only architecturally viable approach**.

---

## 4. Native SDK Runtime Characteristics

### 4.1 AutoCapture Modes
Experimental verification of `AutoCapture` on the physical device revealed:
- `IsDetectFinger = true`: Does **not** honor the timeout. If no finger is on the platen, it immediately exits after ~650ms returning error code `-1324` ("Finger not placed properly").
- `IsDetectFinger = false`: Successfully polls the scanner hardware for the full specified timeout (e.g. 5000ms or 10000ms). When a finger is placed, it captures immediately and returns code `0` with ISO template bytes and Quality/NFIQ scores. If no finger is placed within the timeout window, it returns code `-1140` (Timeout).
- `ShowPreview = false`: Bypasses window handle (HWND) rendering and message pump dispatching, enabling headless execution.

### 4.2 Native Standard Output Chatter
During `AutoCapture`, the native DLL writes raw strings directly to standard output:
`MFS100AutoCapture Quality = N` (repeated hundreds of times per second).
- **Impact**: Without suppression, this chatter corrupts stdin/stdout JSON IPC streams.
- **Solution**: `MFS100Worker.exe` redirects managed `Console.Out` to a filter while retaining the raw Win32 `STD_OUTPUT_HANDLE` exclusively for structured JSON responses.

### 4.3 Native SDK Unresponsiveness & Isolation
A native blocking call in C/C++ cannot be interrupted by .NET `CancellationToken.Cancel()`. If an optical scanner USB connection drops or hardware deadlocks mid-capture:
- The worker subprocess is monitored by `WorkerHost` in `MFS100BridgeSvc`.
- `WorkerHost` enforces a hard deadline (`timeoutMs + 2500ms`).
- If the worker does not return within the deadline, `WorkerHost` forcefully terminates the worker with `Process.Kill()` and auto-spawns a fresh worker on the next request.

---

## 5. Security Model & Threat Assessment

### 5.1 Credential Caching Model (Option A)
To log into Windows via Kerberos interactive authentication without Windows Hello TPM biometric enrollment:
- The user's Windows password is encrypted using AES-256-GCM.
- Per-user encrypted files are stored at `%ProgramData%\MFS100CP\<UserSID>.dat`.
- Decryption only occurs in Session 0 inside the SYSTEM-privileged Credential Provider when a matching fingerprint score >= threshold is achieved.
- At logon, credentials are packed into `KERB_INTERACTIVE_LOGON` / `KERB_INTERACTIVE_UNLOCK_LOGON` and submitted to the Local Security Authority (LSA).

### 5.2 Threat Model & Inherent Limitations
1. **SYSTEM Privilege Compromise**: Any malicious process running as `LocalSystem` or offline disk access can read stored keys. This threat level is identical to Windows Credential Manager and third-party commercial biometric providers.
2. **Password Changes**: If the user changes their Windows password, the cached password becomes stale. The Credential Provider handles `STATUS_WRONG_PASSWORD` gracefully by prompting the user to log in with their password and re-enroll.
3. **Preservation of Native Authentication**: The standard Windows password and PIN credential providers are **never** hidden, filtered, or disabled. The user always retains full access to their standard Windows login options.
