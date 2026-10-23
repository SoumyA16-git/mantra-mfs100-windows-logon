# Windows 11 VM Test Harness Blueprint & Emergency Rollback Runbook

**Document Version**: 3.0 (Master VM Blueprint & Disaster Recovery Protocol)  
**Date**: 2026-10-23  
**Status**: Formal Architectural Review (Planning & Specification Phase ONLY)  
**Target Hypervisor**: VMware Workstation Pro 17+ (or Oracle VirtualBox 7.x)  
**Guest Operating System**: Windows 11 Pro 64-bit (23H2 / 24H2) with Virtual TPM 2.0  
**Target Device**: Physical Mantra MFS100 Optical Fingerprint Scanner (`USB\VID_2C0F&PID_1005`, Serial `49007331`)  
**Strict Safety Guarantee**: **Zero Credential Provider DLL registration, driver installation, or authentication changes on the host system.**

---

## 1. Safety Mandate & Host Isolation Principles

Windows Credential Providers execute in-process inside `LogonUI.exe` within the Winlogon secure desktop environment (`Session 1` / `Session 0`). An unhandled exception, null-pointer dereference, or infinite loop in a Credential Provider causes `LogonUI.exe` to crash repeatedly, resulting in an immediate **system-wide interactive logon lockout (black screen / boot loop)**.

To provide a 100% mathematical guarantee of host safety:
1. **Zero Host Registration**: No custom DLL will be registered in `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers` on the host machine.
2. **Complete In-VM Confinement**: All Credential Provider compilations, COM registrations, LSA serialization tests, and driver experiments will be performed strictly inside an isolated Windows 11 VM.
3. **Read-Only Host-to-VM Artifact Transfer**: The folder shared between host and guest must be mounted as **Strictly Read-Only** in the guest VM. The VM guest operating system has zero write access to the host project files.
4. **Verified Multi-Tier Recovery**: A verified clean snapshot hierarchy and offline registry recovery runbook must be tested and validated before any Credential Provider is registered.

---

## 2. Virtual Machine Hardware & Hypervisor Blueprint

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                               VM TEST HARNESS ARCHITECTURE                             │
├────────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                        │
│   HOST ENVIRONMENT (Windows 11 Physical Machine)                                       │
│   ┌────────────────────────────────────────────────────────────────────────────────┐   │
│   │ • Source Workspace: c:\Users\soumy\Documents\Mantra Device Windows Log-on      │   │
│   │ • Physical USB Device: Mantra MFS100 (VID: 0x2C0F, PID: 0x1005)                │   │
│   │ • Zero Credential Providers Registered                                         │   │
│   └───────────────────────┬──────────────────────────────────┬─────────────────────┘   │
│                           │ (Physical USB 2.0 Passthrough)   │ (Read-Only Mount)       │
│                           ▼                                  ▼                         │
│   GUEST ENVIRONMENT (VMware Workstation Pro 17+ Windows 11 VM)                         │
│   ┌────────────────────────────────────────────────────────────────────────────────┐   │
│   │ • Windows 11 Pro 64-bit (UEFI + Secure Boot)                                   │   │
│   │ • Virtual TPM 2.0 (vTPM Hardware Enclave for CNG Platform Crypto Provider)     │   │
│   │ • Read-Only Shared Folder: \\vmware-host\Shared Folders\MFS100_ReadOnly        │   │
│   │ • Test Accounts: TestAdmin (Local Admin) & TestUser (Standard Local Account)   │   │
│   │ • Clean Snapshot Baseline: "Clean_Pre_CP_Baseline"                             │   │
│   │ • Experimental Credential Provider & UMDF Driver Isolation                      │   │
│   └────────────────────────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

### 2.1 Virtual Machine Specifications
Configure the virtual machine in VMware Workstation Pro:
- **Guest Operating System**: Windows 11 64-bit (`x64`).
- **Firmware**: UEFI with **Secure Boot enabled**.
- **Processors**: Minimum 2 cores (4 cores recommended).
- **RAM**: Minimum 4096 MB (8192 MB recommended).
- **Hard Disk**: Minimum 64 GB NVMe virtual disk (Split into multiple files or pre-allocated).
- **Network Adapter**: Host-only (or NAT) — isolated from physical enterprise networks.

### 2.2 Enabling Virtual TPM 2.0 (vTPM)
Windows 11 and our experimental CNG Platform Crypto Provider (`MS_PLATFORM_KEY_STORAGE_PROVIDER`) require a hardware-backed TPM 2.0:
1. In VMware Workstation, open **VM -> Settings -> Options tab -> Access Control**.
2. Click **Encrypt...** and set an encryption password for the VM.
3. Switch to the **Hardware tab**, click **Add...**, select **Trusted Platform Module**, and click **Finish**.
4. Confirm that the TPM status displays as **Present** (TPM 2.0).

