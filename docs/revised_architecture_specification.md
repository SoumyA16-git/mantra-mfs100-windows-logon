# Revised Architecture Specification: Mantra MFS100 Windows 11 Biometric System

**Document Version**: 2.0 (Post-Grill Architectural Alignment)  
**Date**: 2026-10-23  
**Status**: Pending User Review  
**Target Environments**:  
- **Host System**: Standalone Biometric Suite & Process-Isolated Service ONLY (**Zero Credential Provider registration**).  
- **Test VM**: Windows 11 (64-bit) in VMware / VirtualBox with MFS100 USB Passthrough and rollback snapshots.

---

## 1. Executive Summary & Resolution of Core Contradictions

This document formalizes the architectural specifications, threat model, account scoping, and acceptance criteria agreed upon during the technical review.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CORE GOVERNING PRINCIPLES                         │
├─────────────────────────────────────────────────────────────────────────────┤
│ 1. ZERO PERSISTENT PASSWORDS: No passwords, PINs, or hashes on disk.        │
│ 2. ZERO RAM CACHING: No reusable credentials held in RAM across sessions.   │
│ 3. HOST SAFETY: No Credential Provider DLL will be installed on the host.   │
│ 4. UNHINDERED RECOVERY: Native Password/PIN providers remain always active. │
│ 5. PRIVILEGED TEMPLATE BROKER: Standard users have zero direct file access. │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Detailed Resolution of Requirements & Contradictions

### 2.1 Requirement 1 & 2: Optional Biometric Tile vs. Mandatory System-Wide 2FA

#### The Contradiction:
A biometric solution cannot simultaneously claim to **enforce mandatory system-wide two-factor authentication** while keeping **standard Windows Password and PIN sign-in tiles available for emergency recovery**.

#### The Technical Reality:
- On Windows 11 `LogonUI`, all registered Credential Providers are presented side-by-side under "Sign-in options".
- If the default Microsoft Password Provider (`{60b78e88-ead8-445c-9cfd-0b87f74ea6cd}`) and PIN Provider (`{d68ddaee-cdb2-41f0-8152-7634fbf6c26f}`) are kept visible, **any user can simply click the Password or PIN tile and bypass the biometric scanner completely**.
- Enforcing mandatory system-wide 2FA requires implementing an `ICredentialProviderFilter` that actively intercepts and hides Microsoft's default providers. However, hiding default providers eliminates the fallback mechanism if the USB scanner is disconnected, damaged, or unpowered.

#### The Architectural Resolution:
- The MFS100 Credential Provider is explicitly classified and documented as an **Optional Biometric Tile with Native Fallback (User-Selected Biometric Gate)**.
- It provides a convenient, biometric-verified sign-in tile for enrolled users while guaranteeing 100% recovery via native Windows tiles.
- **We do NOT claim mandatory system-wide 2FA is achieved** because native providers can bypass it by design.

---

### 2.2 Requirement 3: Account Scoping & Authentication Packages

#### The Technical Reality:
Windows 11 utilizes distinct authentication packages for different account types:

1. **Standalone Local Accounts (`MSV1_0`)**:
   - Uses the documented `MSV1_0_INTERACTIVE_LOGON` structure (`MessageType = MsV1_0InteractiveLogon`).
   - Requires the user's **actual local account password string**.
   - Computes the NTLM hash in LSA memory to validate against the local SAM database.
   - **Windows Hello PIN is NOT a password**: A Windows Hello PIN is an NGC container credential stored in the TPM and evaluated exclusively by Microsoft's `NgcCredProv.dll`. A Hello PIN cannot be serialized to `MSV1_0`; doing so results in `STATUS_LOGON_FAILURE` (`0xC000006D` / `0xC000006A`). Users must enter their actual local account password in the biometric tile.

2. **Personal Microsoft-Linked Accounts (`CloudAP`)**:
   - Authenticated through the `CloudAP` plugin (`CloudAP\MicrosoftAccount`).
   - Requires live OAuth / Primary Refresh Tokens (PRT) and can trigger interactive modern authentication or MFA prompts that a third-party Credential Provider cannot render.
   - Passing passwords to CloudAP from custom tiles is fragile across Windows 11 cumulative updates.

