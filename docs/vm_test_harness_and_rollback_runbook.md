# Windows 11 VM Test Harness & Emergency Rollback Runbook

**Environment**: VMware Workstation Pro 17+  
**Target Guest**: Windows 11 Pro (64-bit) with Virtual TPM 2.0  
**Device**: Mantra MFS100 USB Optical Fingerprint Scanner (VID `0x2C0F`, PID `0x1005`)  
**Strict Safety Rule**: Zero Credential Provider installation or registration on the host operating system.

---

## 1. Overview & Isolation Guarantee

Windows Credential Providers operate directly inside `LogonUI.exe` in the Windows Secure Desktop. A bug, unhandled exception, or thread deadlock in a Credential Provider can cause a crash loop at logon, rendering the system unable to log in interactively.

To guarantee host safety:
1. **Host System**: Runs only the standalone biometric enrollment tool (`MFS100Enroll.exe`) and isolated test runner. Zero registry keys are written to `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers`.
2. **Virtual Machine**: All Credential Provider DLL compilation, registration, and logon experiments occur exclusively inside a dedicated Windows 11 VM in VMware Workstation Pro.
3. **Instant Rollback**: A clean snapshot is created before registering the Credential Provider, enabling 1-click restoration if any failure occurs.

---

## 2. VMware Workstation VM Setup Blueprint

### Step 2.1: VM Creation & Virtual Hardware Configuration
1. Open **VMware Workstation Pro**.
2. Select **File -> New Virtual Machine** -> **Custom (advanced)**.
3. Choose **Hardware Compatibility**: Workstation 17.x.
4. Select your Windows 11 ISO (e.g. Windows 11 23H2 or 24H2 64-bit).
5. Configure VM specs:
   - **Processors**: Minimum 2 cores (4 recommended).
   - **Memory**: Minimum 4096 MB (8192 MB recommended).
   - **Network Connection**: Host-only or NAT.
   - **Disk**: Minimum 64 GB NVMe virtual disk.

### Step 2.2: Enable Virtual TPM 2.0 (vTPM)
Windows 11 and our experimental CNG Platform Crypto Provider require TPM 2.0:
1. In VMware VM Settings, select the **Options** tab.
2. Select **Access Control** -> Click **Encrypt...** -> Enter an encryption password.
3. Switch back to the **Hardware** tab.
4. Click **Add...** -> Select **Trusted Platform Module** -> Click **Finish**.
5. Confirm that **Firmware type** is set to **UEFI** with **Secure Boot enabled**.

### Step 2.3: Configure USB 2.0 Passthrough for Mantra MFS100
1. In VM Settings -> **Hardware** tab -> Select **USB Controller**.
2. Ensure **USB compatibility** is set to **USB 2.0** (or USB 3.1).
3. Check the options:
   - `Automatically connect new USB devices`
   - `Show all USB input devices`
4. Plug the Mantra MFS100 scanner into a physical USB port on the host.
5. In the VMware Workstation menu:
   - Select **VM -> Removable Devices -> Mantra MFS100 -> Connect (Disconnect from host)**.
6. Verify in the host system tray that the device was released by the host and captured by the VM.

### Step 2.4: Enable VMware Shared Folders
To transfer compiled binaries between host and VM without exposing network ports:
1. In VM Settings -> **Options** tab -> Select **Shared Folders**.
2. Set folder sharing to **Always enabled**.
3. Click **Add...** -> Set Host Path to:
   `c:\Users\soumy\Documents\Mantra Device Windows Log-on System`
4. Set Folder Name to `MFS100_Workspace`.
5. Check `Enable this share`.
6. Inside the Windows 11 VM guest, the workspace is automatically accessible at:
   `\\vmware-host\Shared Folders\MFS100_Workspace`

---

## 3. Windows 11 Guest Provisioning

Inside the Windows 11 VM:

1. **Install Windows 11**:
   - Complete standard Windows 11 setup.
   - Create a primary local administrator account:
     - **Username**: `TestAdmin`
     - **Password**: `TestPassword123!`
   - Create a second standard test account:
     - **Username**: `TestUser`
     - **Password**: `UserPassword123!`

2. **Install VMware Tools**:
   - In VMware menu: **VM -> Install VMware Tools...**.
   - Complete installation and reboot to enable Shared Folders and seamless desktop interaction.

3. **Install Mantra MFS100 Driver**:
   - Navigate to `\\vmware-host\Shared Folders\MFS100_Workspace\sdk`.
   - Run the Mantra MFS100 Driver installer (`MFS100Driver_9.0.2.5.exe` or run `InstallDriver.bat`).
   - Open Device Manager inside the VM:
     - Confirm that **MFS100** appears under `Universal Serial Bus devices` or `Biometric devices` without error code 10/28.
   - Run the Mantra test application to confirm raw capture functions properly inside the VM.

