# Mantra MFS100 Windows 11 Biometric System

A secure, high-accuracy Windows 11 biometric authentication and enrollment solution for the **Mantra MFS100 Optical USB Fingerprint Scanner**, built directly upon the official Mantra native SDK without relying on Aadhaar RD Service.

> **Status**: **Phase 2 Verified** — Preliminary Matcher Discrimination Passed (Margin: 5,511 pts), Native 32-bit Service Isolation Verified, Standalone Enrollment & Verification Suite Complete.  
> **Host Safety Guarantee**: **Zero Credential Provider DLLs are installed on the host machine.** Standard Windows Password and PIN sign-in remain 100% untouched and always available.

---

## Important Security Disclosures & Architectural Boundaries

> [!CAUTION]
> ### 1. Zero Persistent Passwords on Disk
> Permanent storage of Windows passwords, PINs, or password hashes on disk (even if encrypted with AES or protected by ACLs) is **permanently rejected and prohibited**. No credential files exist on disk.

> [!WARNING]
> ### 2. Windows 11 Authentication Reality & Limitations
> - **Cold Boot 1-Touch Login is Impossible Without Passwords or Domain Smart Cards**:  
>   Windows interactive authentication (`LsaLogonUser`) strictly requires either the user's plaintext password (`MSV1_0_INTERACTIVE_LOGON`) to decrypt DPAPI master keys and load `NTUSER.DAT`, or an Active Directory domain Smart Card PKINIT certificate. Standard local Windows accounts do not support smart cards. Because passwords are not stored on disk, **a 1-touch cold boot biometric login cannot be achieved on standalone Windows 11**.
> - **Native Windows Hello is Unsupported by the Hardware**:  
>   Windows 11 Windows Biometric Framework (WBF / Windows Hello) requires signed 64-bit WBDI drivers and engine adapters. The Mantra MFS100 provides only a proprietary **32-bit (x86) legacy user-mode SDK**. Consequently, native Windows Hello cannot enumerate or manage this device.
> - **Volatile RAM Session Caching Limitations**:  
>   Holding a password in volatile RAM for Win+L workstation unlock (`CryptProtectMemory`) protects against non-admin memory dumps, but **provides zero protection against local Administrators, `NT AUTHORITY\SYSTEM` processes, or kernel drivers**. Furthermore, it is wiped on reboot and does not work at cold boot.

> [!IMPORTANT]
> ### 3. Host Protection Policy
> All Credential Provider development is strictly restricted to an isolated **Windows 11 Virtual Machine (VM) with USB passthrough**. No Credential Provider code will ever be registered on the developer's physical host operating system.

---

## Table of Contents

