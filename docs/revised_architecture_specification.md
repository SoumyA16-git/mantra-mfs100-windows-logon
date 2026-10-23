# Comprehensive Architecture Specification: Mantra MFS100 Windows 11 Biometric Authentication

**Document Version**: 3.0 (Master Architectural Specification)  
**Date**: 2026-10-23  
**Status**: Formal Architectural Review (Planning & Specification Phase ONLY)  
**Strict Safety Guarantee**: **Zero Credential Provider registration, driver installation, or authentication changes on the host system.** All experiments are strictly confined to an isolated Windows 11 Virtual Machine.

---

## 1. Executive Charter & Non-Negotiable Requirements

This specification defines the architectural roadmap for integrating the **original Mantra MFS100 USB Optical Fingerprint Scanner** into the Windows 11 authentication subsystem.

### Non-Negotiable System Principles:
1. **Original Hardware Preservation**: Strictly maintain compatibility with the physical Mantra MFS100 device (`USB\VID_2C0F&PID_1005`, Serial `49007331`).
2. **Preservation of All Four Target Outcomes**:
   - **Target 1**: Cold-Boot One-Touch Fingerprint Login.
   - **Target 2**: Correct Windows Hello PIN Integration & Coexistence.
   - **Target 3**: Native Windows Hello Fingerprint Integration (WBF / WBDI).
   - **Target 4**: Mandatory System-Wide Two-Factor Authentication (2FA).
3. **No Silent Goal Substitution**: We do **not** redefine the project as merely an optional desktop application. The four target outcomes remain the governing architectural targets.
4. **Honest Architectural Classification**: Every target outcome must be transparently classified into:
   - **Documented & Supported**: Achievable via published Microsoft APIs.
   - **Experimental**: Achievable via advanced engineering, custom drivers, or TPM vaults in an isolated VM.
   - **Hard Blocked**: Impassable due to Windows 11 security architecture (PPL, VBS, Credential Guard) or hardware limitations of the 32-bit optical scanner.
5. **Zero Plaintext Credentials at Rest**: Reusable Windows passwords, hashes, and PINs must **never** be stored in plaintext on disk, registry, or event logs.
6. **No Windows System Binary Patching**: Windows system files (`winlogon.exe`, `LogonUI.exe`, `lsass.exe`, `authz.dll`, `ngcpop.dll`) must remain untouched.
7. **Complete Host Isolation**: All Credential Provider and driver experiments are restricted exclusively to a dedicated Windows 11 VM with USB passthrough and rollback snapshots.

---

## 2. Exhaustive Architectural Breakdown of the Four Target Outcomes

```
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│                           FOUR TARGET OUTCOMES: FEASIBILITY SPECTRUM                          │
├──────────────────────────┬─────────────────────────────┬───────────────────────────────────────┤
│ Target Outcome           │ Architectural Status        │ Primary Technical Mechanism / Blocker │
├──────────────────────────┼─────────────────────────────┼───────────────────────────────────────┤
│ 1. Cold-Boot One-Touch   │ EXPERIMENTAL (In-VM Only)   │ TPM 2.0 CNG Hardware Vault (PCP/KSP)  │
│    Login                 │ Blocked on MSV1_0 natively  │ LSA requires password for user DPAPI  │
├──────────────────────────┼─────────────────────────────┼───────────────────────────────────────┤
│ 2. Windows Hello PIN     │ DOCUMENTED (Side-by-Side)   │ Native NgcCredProv.dll / TPM NGC      │
│    Integration           │ Blocked from custom reuse   │ PIN is not a password; NGC is private │
├──────────────────────────┼─────────────────────────────┼───────────────────────────────────────┤
│ 3. Native Windows Hello  │ HARD BLOCKED (Original SDK) │ WBF requires 64-bit UMDF 2.0 WBDI     │
│    Fingerprint (WBF)     │ EXPERIMENTAL (Custom 64-bit)│ Mantra SDK is strictly 32-bit x86     │
├──────────────────────────┼─────────────────────────────┼───────────────────────────────────────┤
│ 4. Mandatory System-Wide │ EXPERIMENTAL (In-VM Only)   │ ICredentialProviderFilter             │
│    2FA Enforcement       │ High Lockout Hazard         │ Must feature dynamic hardware failsafe│
└──────────────────────────┴─────────────────────────────┴───────────────────────────────────────┘
```

---

### 2.1 Target Outcome 1: Cold-Boot One-Touch Fingerprint Login

#### 2.1.1 Target Goal:
A user powers on their Windows 11 machine, touches the Mantra MFS100 scanner once at the logon screen, and is authenticated directly into their local Windows desktop session without typing a password or PIN.