4. **Verify Native Windows Recovery Options**:
   - Press `Ctrl + Alt + Del` -> Lock -> Verify that the standard Microsoft Password tile and PIN options work normally.

---

## 4. Snapshot Protocol: Baseline Creation

> [!IMPORTANT]
> You MUST take the baseline snapshot BEFORE registering any Credential Provider in the registry.

### Creating the "Clean_Pre_CP_Baseline" Snapshot
1. Ensure the Mantra MFS100 driver is installed and the device is connected.
2. Ensure both local accounts (`TestAdmin` and `TestUser`) have known passwords.
3. In VMware Workstation menu:
   - Select **VM -> Snapshot -> Take Snapshot...**
   - **Name**: `Clean_Pre_CP_Baseline`
   - **Description**: `Clean Windows 11 with MFS100 drivers, vTPM 2.0, test accounts, zero custom Credential Providers registered.`
4. Click **Take Snapshot**.

---

## 5. Three-Tier Disaster Recovery & Rollback Runbook

If a custom Credential Provider crashes `LogonUI.exe`, causes a black screen, or locks you out:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        DISASTER RECOVERY TIERS                         │
├────────────────────────────────────────────────────────────────────────┤
│ Tier 1: UI Switch       ──> Click "Sign-in options" -> Native Password │
│ Tier 2: Safe Mode       ──> Boot Safe Mode -> Delete CP Registry Key   │
│ Tier 3: Instant Revert  ──> VMware -> Revert to Clean_Pre_CP_Baseline  │
└────────────────────────────────────────────────────────────────────────┘
```

### Tier 1: UI Provider Switch (Non-crashing failure)
If the custom tile fails verification or times out:
1. Look below the username on the Windows logon screen for **"Sign-in options"**.
2. Click the Key icon to switch to the native Microsoft Password Provider.
3. Enter `TestPassword123!` to log in.

### Tier 2: Safe Mode Registry Cleanup (LogonUI crash / freeze)
If `LogonUI.exe` crashes or hangs on startup:
1. On the logon screen, hold the **Shift** key and click **Power -> Restart**.
   *(If logon screen is inaccessible: power-cycle the VM twice during Windows boot to trigger Windows Recovery Environment).*
2. Select **Troubleshoot -> Advanced options -> Startup Settings -> Restart**.
3. Press **4** or **F4** to boot into **Safe Mode**.
4. In Safe Mode:
   - Third-party Credential Providers are **never loaded** by Windows.
   - Log in with `TestAdmin`.
   - Open PowerShell as Administrator and run:
     ```powershell
     # Remove MFS100 Credential Provider registration
     Remove-Item -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\{B5A1D20E-4F23-4A56-B4A9-10052C0F9999}" -Recurse -Force -ErrorAction SilentlyContinue
     ```
5. Reboot normally.

### Tier 3: One-Click Hypervisor Snapshot Revert (Immediate Recovery)
If the VM OS is corrupted or unresponsive:
1. In VMware Workstation menu:
   - Select **VM -> Snapshot -> Revert to Snapshot: Clean_Pre_CP_Baseline**.
2. Confirm the revert.
3. The VM instantly restores to the exact clean pre-experiment state in ~2 seconds.

---

## 6. Phase 3 Test Matrix & Acceptance Protocol

All Phase 3 validation must pass these acceptance gates inside the VM:

| Test ID | Test Scenario | Expected Outcome | Status |
|---|---|---|---|
| **AC 3.1** | MFS100 USB Passthrough | Scanner detected in VM; `MFS100Worker` captures sample (NFIQ <= 3, score >= 60). | Ready |
| **AC 3.2** | Baseline Snapshot Verified | `Clean_Pre_CP_Baseline` created in VMware. | Ready |
| **AC 3.3** | Standalone Local Enrollment | `MFS100Enroll.exe` enrolls 2 fingers inside VM; templates encrypted with DPAPI LocalMachine. | Ready |
| **AC 3.4** | Experimental TPM Vault Sealing | Password sealed with CNG Platform Crypto Provider inside vTPM; zero plaintext credentials on disk. | Ready |
| **AC 3.5** | One-Touch Logon via Biometric Unseal | Enrolled user touches scanner; biometric score >= 1400; TPM unseals credential; `MSV1_0` logs in. | Pending VM Integration |
| **AC 3.6** | Impostor / Cross-Finger Rejection | Unenrolled finger scores < 1400; logon rejected; password buffer wiped with `SecureZeroMemory`. | Pending VM Integration |
| **AC 3.7** | Native Password / PIN Recovery | User clicks "Sign-in options" and successfully logs in with native password/PIN without interference. | Pending VM Integration |
| **AC 3.8** | Out-of-Sync / Password Change Fallback | After password change, tile displays re-authentication prompt and redirects to native password. | Pending VM Integration |
