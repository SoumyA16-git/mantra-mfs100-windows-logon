# Mantra MFS100 Native Windows Hello Biometric System

[![Windows 11](https://img.shields.io/badge/Windows%2011-Ready-0078D6?logo=windows&logoColor=white)](https://microsoft.com)
[![Windows Hello](https://img.shields.io/badge/Windows%20Hello-Verified-success?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/windows-hello)
[![Architecture](https://img.shields.io/badge/Architecture-x64%20WBF%20%2B%20x86%20Worker-blueviolet)](docs/wbf_native_windows_hello_blueprint.md)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Native Windows Hello Fingerprint Logon and Enrollment solution for the **Mantra MFS100 Optical USB Fingerprint Scanner** (`USB\VID_2C0F&PID_1005`). 

This project integrates the physical Mantra MFS100 directly into the **Windows Biometric Framework (WBF)**, enabling native biometric sign-in directly on the Windows 11 / Windows 10 Lock Screen (`Win + L`) and Windows Settings enrollment without requiring Aadhaar RD Service or third-party login screens.

---

## Key Highlights

- **Native Windows Hello Integration**: Enrolls seamlessly inside **Windows Settings > Accounts > Sign-in options > Fingerprint recognition (Windows Hello)**.
- **Lock Screen Unlock**: Unlocks the PC immediately upon touching the physical sensor on the lock screen (`Win + L`).
- **Overcoming 32-bit SDK Legacy Barriers**: Seamlessly bridges 64-bit Windows Biometric Service (`WbioSrvc`) to the manufacturer's proprietary 32-bit (`x86`) binaries (`MFS100Dll.dll`, `iengine_ansi_iso.dll`) via a low-latency, secure local IPC broker.
- **Reverse-Engineered Storage Compatibility**: Implements strict Windows Biometric Framework database cursor specifications (`WINBIO_IDENTITY_WILDCARD = 0x25066282`) for reliable template matching and retrieval.
- **Zero Plaintext Credentials**: Never saves user passwords or PINs to disk. Windows Hello cryptographic payloads are bound to the biometric template within the operating system's native encrypted `WinBioDatabase`.

---

## Complete End-User Setup Guide (Step-by-Step)

Follow these exact 6 steps in chronological order on any Windows 11 / 10 PC to get native Windows Hello fingerprint logon working:

---

### Step 1: Check and Disable Secure Boot in BIOS

> [!IMPORTANT]
> **Why this is required:** Microsoft UEFI security policies strictly block `testsigning` when Secure Boot is active. To load our self-signed driver certificate for free personal use, Secure Boot must be turned off. (For production deployment with a commercial code-signing certificate, Secure Boot can remain 100% ON).

1. **Check your current Secure Boot state:**
   - Press **Win + R**, type `msinfo32`, and hit Enter.
   - In *System Summary*, look for **Secure Boot State**.
   - If it says **Off**, you are good! Skip directly to [Step 2](#step-2-enable-test-signing-mode).
   - If it says **On**, disable it in BIOS:
2. **How to disable Secure Boot:**
   - Restart your computer and press your BIOS key during boot:
     * **Dell**: F2 or F12
     * **HP**: F10 or Esc
     * **Lenovo**: F2 or Fn+F2
     * **Asus / Acer / MSI / Others**: Del or F2
     * *(Alternative Windows route)*: Go to **Settings > System > Recovery > Advanced startup (Restart now) > Troubleshoot > Advanced options > UEFI Firmware Settings**.
   - In BIOS setup, open the **Security** or **Boot** menu.
   - Locate **Secure Boot** and set it to **Disabled**.
   - Press **F10** to Save & Exit, then boot back into Windows.

---

### Step 2: Enable Test Signing Mode

1. Open the Start menu, type `cmd`.
2. Right-click **Command Prompt** and select **Run as administrator**.
3. Run the following command:
   ```cmd
   bcdedit /set testsigning on
   ```
4. Confirm you receive: `The operation completed successfully.`
5. **Restart your computer.**
   *(After restarting, a subtle "Test Mode" watermark may appear in the bottom-right corner of the desktop. This confirms Test Signing is active).*

---

### Step 3: Install Official Mantra Driver & Connect Device

1. Download and install the official **Mantra MFS100 Driver v9.0.2.5** (installed to `C:\Program Files\Mantra\MFS100\Driver`).
   *(Note: Do NOT install or run Aadhaar RD Service; RD Service is not required for Windows Hello logon).*
2. Plug your **Mantra MFS100 scanner** into any USB port.
3. Confirm the scanner's red optical LED blinks once upon connection.

---

### Step 4: Ensure Windows Hello PIN is Set Up

Windows requires a PIN to be configured on your account before permitting biometric fingerprint setup:

1. Open **Settings** (`Win + I`) > **Accounts** > **Sign-in options**.
2. Look under **PIN (Windows Hello)**:
   - If you already have a PIN set up, proceed directly to [Step 5](#step-5-download--register-wbf-adapters).
   - If not, click **Set up**, follow the prompts, and choose a PIN.

---

### Step 5: Download & Register WBF Adapters

1. Download the latest release package: [`Mantra_MFS100_Windows_Hello_v1.0.0.zip`](dist/Mantra_MFS100_Windows_Hello_v1.0.0.zip).
2. Extract the ZIP to a permanent folder on your PC (e.g., `C:\MantraWBF` or in your Documents folder). Do not delete this folder after setup.
3. In the extracted folder, right-click **`7_Register_WBF_Windows_Hello.bat`** and select **Run as administrator**.
4. The automated installer will:
   - Install the driver certificate into `Trusted Root Certification Authorities` and `Trusted Publishers`.
   - Install and launch the background **MFS100 Bridge Service** (`MFS100BridgeSvc.exe`).
   - Copy 64-bit WBF Engine & Sensor adapters to `C:\Windows\System32\WinBioPlugIns`.
   - Register the biometric database configuration in the Windows Registry (`HKLM\SYSTEM\CurrentControlSet\Services\WbioSrvc\Databases`).
   - Restart the **Windows Biometric Service** (`WbioSrvc`).
5. Confirm the console displays:
   ```text
   ==================================================
   [SUCCESS] Mantra MFS100 registered with Windows Biometric Framework!
   ==================================================
   ```

---

### Step 6: Enroll Fingerprint in Windows Settings & Test Logon

1. Open **Settings** (`Win + I`) > **Accounts** > **Sign-in options**.
2. Under **Fingerprint recognition (Windows Hello)**, click **Set up** > **Get started**.
3. Enter your Windows Hello PIN when prompted.
4. When the Mantra MFS100 red optical sensor lights up:
   - Place your finger flat on the scanner.
   - When Windows prompts, lift and touch again from slightly different angles.
5. Click **Close** once enrollment completes.
6. Now lock your PC: press **Win + L**.
7. Touch the Mantra scanner with your registered finger — **Windows will instantly recognize your fingerprint and unlock!**

---

## System Architecture

```
+---------------------------------------------------------------------------------------+
|                                    WINDOWS 11 OS                                      |
|                                                                                       |
|  +--------------------------------+       +----------------------------------------+  |
|  | Windows Hello / Lock Screen UI |       | Windows Settings (Biometric Enrollment)|  |
|  +--------------------------------+       +----------------------------------------+  |
|                  |                                             |                      |
|                  +----------------------+----------------------+                      |
|                                         |                                             |
|                                         v                                             |
|                   +--------------------------------------------+                      |
|                   | Windows Biometric Service (WbioSrvc - x64) |                      |
|                   |  C:\Windows\System32\svchost.exe -k Wbio...|                      |
|                   +--------------------------------------------+                      |
|                         |                                |                            |
|                         v                                v                            |
|             +-----------------------+        +-----------------------+                |
|             | MFS100SensorAdapter   |        | MFS100EngineAdapter   |                |
|             | (x64 /MT Native DLL)  |        | (x64 /MT Native DLL)  |                |
|             +-----------------------+        +-----------------------+                |
|                         |                                |                            |
|                         +----------------+---------------+                            |
|                                          |                                            |
|                        Named Pipe IPC    | \\.\pipe\MFS100Bridge                      |
|                        JSON Protocol     v                                            |
|                   +--------------------------------------------+                      |
|                   |   MFS100BridgeSvc (x86 Managed Windows Svc)|                      |
|                   |   Coordinates IPC, session locks, watchdog |                      |
|                   +--------------------------------------------+                      |
|                                          |                                            |
|                                          v Anonymous Pipe                             |
|                   +--------------------------------------------+                      |
|                   |    MFS100Worker (x86 Native Bridge Host)   |                      |
|                   |      Hosts Mantra 32-bit SDK & Engine      |                      |
|                   +--------------------------------------------+                      |
|                         |                                |                            |
|                         v                                v                            |
|             +-----------------------+        +-----------------------+                |
|             |     MFS100Dll.dll     |        |  iengine_ansi_iso.dll |                |
|             |  (Mantra 32-bit SDK)  |        |  (Minutiae Matcher)   |                |
|             +-----------------------+        +-----------------------+                |
|                                          |                                            |
+------------------------------------------|--------------------------------------------+
                                           v
                              +--------------------------+
                              | Mantra MFS100 USB Device |
                              | (USB\VID_2C0F&PID_1005)  |
                              +--------------------------+
```

### Technical Solution to the 32-bit SDK Restriction

Windows 11 Biometric Service (`WbioSrvc`) runs strictly in a **64-bit (`x64`)** host process. The manufacturer distributes only **32-bit (`x86`)** dynamic link libraries (`MFS100Dll.dll`, `iengine_ansi_iso.dll`). Because 64-bit processes cannot load 32-bit DLLs into their address space:
1. `MFS100SensorAdapter.dll` and `MFS100EngineAdapter.dll` are compiled as native **64-bit (`x64`)** plugins conforming to Microsoft's WBDI specifications.
2. The adapters connect to a local high-performance IPC pipe (`\\.\pipe\MFS100Bridge`) managed by the background service.
3. The background service delegates hardware calls to an isolated 32-bit worker process (`MFS100Worker.exe`), capturing ISO/ANSI minutiae templates and returning JSON responses in real-time.

### Reverse-Engineered Storage Adapter Compatibility

During logon identification (`EngineAdapterIdentifyFeatureSet`), the Windows Biometric Framework queries enrolled database templates (`C:\Windows\System32\WinBioDatabase\*.DAT`). 

By disassembling Microsoft's native `winbiostorageadapter.dll`, we discovered that Microsoft's query engine enforces an explicit undocumented wildcard check:
```x86asm
cmp ecx, 1                     ; WINBIO_ID_TYPE_WILDCARD
jne ...
cmp dword ptr [rsi+4], 25066282h ; Value.Wildcard MUST equal WINBIO_IDENTITY_WILDCARD
je  valid_query
mov ebx, 80070057h            ; E_INVALIDARG
```
Setting `wildcardId.Value.Wildcard = WINBIO_IDENTITY_WILDCARD` (`0x25066282`) ensures the query cursor properly iterates all enrolled templates, enabling immediate matching on the lock screen.

---

## Commercial Code Signing & Production Deployment

If deploying to corporate environments where enabling `testsigning` is not permitted:

1. **User-Mode Exemption**: WBF adapter DLLs are **User-Mode UMDF plugins**, not kernel-mode drivers (`.sys`). Microsoft **does not require WHQL attestation** for user-mode DLLs.
2. **Authenticode Requirement**: The DLLs simply require a standard **Commercial Authenticode / EV Code Signing Certificate** from a publicly trusted CA (DigiCert, Sectigo, GlobalSign, SSL.com) with the `/INTEGRITYCHECK` PE characteristic:
   ```cmd
   signtool sign /v /fd SHA256 /ph /tr http://timestamp.digicert.com /td SHA256 /a MFS100SensorAdapter.dll
   signtool sign /v /fd SHA256 /ph /tr http://timestamp.digicert.com /td SHA256 /a MFS100EngineAdapter.dll
   ```
3. Once signed with a trusted commercial certificate, the system operates in standard Windows mode with Secure Boot fully active.

---

## Diagnostics & Troubleshooting

### Real-Time Logs

To monitor the WBF adapters, biometric captures, and match scores in real time:
1. Right-click **`show_logs.bat`** in `dist\MFS100_Deployment` and select **Run as administrator**.
2. Shows:
   - Enrolled database files in `C:\Windows\System32\WinBioDatabase`
   - Real-time logs from `C:\Windows\System32\WinBioPlugIns\MFS100Wbf.log`
   - Real-time logs from `%ProgramData%\MFS100CP\Logs\BridgeService.log` and `Worker.log`

### Common Solutions

| Issue | Cause | Solution |
| :--- | :--- | :--- |
| **"Your fingerprint couldn't be recognized"** | Wildcard query rejected or score threshold | Ensure you are using release `v1.0.0` or newer; run `7_Register_WBF_Windows_Hello.bat`. |
| **"This device is not working properly"** | Certificate not trusted or Test Signing disabled | Run `bcdedit /set testsigning on` and restart. Ensure `MantraWbfCert.cer` is installed into Trusted Root. |
| **"Windows Hello isn't available"** | Windows PIN is missing | Go to *Sign-in options* and create a PIN first. |
| **Sensor LED does not turn on** | Device disconnected / driver missing | Verify device appears as `MFS100` in Device Manager under Universal Serial Bus devices. |

---

## Uninstallation

To cleanly remove the WBF integration and revert Windows to its default biometric state:

1. Right-click **`8_Unregister_WBF_Windows_Hello.bat`** and run as Administrator.
2. The script unregisters the database and adapter configurations from the registry, deletes the adapter DLLs from `WinBioPlugIns`, and restarts `WbioSrvc`.
3. Standard PIN and password sign-in remain completely unaffected.

---

## Legacy Track B: Standalone Credential Provider (Fallback)

> [!NOTE]  
> The custom Credential Provider (`MFS100CP.dll` + `MFS100Enroll.exe`) was developed as an exploratory fallback. Because **Track A (Native Windows Hello)** is now fully functional, the Credential Provider track is preserved only as a legacy reference:
> - `5_Register_Credential_Provider.bat` (Registers custom login tile)
> - `6_Unregister_Credential_Provider.bat` (Unregisters login tile)
> - `run_enrollment.bat` (Standalone CLI enrollment tool)

---

## Repository Structure

```
.
├── dist/
│   ├── Mantra_MFS100_Windows_Hello_v1.0.0.zip  # Production-ready release package
│   └── MFS100_Deployment/                      # Deployment binaries, signed adapters & scripts
│       ├── 7_Register_WBF_Windows_Hello.bat    # Master WBF registration script
│       ├── 8_Unregister_WBF_Windows_Hello.bat  # Master unregistration script
│       ├── show_logs.bat                       # Live diagnostic logger
│       ├── QUICKSTART.txt                      # Offline installation guide
│       ├── MFS100SensorAdapter.dll             # 64-bit WBF Sensor Adapter
│       ├── MFS100EngineAdapter.dll             # 64-bit WBF Engine Adapter
│       ├── MFS100BridgeSvc.exe                 # Background service broker
│       ├── MFS100Worker.exe                    # 32-bit hardware worker host
│       └── MantraWbfCert.cer                   # Driver signing certificate
├── docs/                                       # Architecture blueprints & specs
├── src/
│   ├── WbfAdapters/                            # 64-bit WBF Sensor & Engine C++ source
│   ├── BridgeService/                          # C# Named Pipe IPC Service
│   ├── Worker/                                 # 32-bit C# Mantra SDK worker
│   ├── CredentialProvider/                     # Legacy C++ Credential Provider
│   └── EnrollmentTool/                         # Standalone WPF/Console enrollment tool
└── README.md
```

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
All Mantra SDK binaries (`MFS100Dll.dll`, `iengine_ansi_iso.dll`, `MANTRA.MFS100.dll`) remain the intellectual property of Mantra Softech India Pvt. Ltd.