#### 2.1.2 Windows 11 Internal Authentication Reality:
At interactive cold-boot logon, Windows `LogonUI.exe` calls `LsaLogonUser` via the Local Security Authority (`lsass.exe`). For local SAM accounts, authentication is handled by the **`MSV1_0`** authentication package:
1. `MSV1_0` requires the documented serialization `MSV1_0_INTERACTIVE_LOGON`.
2. This structure **fundamentally mandates the plaintext password string** (`UNICODE_STRING`).
3. **The DPAPI Master Key Requirement**: When a user logs in, Windows initializes the Data Protection API (DPAPI). The user's DPAPI Master Key is stored in `%APPDATA%\Microsoft\Protect\{SID}` encrypted with a key derived via `PBKDF2(Password, UserSID)`. If `LsaLogonUser` is not provided with the plaintext password, the DPAPI Master Key **cannot be decrypted**. Consequently, the user's profile is severely degraded: saved Edge/Chrome credentials, Wi-Fi passwords, VPN secrets, Certificate stores, and EFS encrypted files fail to open.
4. **NTLM Hash Interactive Logon**: Windows `MSV1_0` does **not** support interactive desktop logon using an NTLM hash alone (`MSV1_0_LM20_LOGON` only creates a non-interactive network session without an interactive desktop window station).

#### 2.1.3 Why Documented Interfaces Alone Cannot Meet This Without Storage:
Because `MSV1_0` requires the plaintext password, a true "one-touch" logon without user password entry requires that the password be supplied to `LsaLogonUser` on the user's behalf. Storing that password on disk in plaintext or with ordinary reversible software encryption violates non-negotiable security requirements.

#### 2.1.4 Experimental Architecture (In-VM Only): TPM 2.0 Hardware-Sealed Vault
To achieve cold-boot one-touch login without plaintext disk storage, we design an **Experimental TPM 2.0 Hardware-Sealed Vault**:
1. **Enrollment Phase (One-Time Setup in VM)**:
   - User enters their Windows local password during enrollment in `MFS100Enroll.exe`.
   - The password is sealed using **Windows CNG Platform Crypto Provider (`MS_PLATFORM_KEY_STORAGE_PROVIDER`)** inside the virtual TPM 2.0 chip.
   - The encryption key is an asymmetric hardware key pair generated inside the TPM hardware enclave (`NCRYPT_MACHINE_KEY_FLAG`). The private key is non-exportable and never leaves the TPM silicon.
   - The encrypted credential blob is saved in `%ProgramData%\MFS100CP\vault\{UserSID}.dat` with strict ACLs restricted to `NT AUTHORITY\SYSTEM` only.
2. **Pre-Logon Authentication Phase**:
   - At cold boot, Session-0 `MFS100BridgeSvc` initializes the MFS100 hardware via `MFS100Worker.exe`.
   - User touches the MFS100 scanner.
   - Biometric matching occurs in `MFS100Worker.exe`. If and only if `score >= 1,400`:
     - `MFS100BridgeSvc` requests the TPM 2.0 chip to unseal the password into a pinned memory buffer.
     - The password is provided to the custom Credential Provider tile via authenticated local Named Pipe IPC.
     - The tile constructs `MSV1_0_INTERACTIVE_LOGON` and yields it to `LogonUI.exe` in `GetSerialization`.
     - The tile immediately sanitizes all transient buffers with native `SecureZeroMemory`.
3. **Recovery on Password Desynchronization**:
   - If the user changes their Windows password, the TPM vault holds the stale password. `LsaLogonUser` returns `STATUS_LOGON_FAILURE` (`0xC000006D`).
   - The tile intercepts this failure, displays an explicit message: *"Windows password changed. Sign in with standard password to update fingerprint vault."*, and switches focus to the native recovery tile.

#### 2.1.5 Acceptance Criteria for Target 1:
- [ ] **AC 1.1**: At cold boot, touching the MFS100 scanner with an enrolled finger authenticates into Windows desktop without typing a password.
- [ ] **AC 1.2**: Password ciphertext is cryptographically bound to TPM 2.0 hardware PCRs; zero plaintext passwords appear in files, registry, or event logs.
- [ ] **AC 1.3**: Pinned memory buffers containing the unsealed password are sanitized with `SecureZeroMemory` immediately following `GetSerialization`.
- [ ] **AC 1.4**: Touching the scanner with an unenrolled/impostor finger rejects unsealing (`score < 1,400`), leaves the session locked, and clears memory.
- [ ] **AC 1.5**: When Windows password is changed, the tile gracefully detects `STATUS_LOGON_FAILURE` and redirects to native password recovery.

