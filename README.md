# Mantra MFS100 Windows 11 Biometric Authentication System

A secure, high-accuracy Windows 11 biometric authentication and enrollment solution for the **Mantra MFS100 Optical USB Fingerprint Scanner**, built directly upon the official Mantra native SDK without relying on Aadhaar RD Service.

> **Status**: **Phase 2 Verified (13/13 Automated Security & Boundary Tests Passed)**  
> **Master Architecture & VM Blueprint**: Delivered & Available in [`docs/revised_architecture_specification.md`](docs/revised_architecture_specification.md) and [`docs/vm_test_harness_and_rollback_runbook.md`](docs/vm_test_harness_and_rollback_runbook.md).  
> **Host Safety Guarantee**: **Zero Credential Provider DLLs or drivers are registered on the host operating system.** Standard Windows Password and PIN sign-in remain 100% untouched and always available. All Credential Provider experiments are strictly isolated to a Windows 11 Virtual Machine.

---

## Important Security Disclosures & Architectural Boundaries

> [!CAUTION]
> ### 1. Zero Persistent Plaintext Passwords on Disk
> Permanent storage of Windows passwords, PINs, or password hashes on disk in plaintext or with ordinary reversible software encryption is **strictly prohibited**. No plaintext credential files exist on disk.

> [!WARNING]
> ### 2. The Four Target Outcomes: Technical Reality & Verification Status
> - **Target 1: Cold-Boot 1-Touch Login (Experimental Persistent Encrypted Storage - Unproven)**:  
>   Windows interactive authentication (`LsaLogonUser`) strictly requires the user's plaintext password (`MSV1_0_INTERACTIVE_LOGON`) at cold boot to derive user DPAPI Master Keys (`PBKDF2(Password, UserSID)`). To evaluate one-touch login without plaintext storage, Phase 3 designs an **Experimental Persistent Encrypted Storage Vault** using Windows CNG Platform Crypto Provider (`MS_PLATFORM_KEY_STORAGE_PROVIDER`) inside vTPM 2.0. Before any login attempt, three prerequisites must be proven in the VM: (1) hardware key protection in TPM silicon, (2) biometric release authorization (`score >= 1,400`), and (3) password-change recovery.
> - **Target 2: Windows Hello PIN Coexistence (Documented Interface - Validated)**:  
>   A Windows Hello PIN is an NGC authorization secret bound to TPM hardware; it is **not** an ordinary password. Passing a PIN to `MSV1_0` returns `STATUS_LOGON_FAILURE`. NGC APIs are private and protected by LSA PPL. The MFS100 tile operates strictly alongside native PINs under "Sign-in options" without intercepting or simulating PIN flows.
> - **Target 3: Native Windows Hello Fingerprint (Hard Blocked on Original SDK - Experimental Research)**:  
>   Windows 11 Windows Biometric Framework (WBF / Windows Hello) requires signed **64-bit (`x64`)** UMDF 2.0 WBDI drivers and engine adapters. The Mantra MFS100 manufacturer provides only proprietary **32-bit (x86) legacy binaries** (`MFS100Dll.dll`, `iengine_ansi_iso.dll`). A 64-bit service cannot load 32-bit DLLs. Custom WBDI integration is treated as **unproven experimental research** until raw USB bulk endpoints, driver implementation, WHQL signature requirements, and Windows Hello enrollment are independently verified inside the VM.
> - **Target 4: Mandatory System-Wide 2FA (Explicitly Unresolved & Blocked)**:  
>   We do **not** claim mandatory system-wide 2FA is achieved by `ICredentialProviderFilter`. Unconditional filtering creates a **catastrophic permanent lockout hazard** if the scanner is disconnected or fails, and third-party filters are not loaded in Windows Safe Mode. This target remains **explicitly unresolved** until a supported enforcement architecture with hardware failsafes is demonstrated.