### 2.3 Physical USB 2.0 Passthrough Configuration
The Mantra MFS100 is an optical USB 2.0 device that requires direct access to USB bulk endpoints:
1. In VM Settings -> **Hardware tab** -> Select **USB Controller**.
2. Set **USB compatibility** to **USB 2.0** (or USB 3.1).
3. Enable the checkboxes:
   - `Automatically connect new USB devices`
   - `Show all USB input devices`
4. Plug the physical Mantra MFS100 scanner into a host USB port.
5. In VMware Workstation top menu:
   - Click **VM -> Removable Devices -> Mantra MFS100 -> Connect (Disconnect from host)**.
6. Verify in the host system notification area that the scanner disconnected from the host and connected directly to the VM.

### 2.4 Read-Only Host-to-VM Shared Folder Setup
To prevent any accidental corruption, malware, or unintended modification of the host codebase by processes running inside the VM:
1. In VM Settings -> **Options tab** -> Select **Shared Folders**.
2. Select **Always enabled**.
3. Click **Add...** -> Set Host Path:
   `c:\Users\soumy\Documents\Mantra Device Windows Log-on System`
4. Set Folder Name: `MFS100_ReadOnly`.
5. **CRITICAL**: Check the box: **`Read-only`**.
6. Inside the Windows 11 VM guest, navigate to:
   `\\vmware-host\Shared Folders\MFS100_ReadOnly`
7. Attempt to create a dummy text file inside the shared folder from within the VM to verify that Windows returns **"Access is denied" (Read-Only Enforcement Verified)**.

---

## 3. Guest Operating System Provisioning & Baseline Setup

Inside the Windows 11 VM:

### Step 3.1: Windows 11 Guest Account Setup
1. Complete standard Windows 11 initial setup (OOBE).
2. Create two local SAM accounts with known, documented credentials:
   - **Primary Local Administrator**:
     - Username: `TestAdmin`
     - Password: `AdminPassword123!`
     - Role: Member of `BUILTIN\Administrators`.
   - **Standard Local User Account**:
     - Username: `TestUser`
     - Password: `UserPassword123!`
     - Role: Standard User (`BUILTIN\Users`).
3. Verify that both accounts can log in using their native passwords via the standard Microsoft Password Provider.

### Step 3.2: Install VMware Tools
1. In VMware menu: **VM -> Install VMware Tools...**.
2. Run `setup64.exe` inside the VM, reboot, and verify that display resolution resizing and shared folders function smoothly.

### Step 3.3: Install Official Mantra MFS100 Drivers Inside VM
1. From the read-only shared folder, copy the installer to the VM desktop:
   `Copy-Item "\\vmware-host\Shared Folders\MFS100_ReadOnly\sdk\MFS100Driver_9.0.2.5.exe" "C:\Users\TestAdmin\Desktop\"`
2. Run the driver installer as Administrator.
3. Open **Device Manager** inside the VM:
   - Verify that **MFS100** appears under `Universal Serial Bus devices` (or `Biometric devices`) with status: *"This device is working properly"*.
4. Run the Mantra Test Application (`C:\Program Files\Mantra\MFS100\Driver\MFS100Test\MFS100Test.exe`):
   - Place a finger on the physical scanner.
   - Confirm raw image capture, quality score >= 60, and NFIQ <= 3.

---

## 4. Snapshot Hierarchy Protocol

> [!IMPORTANT]
> The snapshot hierarchy must be established and verified BEFORE any Credential Provider registry key is created.

```
┌────────────────────────────────────────────────────────────────────────┐
│                        SNAPSHOT HIERARCHY TREE                         │
├────────────────────────────────────────────────────────────────────────┤
│ Snapshot 0: "Clean_OS_Base"                                            │
│   └── Clean Windows 11 OS + VMware Tools + vTPM 2.0                    │
│                                                                        │
│ Snapshot 1: "Clean_Pre_CP_Baseline"  <=== [MASTER ROLLBACK TARGET]     │
│   └── Mantra Driver v9.0.2.5 + USB Passthrough + Test Accounts         │
│       ZERO Credential Providers Registered                             │
│                                                                        │
│ Snapshot 2: "Phase2_Enrolled_Base"                                     │
│   └── MFS100BridgeSvc Installed + TestUser Enrolled (DPAPI Templates)  │
│       ZERO Credential Providers Registered                             │
│                                                                        │
│ Snapshot 3: "Phase3_CP_Experimental"                                   │
│   └── Custom Credential Provider Registered in HKLM                    │
└────────────────────────────────────────────────────────────────────────┘
```