---

### 2.2 Target Outcome 2: Correct Windows Hello PIN Integration

#### 2.2.1 Target Goal:
Seamless interoperability with the user's existing Windows Hello PIN, ensuring that PINs are properly handled, neither misused as passwords nor corrupted by third-party code, and that native PIN recovery remains 100% functional.

#### 2.2.2 Windows 11 Internal Authentication Reality:
1. **The Architectural Nature of a Windows Hello PIN**:
   - A Windows Hello PIN is **not a password**. It is an authorization secret used to release an asymmetric cryptographic credential stored in a TPM-backed Next Generation Credential (NGC) container.
   - The PIN never leaves the local machine and is never transmitted over a network.
   - In a local account, Windows Hello creates a synthetic token during PIN setup and seals it inside the TPM. Unsealing requires the correct PIN to be verified by TPM hardware auth.
2. **Hard Blocker 2.1: `MSV1_0` Rejection**:
   - If a third-party Credential Provider captures a Windows Hello PIN and passes it to `MSV1_0_INTERACTIVE_LOGON` as a password, authentication **fails instantly** (`STATUS_LOGON_FAILURE`). Local SAM only validates the real account password hash.
3. **Hard Blocker 2.2: Private, Undocumented NGC APIs**:
   - Windows Hello NGC containers are managed by Microsoft internal libraries (`ngcpop.dll`, `ngcctnr.dll`, `NgcCrypt...`). Microsoft does not publish public SDKs or C++ APIs allowing third-party Credential Providers to invoke NGC unsealing or trigger an NGC logon.
4. **Hard Blocker 2.3: LSA Protected Process Light (PPL) & Credential Guard**:
   - In Windows 11, `lsass.exe` and `LogonUI.exe` enforce PPL. Third-party binaries cannot hook, subclass, or inject into Microsoft's inbox `NgcCredProv.dll` or `PinCredProv.dll`.

#### 2.2.3 Feasible Architecture: Dedicated Coexistence & Clear Separation
- **Strict Separation of Concerns**: The custom MFS100 Credential Provider tile will **never** request, capture, serialize, or process a Windows Hello PIN.
- **Side-by-Side Coexistence**: Microsoft's inbox PIN Credential Provider (`{d68ddaee-cdb2-41f0-8152-7634fbf6c26f}`) remains fully enabled and unmodified under "Sign-in options".
- **Zero Interception**: If a user prefers to log in with their PIN, they click "Sign-in options" -> PIN tile. The MFS100 provider completely steps aside.

#### 2.2.4 Acceptance Criteria for Target 2:
- [ ] **AC 2.1**: Native Windows Hello PIN sign-in remains active, selectable, and fully functional on the logon screen.
- [ ] **AC 2.2**: The MFS100 tile explicitly labels its input fields as "Password", never prompting for or accepting a Windows Hello PIN.
- [ ] **AC 2.3**: MFS100 code contains zero hooks or references to private `ngcpop.dll` APIs.

---

### 2.3 Target Outcome 3: Native Windows Hello Fingerprint Integration (WBF / WBDI)

#### 2.3.1 Target Goal:
The Mantra MFS100 appears in Windows 11 **Settings -> Accounts -> Sign-in options** as a recognized "Fingerprint recognition (Windows Hello)" device. The native Windows Hello setup wizard enrolls fingers, and Windows Hello's native `BioCredProv.dll` logs the user in automatically upon touch.

#### 2.3.2 The Windows Biometric Framework (WBF) Architecture:
The Microsoft-documented path for native Windows Hello fingerprint support is the **Windows Biometric Framework (WBF)**:
- **WBDI Driver (Windows Biometric Driver Interface)**: A User-Mode Driver Framework (UMDF 2.0) driver that manages USB communication with the scanner.
- **WBF Adapters**:
  - **Sensor Adapter**: Manages sensor capture, sleep, and wake states.
  - **Engine Adapter**: Extracts minutiae features and executes biometric template matching.
  - **Storage Adapter**: Securely stores biometric templates.