- [Part 1: End-User Guide](#part-1-end-user-guide)
  - [Hardware & Driver Requirements](#hardware--driver-requirements)
  - [Quick Start](#quick-start)
  - [Enrolling Your Fingerprints](#enrolling-your-fingerprints)
  - [Verifying Enrolled Fingerprints (1:1)](#verifying-enrolled-fingerprints-11)
  - [Multi-User Identification (1:N)](#multi-user-identification-1n)
  - [Deleting Fingerprints](#deleting-fingerprints)
  - [Troubleshooting & Recovery](#troubleshooting--recovery)
- [Part 2: Developer & Architecture Manual](#part-2-developer--architecture-manual)
  - [System Architecture](#system-architecture)
  - [Why 32-bit Process Isolation is Mandatory](#why-32-bit-process-isolation-is-mandatory)
  - [Gate 1 Biometric Verification Results](#gate-1-biometric-verification-results)
  - [Template Storage Specification (`.dat`)](#template-storage-specification-dat)
  - [Named Pipe IPC Protocol (`\\.\pipe\MFS100Bridge`)](#named-pipe-ipc-protocol-pipemfs100bridge)
  - [Building from Source](#building-from-source)
  - [Automated Verification & Boundary Tests](#automated-verification--boundary-tests)
  - [Native Error Codes Reference](#native-error-codes-reference)

---

# Part 1: End-User Guide

### Hardware & Driver Requirements

- **Device**: Mantra MFS100 Optical Fingerprint Scanner (USB)
- **Hardware IDs**: `USB\VID_2C0F&PID_1005`
- **Operating System**: Windows 11 (64-bit) or Windows 10 (64-bit)
- **Official Driver**: Mantra MFS100 Driver v9.0.2.5 (Installed in `C:\Program Files\Mantra\MFS100\Driver`)
- **No Aadhaar RD Service Needed**: This application interacts directly with the driver and SDK. You do not need and should not install Aadhaar RD Service.
- **Runtime**: Microsoft .NET Framework 4.8 (built into Windows 11)

---

### Quick Start

1. Plug your **Mantra MFS100 scanner** into a USB 2.0 or 3.0 port.
2. Ensure the official driver is installed (the red optical sensor LED blinks upon plugging in).
3. Open the project folder and double-click:
   ```cmd
   run_enrollment.bat
   ```
4. An interactive console menu will appear:
   ```text
   ==================================================
      MFS100 Standalone Enrollment & Verification    
      (Zero Credentials Stored | Per-SID Isolation)  
   ==================================================
     1. List Windows Accounts & Enrollment Status
     2. Enroll a Finger for an Account
     3. Perform 1:1 Fingerprint Verification
     4. Perform 1:N Multi-User Identification
     5. Delete Enrolled Fingerprint
     6. Run Automated Template Boundary Tests (AC 2.3 & 2.7)
     7. Exit
   ```

---

### Enrolling Your Fingerprints

To ensure high accuracy and prevent false rejections, the enrollment workflow enforces **Multi-Scan Consistency**:

1. Select option **`2`** from the menu (or run `run_enrollment.bat --enroll <UsernameOrSID>`).
2. Choose your Windows user account from the list.
3. Select which finger index to register:
   - **`0`**: Primary Finger (e.g., Right Index)
   - **`1`**: Secondary Finger (e.g., Left Thumb)
4. Enter a descriptive label (e.g., `Right Index`).
5. **The 3-Scan Process**:
   - The tool will ask you to perform **3 consecutive scans** of the *exact same finger*.
   - **Crucial**: Clean the sensor glass with a soft cloth and **lift your finger completely** between each scan.
   - Each scan must meet a minimum quality score of **`>= 60`** (recommended: 70–85).
   - The application automatically cross-matches all 3 scans (`Scan 1 vs 2`, `Scan 2 vs 3`, `Scan 1 vs 3`). All pairs must match with a score **`>= 1,400`**.
   - The scan with the highest biometric quality is saved to your user template.

> [!NOTE]
> Each Windows user SID is strictly limited to a **maximum of 2 enrolled fingers**. If you attempt to enroll a 3rd finger, the tool will instruct you to replace an existing one.

---

### Verifying Enrolled Fingerprints (1:1)

Option **`3`** (or `--verify <username>`) tests 1:1 verification:
1. Select your username.
2. When prompted, place your enrolled finger firmly and flatly on the scanner platen.
3. The scanner captures a fresh live probe and matches it against your enrolled templates:
   - **Match Success**: Match score is **`>= 1,400`** (typically 6,000–10,000). The screen displays `[MATCH SUCCESS]` and identifies which finger matched.
   - **Access Denied**: If an unenrolled finger or someone else's finger is placed, the score drops below 1,400 (typically 0–24) and the screen displays `[ACCESS DENIED]`.

---

### Multi-User Identification (1:N)

Option **`4`** (or `--identify`) tests 1:N cross-account identification:
1. Place any enrolled finger on the scanner.
2. The engine scans through all enrolled Windows SIDs on the system.
3. It uniquely identifies which Windows user account you belong to and reports the winning match score.

---

### Deleting Fingerprints

Option **`5`** (or `--delete <username> [fingerIndex]`):
- To remove a specific finger (e.g., Finger 1) while keeping Finger 0, specify `0` or `1`.
- To completely delete all enrolled biometric data for an account, choose `A` (All).

---

### Troubleshooting & Recovery

- **Red Light Doesn't Turn On**:
  - Unplug the scanner, wait 3 seconds, and plug it back in.
  - Check Windows Device Manager under `Universal Serial Bus controllers` for `Mantra MFS100`.
- **Quality Too Low (`< 60`)**:
  - Clean the glass platen of any grease or dust.
  - Press your finger flatly in the center of the optical window with moderate pressure (do not press too lightly).
- **Inconsistent Scans Warning During Enrollment**:
  - If you switch fingers during the 3 enrollment scans, the consistency check will reject the enrollment to protect you from saving a corrupted template. Always use the same finger for all 3 scans.
- **Windows Sign-in Recovery**:
  - The standard Windows Password and PIN options are **always** active on your system. If the biometric scanner is disconnected, you can simply type your normal password or PIN to log in.

---

# Part 2: Developer & Architecture Manual

### System Architecture

Windows 11 `LogonUI.exe` is a 64-bit process running in Session 0/1. The Mantra MFS100 SDK is strictly a 32-bit (x86) native library. Direct in-process loading of 32-bit DLLs into 64-bit `LogonUI` or 64-bit applications is forbidden by the Windows operating system.

To solve this, the architecture isolates the native SDK in a dedicated 32-bit worker process connected via local Inter-Process Communication (IPC):

```mermaid
graph TD
    subgraph Client / User Session
        CLI[MFS100Enroll.exe / CLI Tool]
        VMCP[MFS100CP.dll - VM LogonUI]
    end

    subgraph Service Layer - Session 0
        PIPE[\\.\pipe\MFS100Bridge - Named Pipe Server]
        SVC[MFS100BridgeSvc.exe - Windows Service]
    end

    subgraph Native Isolation Layer - 32-bit x86
        HOST[WorkerHost Process Manager]
        WORKER[MFS100Worker.exe - 32-bit x86]
        SDK[MANTRA.MFS100.dll + MFS100Dll.dll]
        DEV[(Mantra MFS100 USB Hardware)]
    end

    subgraph Isolated Storage
        STORE[%ProgramData%\MFS100CP\templates\<UserSID>.dat]
    end

    CLI -->|Named Pipe / Local API| PIPE
    VMCP -->|Named Pipe| PIPE
    PIPE --> SVC
    SVC --> HOST
    HOST -->|stdin / stdout JSON IPC| WORKER
    WORKER --> SDK
    SDK --> DEV
    CLI -->|Read/Write Templates| STORE
```

---

### Why 32-bit Process Isolation is Mandatory

1. **Binary Bit-Width Incompatibility**:
   The Mantra driver package includes:
   - `MFS100Dll.dll` (x86 32-bit PE)
   - `iengine_ansi_iso.dll` (x86 32-bit Innovatrics minutiae extraction & matching engine)
   - `MANTRA.MFS100.dll` (x86 32-bit .NET interop assembly)
   A 64-bit process cannot load 32-bit DLLs. Attempting to do so results in `BadImageFormatException` (`0x8007000B`).
2. **Crash Resilience**:
   Native biometric libraries that interface directly with USB endpoints can occasionally crash or deadlock if a device is abruptly unplugged during capture. By hosting the SDK inside `MFS100Worker.exe`, crashes are contained. `MFS100BridgeSvc` detects worker termination and automatically restarts it within milliseconds without interrupting the operating system.

---

### Gate 1 Biometric Verification Results

Before implementing enrollment or credential logic, Gate 1 evaluated the discrimination capability of the installed Mantra SDK matcher using live hardware trials:

- **Primary Finger Trials**: 5 distinct captures (Quality 72–84, NFIQ 1–2).
- **Secondary (Cross-Finger) Trials**: 3 distinct captures (Quality 75–79, NFIQ 2).
- **Genuine Pairwise Matches (10 pairs)**:
  - Minimum Score: `5,535`
  - Maximum Score: `10,000`
  - Mean Score: `8,599`
- **Cross-Finger / Impostor Pairwise Matches (15 pairs)**:
  - Minimum Score: `0`
  - Maximum Score: `24`
  - Mean Score: `6`
- **Separation Margin**: **`5,511` points** (between genuine minimum 5,535 and cross-finger maximum 24).
- **Decision Threshold Selected**: **`1,400`** (on the 0–10,000 scale).
  - Observed False Accepts at 1,400: **0 / 15 (0.0%)**
  - Observed False Rejects at 1,400: **0 / 10 (0.0%)**

> [!NOTE]
> Gate 1 confirms empirical matcher discrimination for this device. It is a preliminary acceptance test, not a commercial certification across open demographic populations.

---

### Template Storage Specification (`.dat`)

All enrolled templates are stored per Windows User SID:
- **Directory**: `%ProgramData%\MFS100CP\templates\`
- **File Name**: `<UserSID>.dat` (e.g., `S-1-5-21-2956279811-1683308700-1261978901-1001.dat`)

#### Binary Layout (Version 1):

| Offset | Field | Data Type | Description |
|---|---|---|---|
| `0x00` | Magic Header | `uint32` | `0x4D465354` (ASCII: `"MFST"`) |
| `0x04` | Format Version | `int32` | `1` |
| `0x08` | User SID | `string` (7-bit prefixed) | Windows User SID string |
| `Var`  | Enrolled UTC | `int64` | `DateTime.UtcNow.Ticks` |
| `Var`  | Finger Count | `int32` | Number of enrolled fingers (`1` or `2`) |
| *Repeated per finger (max 2):* | | | |
| `+0x00`| Finger Index | `int32` | `0` = Primary, `1` = Secondary |
| `+0x04`| Label | `string` (7-bit prefixed) | Descriptive label (e.g. `"Right Index"`) |
| `Var`  | Quality | `int32` | Extraction quality score (0–100) |
| `Var`  | Enrolled UTC | `int64` | `DateTime.UtcNow.Ticks` |
| `Var`  | Template Length | `int32` | Length in bytes of ISO template |
| `Var`  | Template Bytes | `byte[]` | Raw ISO 19794-2 biometric minutiae bytes |

#### Security & Access Control Lists (ACLs):
Template files contain **zero passwords, PINs, or hashes**. File ACLs are strictly applied on creation:
- `NT AUTHORITY\SYSTEM`: FullControl
- `BUILTIN\Administrators`: FullControl
- `Target User SID`: Read, Write, Modify
- `All Others`: Denied

---

### Named Pipe IPC Protocol (`\\.\pipe\MFS100Bridge`)

`MFS100BridgeSvc` exposes a duplex named pipe. All messages are line-delimited JSON.

#### 1. Ping / Health Check
```json
// Request
{ "cmd": "ping" }

// Response
{ "ok": true, "servicePid": 1240, "workerPid": 5488 }
```

#### 2. Device Information
```json
// Request
{ "cmd": "device_info" }

// Response
{ "ok": true, "make": "MANTRA", "model": "MFS100", "serial": "49007331", "width": 316, "height": 354 }
```

#### 3. Live Fingerprint Capture
```json
// Request
{ "cmd": "capture", "timeoutMs": 10000 }

// Response
{
  "ok": true,
  "quality": 78,
  "nfiq": 2,
  "isoTemplate": "<base64_encoded_iso19794_2_template>",
  "ansiTemplate": "<base64_encoded_ansi378_template>"
}
```

#### 4. Template Matching
```json
// Request
{
  "cmd": "match",
  "probe": "<base64_probe_template>",
  "gallery": "<base64_gallery_template>"
}

// Response
{ "ok": true, "score": 9850 }
```

---

### Building from Source

#### Prerequisites:
- Windows 10/11 x64
- Microsoft .NET SDK (6.0, 7.0, 8.0, or 9.0)
- .NET Framework 4.8 Developer Pack / Targeting Pack

#### Build Commands:
From the root workspace directory, run:

```powershell
# 1. Build 32-bit Worker
dotnet build src/Worker/MFS100Worker.csproj -c Release -p:Platform=x86

# 2. Build 32-bit Bridge Service
dotnet build src/BridgeService/MFS100BridgeSvc.csproj -c Release -p:Platform=x86

# 3. Build Standalone Enrollment Tool
dotnet build src/EnrollmentTool/MFS100Enroll.csproj -c Release -p:Platform=x86
```

All native DLLs (`MFS100Dll.dll`, `iengine_ansi_iso.dll`, `MANTRA.MFS100.dll`) are automatically copied to the output directories (`bin\x86\Release\net48\`).

---

### Automated Verification & Boundary Tests

Run the built-in diagnostic test suite to verify storage boundaries, ACLs, and biometric matching:

```powershell
src\EnrollmentTool\bin\x86\Release\net48\MFS100Enroll.exe --boundary-test
```

This runs 10 automated unit and biometric tests:
1. **Test 1**: Single finger enrollment (Primary) -> `PASS`
2. **Test 2**: Secondary finger enrollment (Total 2) -> `PASS`
3. **Test 3 (AC 2.3)**: Strict 2-finger limit enforcement (`InvalidOperationException` on 3rd finger) -> `PASS`
4. **Test 4 (AC 2.7)**: Byte-level inspection verifying zero stored credentials -> `PASS`
5. **Test 5 & 6**: Finger and file deletion lifecycle -> `PASS`
6. **Test 7 (AC 2.2)**: Multi-scan consistency verification (`score >= 1400`) -> `PASS`
7. **Test 8 (AC 2.4 & 2.5)**: 1:1 Verification match and reject -> `PASS`
8. **Test 9 (AC 2.6)**: Cross-user account isolation (User A probe vs User B yields `< 1400`) -> `PASS`
9. **Test 10**: 1:N multi-user identification across enrolled accounts -> `PASS`

---

### Native Error Codes Reference

| Error Code | Constant | Meaning / Resolution |
|---|---|---|
| `0` | `MFS100_SUCCESS` | Operation completed successfully. |
| `-1140` | `MFS100_E_TIMEOUT` | No finger placed within timeout period. |
| `-1307` | `MFS100_E_CORRUPT` | Corrupted or invalid ISO 19794-2 template bytes. |
| `-1322` | `MFS100_E_NO_DEVICE` | Mantra MFS100 scanner disconnected or unpowered. |
| `-1337` | `MFS100_E_EXTRACT` | Failed to extract minutiae (finger placed too lightly). |
| `-1101` | `MFS100_E_MEMORY` | Insufficient memory allocated for image buffer. |
| `-1103` | `MFS100_E_LOAD_LIB` | Failed to load `iengine_ansi_iso.dll` dependency. |

---

## License & Privacy

- **Biometric Privacy**: Fingerprint images are never written to disk. Only standard ISO 19794-2 minutiae vectors are stored in user-isolated files protected by Windows ACLs.
- **Zero Secrets**: No passwords, PINs, or DPAPI keys are stored. Standard Windows authentication remains active at all times.