#### The Architectural Resolution:
- **Phase 3A (Immediate Scope)**: Standalone Local Accounts (`.\username` + real local password) using `MSV1_0_INTERACTIVE_LOGON`. This is documented, stable, and completely offline.
- **Phase 3B (Future Extension)**: Personal Microsoft-linked accounts evaluated as an isolated experimental branch only after Phase 3A local accounts pass all VM tests.

---

### 2.3 Requirement 4: Memory Clearing & Zero-Secret Guarantee

#### The Threat Model:
- Plaintext passwords must never touch disk storage, the Windows registry, event logs, or unencrypted persistent files.
- Reusable credentials must not be held in memory after the user session is authenticated.

#### The Architectural Resolution:
1. When the user enters their password in the VM Credential Provider tile, it is held in a pinned memory buffer.
2. The tile communicates with `MFS100BridgeSvc` via named pipe only to request biometric verification (`cmd: "match_user"`, passing the target SID).
3. The password string **is never transmitted over the named pipe** to the Bridge Service.
4. If biometric verification succeeds (`score >= 1,400`):
   - The tile packages the password into `CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION`.
   - It yields the serialization to `LogonUI` via `GetSerialization`.
   - It immediately wipes the password buffer with zeros using native `SecureZeroMemory` / `RtlSecureZeroMemory`.
5. If biometric verification fails or times out:
   - The password buffer is immediately wiped with zeros and discarded.
   - `LsaLogonUser` is never invoked.

---

### 2.4 Requirement 5: Biometric Template Protection at Rest & Privileged Brokerage