#### 2.3.3 Hard Blockers with Original Mantra MFS100 SDK:
1. **Hard Blocker 3.1: 64-bit OS vs. 32-bit Legacy SDK**:
   - Windows 11 is strictly a 64-bit operating system (`x64` / `ARM64`).
   - The Windows Biometric Service (`WbioSrvc.exe`) runs as a 64-bit Windows service.
   - All UMDF 2.0 driver host processes (`WUDFHost.exe`) and WBF Adapter DLLs must be 64-bit binaries.
   - **The Mantra MFS100 native libraries are 32-bit (x86) legacy binaries**:
     - `MFS100Dll.dll`: PE32 executable for Intel 80386 (Machine: `0x014C`).
     - `iengine_ansi_iso.dll`: 32-bit optical extraction & matching engine.
   - A 64-bit service (`WbioSrvc.exe`) **cannot** load 32-bit DLLs into its address space. Windows architecture prohibits 64-bit processes from loading 32-bit in-process COM adapters or DLLs.
2. **Hard Blocker 3.2: WHQL & Driver Signature Enforcement (DSE)**:
   - Windows 11 enforces Kernel Driver Signature Enforcement and UMDF driver signature checks.
   - A custom WBDI driver requires a Microsoft WHQL-attested driver catalog signature. Unsigned custom drivers fail to load on production Windows 11 unless Test-Signing mode (`bcdedit /set testsigning on`) is enabled.
3. **Hard Blocker 3.3: Windows Hello Enhanced Sign-in Security (ESS)**:
   - Windows 11 22H2+ enforces Enhanced Sign-in Security (ESS) for Windows Hello biometrics.
   - ESS requires biometric matching to execute inside a hardware-isolated Virtual Secure Mode (VSM) enclave (Virtualization-Based Security).
   - The legacy Mantra MFS100 optical scanner does not support secure channel encryption or VSM enclave execution.

#### 2.3.4 Experimental Research Architecture (In-VM Only): Custom 64-bit UMDF 2.0 Bridge
Custom WBDI driver integration is classified strictly as **unproven experimental research**. Claims of native Windows Hello integration cannot be made until the following components are independently proven and verified inside the VM:
1. **Raw USB Bulk Protocol**: Reverse-engineering or implementing USB bulk transfers directly via `WinUSB` without relying on 32-bit `MFS100Dll.dll`.
2. **64-bit Driver Implementation**: Creating a 64-bit UMDF 2.0 driver that satisfies Windows 11 kernel driver architecture.
3. **WHQL Code Signing Requirements**: Overcoming driver signature enforcement in production vs test-signing in the VM (`bcdedit /set testsigning on`).
4. **Native Windows Hello Enrollment**: Successfully passing Windows Hello enrollment wizard frames to WBF.

#### 2.3.5 Acceptance Criteria for Target 3 (Experimental Gate):
- [ ] **AC 3.1**: Windows 11 Device Manager in VM recognizes Mantra MFS100 as a `Biometric device` with a valid custom 64-bit WBDI driver.
- [ ] **AC 3.2**: Windows Settings displays "Fingerprint recognition (Windows Hello): Available".
- [ ] **AC 3.3**: Windows Hello enrollment wizard captures fingers and completes enrollment natively.
- [ ] **AC 3.4**: Native `BioCredProv.dll` authenticates the user at Win+L unlock screen.

---

### 2.4 Target Outcome 4: Mandatory System-Wide Two-Factor Authentication (2FA)

#### 2.4.1 Target Goal:
Enforce that no user can sign in to Windows using their password alone. A verified physical fingerprint scan on the Mantra MFS100 must be strictly mandatory for every Windows interactive login.

#### 2.4.2 Documented Windows Interfaces & Status: EXPLICITLY UNRESOLVED & BLOCKED
- We do **not** claim mandatory system-wide 2FA is achieved by `ICredentialProviderFilter`.
- This target remains **explicitly unresolved and blocked** until a supported enforcement architecture with verified hardware failsafes is demonstrated.

#### 2.4.3 The Critical Catastrophic Lockout Hazard:
If native Password and PIN providers are filtered out, and:
- The Mantra MFS100 USB cable is disconnected or damaged,
- The USB controller or optical prism malfunctions,
- The background service crashes or deadlocks,
- The user has a bandaged or injured finger,
**The machine becomes completely inaccessible.** No user or administrator can log in interactively to repair the issue. Furthermore, `ICredentialProviderFilter` can be bypassed by booting into Windows Safe Mode, meaning true system-wide 2FA is not enforced at the kernel/LSA level.

#### 2.4.4 Safe Experimental Research Path (In-VM Only): Dynamic Hardware-Gated Filter
To investigate 2FA without catastrophic lockout, an experimental **Dynamic Hardware-Gated Credential Provider Filter** will be evaluated exclusively inside the VM:
1. **Dynamic Health & Presence Check**:
   In `Filter()`, the filter dynamically polls `MFS100BridgeSvc`:
   - Is the Mantra MFS100 physically connected and responsive (`VID 0x2C0F, PID 0x1005`)?
   - Is `MFS100BridgeSvc` in a healthy, running state?
   - Is the target user enrolled with at least one valid fingerprint?