### 4.1 Creating the Master Rollback Baseline: `Clean_Pre_CP_Baseline`
1. Close all open applications inside the VM.
2. In VMware Workstation menu:
   - Select **VM -> Snapshot -> Take Snapshot...**
   - **Name**: `Clean_Pre_CP_Baseline`
   - **Description**: `Verified Windows 11 VM with Mantra MFS100 USB passthrough, vTPM 2.0, driver v9.0.2.5 functional, TestAdmin and TestUser created. Zero custom Credential Providers registered.`
3. Click **Take Snapshot**.

---

## 5. Pre-Experiment Recovery Testing Protocol

Before registering any Credential Provider in `HKLM`, we must deliberately execute and validate the recovery mechanisms:

### Recovery Test 1: Snapshot Restore Verification
1. Create a dummy file on the VM desktop: `C:\Users\TestAdmin\Desktop\test_marker.txt`.
2. In VMware menu, select **VM -> Snapshot -> Revert to Snapshot: Clean_Pre_CP_Baseline**.
3. After the ~2 second revert, verify that `test_marker.txt` is gone. This confirms snapshot restoration functions flawlessly.

### Recovery Test 2: Safe Mode Access Verification
1. In the VM, hold the **Shift** key and click **Start -> Power -> Restart**.
2. Navigate to **Troubleshoot -> Advanced options -> Startup Settings -> Restart**.
3. Press **4** to boot into Safe Mode.
4. Verify that you can log in to `TestAdmin` in Safe Mode.
5. Reboot back to normal mode.

---

## 6. Four-Tier Emergency Rollback Runbook

If an experimental Credential Provider crashes `LogonUI.exe`, causes a black screen, freezes the lock screen, or blocks password entry:

```
┌────────────────────────────────────────────────────────────────────────┐
│                       EMERGENCY RECOVERY RUNBOOK                       │
├────────────────────────────────────────────────────────────────────────┤
│ Tier 1: UI Switch       ──> Click "Sign-in options" -> Native Password │
│ Tier 2: Safe Mode       ──> Boot Safe Mode -> Delete CP Registry Key   │
│ Tier 3: WinRE Offline   ──> Boot WinRE Command Prompt -> Edit Hive     │
│ Tier 4: Instant Revert  ──> VMware -> Revert to Clean_Pre_CP_Baseline  │
└────────────────────────────────────────────────────────────────────────┘
```

### Tier 1: UI Provider Switch (Non-crashing failure)
- **When to Use**: The custom tile displays an error or fails biometric matching, but `LogonUI.exe` is still responsive.
- **Procedure**:
  1. On the logon screen, look below the username for **"Sign-in options"**.
  2. Click the standard Key icon to select the native Microsoft Password Provider.
  3. Enter `AdminPassword123!` or `UserPassword123!` to log in.

### Tier 2: Safe Mode Registry Purge (LogonUI crash / freeze)
- **When to Use**: `LogonUI.exe` crashes repeatedly, displays a blinking black screen, or locks up on startup.
- **Procedure**:
  1. Hold **Shift** while clicking **Power -> Restart** on the logon screen (or force-reset the VM twice during Windows boot to trigger WinRE).
  2. Select **Troubleshoot -> Advanced options -> Startup Settings -> Restart**.
  3. Press **4** (or **F4**) to enter **Safe Mode**.
  4. In Safe Mode:
     - Third-party Credential Providers are **never loaded** by Windows.
     - Log in with `TestAdmin`.
     - Open PowerShell as Administrator and run the emergency unregistration script:
       ```powershell
       # Remove custom MFS100 Credential Provider registration
       $cpGuid = "{B5A1D20E-4F23-4A56-B4A9-10052C0F9999}"
       Remove-Item -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\$cpGuid" -Recurse -Force -ErrorAction SilentlyContinue
       Remove-Item -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Provider Filters\$cpGuid" -Recurse -Force -ErrorAction SilentlyContinue
       Write-Host "[SUCCESS] MFS100 Credential Provider removed from registry." -ForegroundColor Green
       ```
  5. Restart the VM normally.