#### The Security Model:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        PRIVILEGED BROKER PATTERN                       │
├────────────────────────────────────────────────────────────────────────┤
│                                                                        │
│  [Standard User / CLI] ──(Named Pipe IPC)──> [MFS100BridgeSvc (SYSTEM)]│
│                                                     │                  │
│                                      (DPAPI LocalMachine + SID Entropy)│
│                                                     ▼                  │
│                                     [%ProgramData%\MFS100CP\templates] │
│                                     (ACL: SYSTEM & Admin ONLY)         │
└────────────────────────────────────────────────────────────────────────┘
```

1. **Encryption Mechanism**:
   - Biometric ISO 19794-2 templates are encrypted at rest using **DPAPI Machine Scope** (`ProtectedData.Protect` with `DataProtectionScope.LocalMachine`).
   - This enables the pre-logon SYSTEM service to decrypt templates when no user profile is loaded.
2. **Context Binding via User SID Entropy**:
   - The target user's Windows SID string (`Encoding.UTF8.GetBytes(userSid)`) is passed as optional entropy to DPAPI.
   - This cryptographically binds the encrypted ciphertext to that specific user SID, ensuring that a template file copied or renamed to another user's SID will fail decryption (`CryptographicException`).
   - *Note*: As required, the SID entropy is treated as a context-binding parameter, not as an authorization secret.
3. **Strict Directory ACLs (No Standard User Access)**:
   - Directory: `%ProgramData%\MFS100CP\templates\`
   - Permissions:
     - `NT AUTHORITY\SYSTEM`: FullControl
     - `BUILTIN\Administrators`: FullControl
     - `Authenticated Users / Standard Users`: **NO ACCESS (Denied)**. Standard users cannot read encrypted template files directly.
4. **Privileged Named Pipe Brokerage & Authorization**:
   - All template reads, writes, encryptions, and decryptions route strictly through the Session-0 service `MFS100BridgeSvc`.
   - The client application (`MFS100Enroll.exe`) **never accesses template files directly**.
   - Upon connection, `MFS100BridgeSvc` inspects the client using `ImpersonateNamedPipeClient` to retrieve the caller's Windows token.
   - The service enforces that a caller can only enroll or view templates for their **own SID**, unless the caller possesses administrative elevation (`TokenElevationTypeFull` or member of `BuiltinAdministratorsSid`).
5. **Threat Model Disclosure**:
   - Local Administrators and `NT AUTHORITY\SYSTEM` remain trusted principals on the Windows operating system. No claim of protection against a compromised Administrator or kernel driver is made.

---

## 3. Supported vs. Unsupported Scenarios Matrix

| Scenario | Supported? | Mechanism & Requirements |
|---|---|---|
| **Local Account Sign-in (Cold Boot / Restart)** | **YES** | User selects MFS100 tile, enters local password, touches scanner (`score >= 1400`). |
| **Local Account Unlock (Win+L)** | **YES** | User selects MFS100 tile, enters local password, touches scanner (`score >= 1400`). |
| **Recovery via Native Password / PIN** | **YES** | User clicks "Sign-in options" and picks standard Microsoft Password or PIN tile. |
| **Multi-Finger Enrollment per SID** | **YES** | Up to 2 fingers per account, 3-scan consistency verified, encrypted via DPAPI. |
| **Cold Boot 1-Touch Passwordless Login** | **NO** | Technically impossible on Windows 11 without disk password storage or Active Directory PKINIT. |
| **Windows Hello PIN as Password** | **NO** | Hello PIN cannot be serialized to MSV1_0. Actual local account password is required. |
| **Native Windows Hello Integration** | **NO** | Mantra MFS100 lacks a 64-bit WBF/WBDI driver. |
| **Mandatory System-Wide 2FA** | **NO** | Native password tiles remain enabled to guarantee recovery. |
| **Personal Microsoft Accounts (CloudAP)** | **DEFERRED** | Deferred to Phase 3B; requires separate validation of CloudAP token flows. |

---

## 4. Windows 11 VM Test Harness & Disaster Recovery Plan

All Credential Provider experiments are strictly restricted to an isolated **Windows 11 VM** running under **VMware Workstation Pro** or **VirtualBox**.

### 4.1 VM Prerequisites
1. **Virtualization Software**: VMware Workstation Pro 17+ or VirtualBox 7+ with Extension Pack.
2. **Guest OS**: Windows 11 Pro 64-bit (Version 23H2 or 24H2).
3. **USB Passthrough**:
   - In VM settings, add USB Controller (USB 2.0 / 3.0 enabled).
   - Connect Mantra MFS100 hardware (`USB\VID_2C0F&PID_1005`).
   - In VMware/VirtualBox menu: `Removable Devices -> Mantra MFS100 -> Connect (Disconnect from host)`.
4. **Guest Software**:
   - Install official Mantra MFS100 Driver v9.0.2.5 inside the VM.
   - Create a local test user (e.g. `TestUser`, password `TestPassword123!`).

### 4.2 Snapshot & Rollback Protocol
1. **Baseline Snapshot ("Clean_Pre_CP")**:
   - Taken after driver installation, device verification, and `MFS100BridgeSvc` installation, but **BEFORE** registering any Credential Provider CLSID in `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers`.
2. **Post-Enrollment Snapshot ("Enrolled_Pre_Logon")**:
   - Taken after enrolling `TestUser` fingers and verifying DPAPI template encryption.

### 4.3 Three-Tier Recovery Procedure
If a Credential Provider DLL hangs, crashes `LogonUI.exe`, or causes a boot loop inside the VM:

1. **Tier 1 (UI Fallback)**:
   - Click "Sign-in options" (icon below password box) and click the standard Key icon to log in with the native Windows password.
2. **Tier 2 (Windows Safe Mode)**:
   - Third-party Credential Providers are **not loaded by Windows Safe Mode**.
   - Boot into Safe Mode (hold Shift while clicking Restart -> Advanced Options -> Startup Settings -> Safe Mode).
   - Delete the registry key: `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{MFS100-CLSID}`.
3. **Tier 3 (Hypervisor Snapshot Rollback)**:
   - Power off the VM and restore the **"Clean_Pre_CP"** snapshot in 1 click.

---

## 5. Phase 2 & Phase 3 Acceptance Criteria

### Phase 2: Standalone Biometric Suite with Encrypted Brokerage
- [x] **AC 2.8 (Broker Architecture)**: All template read, write, and delete operations execute exclusively within `MFS100BridgeSvc` over named pipe `\\.\pipe\MFS100Bridge`. `MFS100Enroll.exe` performs zero direct file I/O. **[PASSED - Verified in Test 1-2 & 8-9]**
- [x] **AC 2.9 (DPAPI Encryption at Rest)**: Template files on disk are verified to be encrypted with `DataProtectionScope.LocalMachine` + User SID entropy. Plaintext ISO templates never appear in `.dat` files (magic != `MFST`). **[PASSED - Verified in Test 4]**
- [x] **AC 2.10 (Directory ACL Enforcement)**: `%ProgramData%\MFS100CP\templates` ACLs permit only `SYSTEM` and `Administrators` (with inheritance protected). Standard user direct file access is denied. **[PASSED - Verified in Test 7]**
- [x] **AC 2.11 (SID Context Mismatch)**: Attempting to decrypt a template file with a mismatched user SID throws `CryptographicException` and is cryptographically rejected. **[PASSED - Verified in Test 5]**
- [x] **AC 2.12 (IPC Authorization)**: A non-elevated client process cannot enroll or modify templates for any SID other than its own token SID (rejected with 403 Forbidden). **[PASSED - Verified in Test 6]**
- [x] **AC 2.2 (Multi-Scan Consistency)**: Enrolls fingers only when scans match consistently (`score >= 1,400`); rejects inconsistent captures. **[PASSED - Verified in Test 10]**
- [x] **AC 2.3 & AC 2.7 (Limits & Zero Plaintext Passwords)**: Enforces maximum 2 fingers per account SID; zero passwords or hashes stored in templates. **[PASSED - Verified in Test 1-4]**
- [x] **AC 2.4 & AC 2.5 (1:1 Verification & Discrimination)**: Genuine fingers score >= 1,400 (MATCH); unenrolled fingers score < 1,400 (DENIED). **[PASSED - Verified in Test 11]**
- [x] **AC 2.6 (Cross-User Account Isolation)**: Zero false accepts between separate enrolled account SIDs. **[PASSED - Verified in Test 12]**
- [x] **AC 2.13 (1:N Multi-User Identification)**: Identifies correct user accounts across all enrolled SIDs without false crossover. **[PASSED - Verified in Test 13]**

### Phase 3: VM Test Harness & Credential Tile Integration
- [ ] **AC 3.1 (VM Environment Validation)**: Windows 11 VM in VMware Workstation Pro boots with MFS100 USB passthrough and vTPM 2.0; `MFS100Worker` captures samples inside the VM with quality >= 60.
- [ ] **AC 3.2 (Snapshot Baseline)**: "Clean_Pre_CP_Baseline" snapshot created in VMware Workstation and verified before CP registration.
- [ ] **AC 3.3 (Experimental TPM Vault Sealing)**: Credential sealed via Windows CNG Platform Crypto Provider (`MS_PLATFORM_KEY_STORAGE_PROVIDER`) inside vTPM 2.0; zero plaintext passwords on disk or registry.
- [ ] **AC 3.4 (One-Touch Logon via Biometric Unseal)**: In the VM, user touches scanner; biometric score >= 1,400; TPM unseals credential in Session-0 memory; `MSV1_0` logs in.
- [ ] **AC 3.5 (Biometric Rejection & Scrubbing)**: In the VM, impostor finger scores < 1,400; unseal is refused; memory buffers are sanitized with `SecureZeroMemory`.
- [ ] **AC 3.6 (Native Tile Recovery Unhindered)**: In the VM, logging on via standard Windows Password and PIN tiles succeeds without interference.
- [ ] **AC 3.7 (Out-of-Sync Password Change Fallback)**: If password changed via native Windows, tile displays recovery instruction and redirects to standard password.