2. **Conditional Enforcement & Failsafe**:
   - **If ALL checks PASS**: The filter sets `rgbAllow[PasswordProvider] = FALSE`. Only the MFS100 2FA tile is shown.
   - **If ANY check FAILS (Emergency Failsafe)**: The filter automatically sets `rgbAllow[PasswordProvider] = TRUE`. Standard Windows password sign-in is immediately unhidden, allowing recovery.
3. **Safe Mode Immunity**: Safe Mode always bypasses third-party filters for guaranteed recovery.

#### 2.4.5 Acceptance Criteria for Target 4 (Experimental Gate):
- [ ] **AC 4.1**: When MFS100 is connected and healthy, native Password and PIN tiles are suppressed; only the MFS100 2FA tile is presented.
- [ ] **AC 4.2**: Unplugging the MFS100 USB cable causes the filter to unhide native Password/PIN tiles within 2 seconds.
- [ ] **AC 4.3**: Stopping `MFS100BridgeSvc` triggers automatic fallback to native password tiles.
- [ ] **AC 4.4**: Booting into Windows Safe Mode restores native password logon unconditionally.

---

## 3. Template Protection at Rest & Privileged Broker Architecture (Phase 2 Verified)

The biometric storage layer satisfies all security constraints established in Phase 2:

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                              PRIVILEGED BROKER ARCHITECTURE                            │
├────────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                        │
│  [Standard User / CLI] ────(Named Pipe IPC)────> [MFS100BridgeSvc (SYSTEM, Session-0)] │
│                                                               │                        │
│                                                (DPAPI LocalMachine + User SID Entropy) │
│                                                               ▼                        │
│                                              [%ProgramData%\MFS100CP\templates]        │
│                                              (ACL: SYSTEM & Administrators ONLY)       │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

1. **DPAPI Machine Scope Encryption**: All ISO 19794-2 templates are encrypted via `ProtectedData.Protect` using `DataProtectionScope.LocalMachine`. Plaintext templates never touch disk.
2. **User SID Entropy Context Binding**: `Encoding.UTF8.GetBytes(userSid)` is passed as optional entropy. Ciphertext is cryptographically bound to that exact SID; cross-SID renaming fails decryption with `CryptographicException`.
3. **Strict Directory Security**: `%ProgramData%\MFS100CP\templates` has inheritance disabled and ACLs restricted strictly to `SYSTEM` (`S-1-5-18`) and `BuiltinAdministrators` (`S-1-5-32-544`). Standard users have **zero direct file access**.
4. **Privileged Named Pipe Brokerage**: All enrollment, matching, and deletion operations broker through `\\.\pipe\MFS100Bridge`. The service validates caller tokens using `RunAsClient`. Non-elevated users can only access their own SID (cross-SID requests receive 403 Forbidden).

---

## 4. Current Project State & Verified Acceptance Gates

- **Gate 1 Biometric Verification**: **PASSED (Preliminary)**. Genuine mean score 8,599; cross-finger mean score 6; separation margin 5,511 points; candidate threshold 1,400.
- **Gate 2 Native SDK Isolation**: **PASSED**. 32-bit `MFS100Worker.exe` managed by Session-0 `MFS100BridgeSvc.exe` over named pipe with hard timeouts and auto-restart.
- **Phase 2 Standalone Biometric Suite**: **PASSED (13/13 Automated Tests Verified)**:
  - `AC 2.8` (Broker Architecture): PASSED.
  - `AC 2.9` (DPAPI Encryption at Rest): PASSED.
  - `AC 2.10` (Directory ACL Enforcement): PASSED.
  - `AC 2.11` (SID Context Mismatch Rejection): PASSED.
  - `AC 2.12` (IPC Caller Authorization Check): PASSED.
  - `AC 2.2` (Multi-Scan Consistency): PASSED.
  - `AC 2.3 & 2.7` (Max 2 Fingers & Zero Plaintext Passwords): PASSED.
  - `AC 2.4 & 2.5` (1:1 Verification & Discrimination): PASSED.
  - `AC 2.6` (Cross-User Account Isolation): PASSED.
  - `AC 2.13` (1:N Multi-User Identification Across All Enrolled SIDs): PASSED.
- **Phase 3 Credential Provider & Driver Integration**: **PAUSED** pending formal user review of this specification and VM test harness.