> [!IMPORTANT]
> ### 3. Host Protection Policy & VM Isolation
> All Credential Provider and driver experiments are strictly restricted to an isolated **Windows 11 Virtual Machine (VMware Workstation Pro 17+)** with physical USB 2.0 passthrough, read-only shared folders, and a verified snapshot hierarchy (`Clean_Pre_CP_Baseline`).

---

## Table of Contents

- [Part 1: End-User & Enrollment Guide](#part-1-end-user--enrollment-guide)
  - [Hardware & Driver Requirements](#hardware--driver-requirements)
  - [Quick Start](#quick-start)
  - [Enrolling Your Fingerprints](#enrolling-your-fingerprints)
  - [Verifying Enrolled Fingerprints (1:1)](#verifying-enrolled-fingerprints-11)
  - [Multi-User Identification (1:N)](#multi-user-identification-1n)
  - [Deleting Fingerprints](#deleting-fingerprints)
  - [Troubleshooting & Emergency Recovery](#troubleshooting--emergency-recovery)
- [Part 2: Developer & Security Architecture Manual](#part-2-developer--security-architecture-manual)
  - [Privileged Broker Architecture](#privileged-broker-architecture)
  - [Why 32-bit Process Isolation is Mandatory](#why-32-bit-process-isolation-is-mandatory)
  - [Gate 1 Biometric Verification Results](#gate-1-biometric-verification-results)
  - [Template Protection at Rest (DPAPI + SID Entropy)](#template-protection-at-rest-dpapi--sid-entropy)
  - [Named Pipe IPC Protocol (`\\.\pipe\MFS100Bridge`)](#named-pipe-ipc-protocol-pipemfs100bridge)
  - [Building from Source](#building-from-source)
  - [Automated Verification & Boundary Tests (13/13 Passed)](#automated-verification--boundary-tests-1313-passed)
  - [Windows 11 VM Test Harness Blueprint](#windows-11-vm-test-harness-blueprint)
  - [Native Error Codes Reference](#native-error-codes-reference)

---

# Part 1: End-User & Enrollment Guide

### Hardware & Driver Requirements

- **Device**: Mantra MFS100 Optical Fingerprint Scanner (USB)
- **Hardware IDs**: `USB\VID_2C0F&PID_1005`
- **Operating System**: Windows 11 (64-bit) or Windows 10 (64-bit)
- **Official Driver**: Mantra MFS100 Driver v9.0.2.5 (Installed in `C:\Program Files\Mantra\MFS100\Driver`)
- **No Aadhaar RD Service Needed**: Interacts directly with the driver and SDK; RD service is unnecessary and should not be running.
- **Runtime**: Microsoft .NET Framework 4.8 (built into Windows 11).

---

### Quick Start

1. Plug your **Mantra MFS100 scanner** into a USB 2.0 or 3.0 port.
2. Confirm the red optical sensor LED blinks upon connection.
3. Launch the standalone enrollment application:
   ```cmd
   run_enrollment.bat
   ```
4. The interactive console menu appears:
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
     6. Run Automated Template Boundary Tests (13 Security & Biometric Tests)
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
5. **The 3-Scan Consistency Process**:
   - The tool prompts for **3 consecutive captures** of the *exact same finger*.
   - **Crucial**: Clean the optical glass and **lift your finger completely** between each capture.
   - Each capture must meet a minimum quality score of **`>= 60`** (recommended: 70–85).
   - The application automatically cross-matches all 3 scans (`Scan 1 vs 2`, `Scan 2 vs 3`, `Scan 1 vs 3`). All pairs must match with a score **`>= 1,400`**.
   - The capture with the highest biometric quality is encrypted and saved to your user template.

> [!NOTE]
> Each Windows user SID is strictly limited to a **maximum of 2 enrolled fingers**.

---

### Verifying Enrolled Fingerprints (1:1)

Option **`3`** (or `--verify <username>`) tests 1:1 biometric verification:
1. Select your username.
2. Place your enrolled finger firmly and flatly on the scanner platen.
3. The scanner captures a fresh live probe and matches it against your enrolled templates:
   - **Match Success**: Score is **`>= 1,400`** (typically 6,000–10,000). The screen displays `[MATCH SUCCESS]` and identifies which finger matched.
   - **Access Denied**: If an unenrolled or different finger is placed, the score drops below 1,400 (typically 0–24) and the screen displays `[ACCESS DENIED]`.

---

### Multi-User Identification (1:N)

Option **`4`** (or `--identify`) tests 1:N cross-account identification:
1. Place any enrolled finger on the scanner.
2. The engine evaluates the probe against all enrolled Windows SIDs on the system.
3. It uniquely resolves which Windows user account you belong to and reports the winning match score.

---

### Deleting Fingerprints

Option **`5`** (or `--delete <username> [fingerIndex]`):
- To remove a specific finger (e.g., Finger 1) while keeping Finger 0, specify `0` or `1`.
- To completely delete all enrolled biometric data for an account, choose `A` (All).

---

### Troubleshooting & Emergency Recovery

- **Red Light Doesn't Turn On**:
  - Unplug the scanner, wait 3 seconds, and plug it back in.
  - Check Windows Device Manager under `Universal Serial Bus controllers` for `Mantra MFS100`.
- **Quality Too Low (`< 60`)**:
  - Clean the glass platen of any grease or dust.
  - Press your finger flatly in the center of the optical window with moderate pressure.
- **Inconsistent Scans Warning During Enrollment**:
  - If you switch fingers during the 3 enrollment scans, the consistency check rejects the enrollment to prevent saving corrupted data. Use the same finger for all 3 scans.
- **Windows Sign-in Recovery**:
  - The standard Windows Password and PIN options are **always active** on your system under "Sign-in options".

---

# Part 2: Developer & Security Architecture Manual

### Privileged Broker Architecture

Windows 11 `LogonUI.exe` is a 64-bit process running in Session 0/1. The Mantra MFS100 SDK is strictly a 32-bit (x86) native library. In addition, standard users must have **zero direct access** to encrypted template storage.

To solve this, the architecture implements the **Privileged Broker Pattern**:

```mermaid
graph TD
    subgraph User / Client Session
        CLI[MFS100Enroll.exe - Named Pipe Client]
        VMCP[MFS100CP.dll - VM Credential Provider]
    end

    subgraph Privileged Service Layer - Session 0 (SYSTEM)
        PIPE[\\.\pipe\MFS100Bridge - Named Pipe Server]
        SVC[MFS100BridgeSvc.exe - Windows Service]
        STORE[(%ProgramData%\MFS100CP\templates\)]
    end

    subgraph Native Isolation Layer - 32-bit x86
        HOST[WorkerHost Process Manager]
        WORKER[MFS100Worker.exe - 32-bit x86]
        SDK[MANTRA.MFS100.dll + MFS100Dll.dll]
        DEV[(Mantra MFS100 USB Hardware)]
    end

    CLI -->|Named Pipe IPC / Token Auth| PIPE
    VMCP -->|Named Pipe IPC| PIPE
    PIPE --> SVC
    SVC -->|DPAPI LocalMachine + SID Entropy| STORE
    SVC --> HOST
    HOST -->|stdin / stdout JSON IPC| WORKER
    WORKER --> SDK
    SDK --> DEV
```

1. **Privileged Storage Broker**: Standard users and the client tool (`MFS100Enroll.exe`) perform **zero direct template file I/O**. All read, write, match, and delete commands route through `\\.\pipe\MFS100Bridge`.
2. **Caller Authorization**: The service inspects the client token via `RunAsClient`. A non-elevated user can only access their own SID (cross-SID requests receive 403 Forbidden).
3. **Directory Security**: `%ProgramData%\MFS100CP\templates` has inheritance disabled and ACLs restricted strictly to `NT AUTHORITY\SYSTEM` and `BUILTIN\Administrators`. Standard users cannot open or modify template files directly.

---

### Why 32-bit Process Isolation is Mandatory

1. **Binary Bit-Width Incompatibility**:
   The Mantra driver package includes:
   - `MFS100Dll.dll` (x86 32-bit PE, Machine: `0x014C`)
   - `iengine_ansi_iso.dll` (x86 32-bit minutiae extraction & matching engine)
   - `MANTRA.MFS100.dll` (x86 32-bit .NET interop assembly)
   A 64-bit process cannot load 32-bit DLLs (`BadImageFormatException` / `0x8007000B`).
2. **Crash Resilience**:
   Hosting the SDK inside `MFS100Worker.exe` isolates native USB faults. `MFS100BridgeSvc` enforces hard deadlines (`timeoutMs + 2500ms`) and auto-restarts a terminated worker within milliseconds without interrupting the operating system.

---

### Gate 1 Biometric Verification Results

Evaluated using live physical trials with Mantra MFS100 scanner (`49007331`):

- **Genuine Pairwise Matches (10 pairs)**: Min = `5,535`, Max = `10,000`, Mean = `8,599`.
- **Cross-Finger Pairwise Matches (15 pairs)**: Min = `0`, Max = `24`, Mean = `6`.
- **Separation Margin**: **`5,511` points** (between genuine minimum 5,535 and cross-finger maximum 24).
- **Decision Threshold Selected**: **`1,400`** (on the 0–10,000 scale).
  - Observed False Accepts at 1,400: **0 / 15 (0.0%)**
  - Observed False Rejects at 1,400: **0 / 10 (0.0%)**

---

### Template Protection at Rest (DPAPI + SID Entropy)

All enrolled templates are encrypted at rest per Windows User SID:
- **Location**: `%ProgramData%\MFS100CP\templates\<UserSID>.dat`
- **Encryption Algorithm**: DPAPI Machine Scope (`DataProtectionScope.LocalMachine`) via `ProtectedData.Protect`.
- **Context Binding**: The target user's Windows SID string (`Encoding.UTF8.GetBytes(userSid)`) is passed as optional entropy. Ciphertext is cryptographically bound to that exact user; renaming or swapping the file to another SID causes decryption to fail (`CryptographicException`).
- **Zero Plaintext Biometrics**: Files on disk are purely ciphertext (`Raw Magic != MFST`). Plaintext ISO 19794-2 vectors exist only in volatile memory during active matching.
- **Directory Security & Access Control**: `%ProgramData%\MFS100CP\templates` has inheritance disabled and ACLs restricted strictly to `NT AUTHORITY\SYSTEM` and `BUILTIN\Administrators` only.
- **Privileged Brokerage Enforcement**: Standard users have **zero direct file access** (direct read/write denied). All template reads, writes, matches, and deletions broker exclusively through `\\.\pipe\MFS100Bridge` running as `SYSTEM`. The service verifies caller tokens via `RunAsClient` (rejecting cross-SID access with 403 Forbidden).

---

### Named Pipe IPC Protocol (`\\.\pipe\MFS100Bridge`)

`MFS100BridgeSvc` exposes a duplex named pipe using line-delimited JSON messages:

```json
// Ping / Health Check
--> { "cmd": "ping" }
<-- { "ok": true, "servicePid": 1240, "workerPid": 5488 }

// Live Fingerprint Capture
--> { "cmd": "capture", "timeoutMs": 10000 }
<-- { "ok": true, "quality": 78, "nfiq": 2, "isoTemplate": "<base64>" }

// Biometric Verification Against Enrolled User
--> { "cmd": "match_user", "sid": "S-1-5-21-...", "probe": "<base64>", "threshold": 1400 }
<-- { "ok": true, "accessGranted": true, "score": 9850, "label": "Right Index" }

// Multi-User Identification (1:N) Across Enrolled Accounts
--> { "cmd": "identify_user", "probe": "<base64>", "threshold": 1400 }
<-- { "ok": true, "matched": true, "userSid": "S-1-5-21-...", "score": 9850 }
```

---

### Building from Source

```powershell
# 1. Build 32-bit Native Worker
dotnet build src/Worker/MFS100Worker.csproj -c Release -p:Platform=x86

# 2. Build Session-0 Bridge Service
dotnet build src/BridgeService/MFS100BridgeSvc.csproj -c Release -p:Platform=x86

# 3. Build Standalone Enrollment Application
dotnet build src/EnrollmentTool/MFS100Enroll.csproj -c Release -p:Platform=x86
```

All native DLLs (`MFS100Dll.dll`, `iengine_ansi_iso.dll`, `MANTRA.MFS100.dll`) are automatically placed beside the executables.

---

### Automated Verification & Boundary Tests (13/13 Passed)

Run the automated security, cryptographic, and biometric test suite:

```powershell
src\EnrollmentTool\bin\x86\Release\net48\MFS100Enroll.exe --boundary-test
```

All 13 security and biometric boundary tests pass cleanly:

1. **Test 1**: Single finger enrollment (Primary) -> `PASS`
2. **Test 2**: Secondary finger enrollment (Total 2) -> `PASS`
3. **Test 3 (AC 2.3)**: Strict 2-finger limit enforcement (`InvalidOperationException` on 3rd finger) -> `PASS`
4. **Test 4 (AC 2.9 & 2.7)**: DPAPI LocalMachine encryption at rest & zero plaintext credentials -> `PASS`
5. **Test 5 (AC 2.11)**: Cryptographic rejection on SID context mismatch (`CryptographicException`) -> `PASS`
6. **Test 6 (AC 2.12)**: IPC caller authorization check (403 Forbidden for cross-SID standard users) -> `PASS`
7. **Test 7 (AC 2.10)**: Directory security & strict ACL enforcement (`SYSTEM` & `Administrators` only) -> `PASS`
8. **Test 8**: Delete Finger 0, verify Finger 1 persists -> `PASS`
9. **Test 9**: Clean up test enrollment -> `PASS`
10. **Test 10 (AC 2.2)**: Multi-scan consistency verification (`score >= 1,400`) -> `PASS`
11. **Test 11 (AC 2.4 & 2.5)**: 1:1 Verification match and reject -> `PASS`
12. **Test 12 (AC 2.6)**: Cross-user account isolation (Zero false accepts across accounts) -> `PASS`
13. **Test 13**: 1:N Multi-user identification across all enrolled SIDs -> `PASS`

---

### Windows 11 VM Test Harness Blueprint

All Credential Provider experiments are strictly isolated to a **Windows 11 VM in VMware Workstation Pro 17+**:

- **Virtual Hardware**: UEFI + Secure Boot + Virtual TPM 2.0 (vTPM).
- **Physical USB Passthrough**: Mantra MFS100 (`VID 0x2C0F, PID 0x1005`) mapped via VMware USB 2.0 Controller.
- **Read-Only Artifact Transfer**: Host folder mounted as `MFS100_ReadOnly` with **`Read-only = TRUE`**.
- **Snapshot Hierarchy**: Master baseline snapshot **`Clean_Pre_CP_Baseline`** established before any Credential Provider registry registration.
- **4-Tier Disaster Recovery**:
  1. *Tier 1*: UI Sign-in Options switch to native Password tile.
  2. *Tier 2*: Windows Safe Mode boot and PowerShell registry purge.
  3. *Tier 3*: WinRE offline registry hive repair (`reg load HKLM\OFFLINE_SOFTWARE`).
  4. *Tier 4*: Instant hypervisor snapshot revert (~2 seconds full OS restoration).

Detailed VM setup and recovery procedures: [`docs/vm_test_harness_and_rollback_runbook.md`](docs/vm_test_harness_and_rollback_runbook.md).

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

## Documentation Index

- **Master Architecture Specification**: [`docs/revised_architecture_specification.md`](docs/revised_architecture_specification.md)
- **VM Test Harness & Rollback Runbook**: [`docs/vm_test_harness_and_rollback_runbook.md`](docs/vm_test_harness_and_rollback_runbook.md)
- **Phase 2 Results & Metrics**: [`docs/phase2_results.md`](docs/phase2_results.md)
- **Implementation Plan v3.0**: [`implementation_plan.md`](implementation_plan.md)