### Tier 3: WinRE Offline Registry Hive Repair (If Safe Mode fails)
- **When to Use**: Safe Mode is inaccessible, but hypervisor snapshot revert is not desired (preserving crash logs).
- **Procedure**:
  1. Boot from the Windows 11 ISO or interrupt boot twice to enter **Windows Recovery Environment (WinRE)**.
  2. Select **Troubleshoot -> Advanced Options -> Command Prompt**.
  3. In the Command Prompt, identify the Windows drive letter (typically `D:` or `C:`):
     ```cmd
     dir C:\Windows
     dir D:\Windows
     ```
  4. Load the offline `SOFTWARE` registry hive:
     ```cmd
     reg load HKLM\OFFLINE_SOFTWARE D:\Windows\System32\config\SOFTWARE
     ```
  5. Delete the custom Credential Provider registry key:
     ```cmd
     reg delete "HKLM\OFFLINE_SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{B5A1D20E-4F23-4A56-B4A9-10052C0F9999}" /f
     reg delete "HKLM\OFFLINE_SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Provider Filters\{B5A1D20E-4F23-4A56-B4A9-10052C0F9999}" /f
     ```
  6. Unload the hive:
     ```cmd
     reg unload HKLM\OFFLINE_SOFTWARE
     ```
  7. Type `exit` and click **Continue to Windows 11**.

### Tier 4: Instant Hypervisor Snapshot Revert (Nuclear Option)
- **When to Use**: Any unrecoverable system corruption, blue screen (BSOD), or infinite hang.
- **Procedure**:
  1. In VMware Workstation top menu:
     - Select **VM -> Snapshot -> Revert to Snapshot: Clean_Pre_CP_Baseline**.
  2. Click **Revert**.
  3. In approximately 2 seconds, the virtual machine is completely restored to the clean baseline.

---

## 7. Phase 3 Test Matrix & Explicit Acceptance Criteria

Inside the Windows 11 VM, the following criteria govern Phase 3 progression:

| Test ID | Test Scenario | Acceptance Criteria | Status |
|---|---|---|---|
| **AC 3.1** | **USB Passthrough Verification** | MFS100 is recognized in VM Device Manager; `MFS100Worker.exe` captures samples with quality >= 60, NFIQ <= 3. | Ready for Test |
| **AC 3.2** | **Read-Only Share Enforcement** | Guest VM attempts to write to `\\vmware-host\Shared Folders\MFS100_ReadOnly`; write is rejected with `Access is denied`. | Ready for Test |
| **AC 3.3** | **Baseline Snapshot Creation** | `Clean_Pre_CP_Baseline` is created and verified via pre-experiment revert test. | Ready for Test |
| **AC 3.4** | **In-VM Standalone Enrollment** | `MFS100Enroll.exe` enrolls 2 fingers inside VM; templates encrypted with DPAPI LocalMachine + User SID entropy. | Ready for Test |
| **AC 3.5** | **TPM 2.0 Hardware Vault Sealing** | Password sealed via CNG Platform Crypto Provider (`MS_PLATFORM_KEY_STORAGE_PROVIDER`) inside vTPM; zero plaintext credentials on disk. | Pending In-VM Development |
| **AC 3.6** | **Cold-Boot One-Touch Logon** | User touches scanner at logon screen; biometric match score >= 1400; TPM unseals password; `MSV1_0` logs in to desktop. | Pending In-VM Development |
| **AC 3.7** | **Impostor Finger Rejection** | Unregistered finger scores < 1400; unseal is refused; session remains locked; memory is scrubbed via `SecureZeroMemory`. | Pending In-VM Development |
| **AC 3.8** | **Native Recovery Coexistence** | Native Microsoft Password and PIN tiles remain fully functional via "Sign-in options". | Pending In-VM Development |
| **AC 3.9** | **Dynamic 2FA Filter Failsafe** | When scanner is unplugged, dynamic filter unhides native password tile within 2 seconds. | Pending In-VM Development |
| **AC 3.10** | **Emergency Safe Mode Recovery** | Booting to Safe Mode successfully bypasses custom provider and permits registry unregistration. | Pending In-VM Development |

---

## 8. Summary of Unresolved Blockers Before Implementation

Before recommending any Credential Provider code execution or driver installation inside the VM, the following technical blockers must be explicitly acknowledged:

1. **Password Desynchronization Blocker**: If the user changes their password outside the custom tile, the TPM 2.0 hardware vault becomes desynchronized, requiring fallback to native password recovery.
2. **WBF 64-bit Architecture Blocker**: Native Windows Hello fingerprint integration is blocked by Mantra's 32-bit legacy native binaries (`MFS100Dll.dll`, `iengine_ansi_iso.dll`). Overcoming this requires an experimental 64-bit UMDF 2.0 IPC bridge running in Test-Signing mode.
3. **Catastrophic Lockout Blocker**: Unconditional 2FA enforcement via `ICredentialProviderFilter` presents a high risk of permanent lockout if hardware fails. It must strictly utilize a dynamic hardware watchdog failsafe.
