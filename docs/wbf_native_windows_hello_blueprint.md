# Native Windows Hello Integration Blueprint: Mantra MFS100 Optical Fingerprint Reader

**Target Device**: Mantra MFS100 (`USB\VID_2C0F&PID_1005\8E46E90C49AC`)  
**Target OS**: Windows 11 Pro 64-bit (Build 26100+ / 26300+) in VMware Workstation Pro (`babu_vm`)  
**Core Framework**: Windows Biometric Framework (WBF / `WbioSrvc.exe`) & Windows Hello (NGC / Passport)

---

## 1. Architectural Architecture

Windows Biometric Framework manages biometrics through `WbioSrvc.exe` using a tripartite adapter architecture. Because the physical Mantra MFS100 SDK is strictly 32-bit (x86), while `WbioSrvc.exe` is strictly 64-bit (x64), the architecture bridges the process boundary using high-speed local IPC:

```
┌────────────────────────────────────────────────────────────────────────┐
│             Windows 11 Hello Subsystem (NGC / LogonUI)                 │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
┌───────────────────────────────────▼────────────────────────────────────┐
│              Windows Biometric Service (WbioSrvc.exe - x64)            │
│                                                                        │
│  ┌─────────────────────────┐ ┌─────────────────────────┐ ┌───────────┐  │
│  │   MFS100SensorAdapter   │ │   MFS100EngineAdapter   │ │ Inbox     │  │
│  │   (x64 WBF Sensor DLL)  │ │   (x64 WBF Engine DLL)  │ │ WinBio    │  │
│  │   - WbioQuerySensorIntf │ │   - WbioQueryEngineIntf │ │ Storage   │  │
│  │   - WinBioStartCapture  │ │   - WinBioVerifyFeature │ │ Adapter   │  │
│  │   - Generates WINBIO_BIR│ │   - WinBioUpdateEngine  │ │           │  │
│  └────────────┬────────────┘ └────────────┬────────────┘ └─────┬─────┘  │
└───────────────┼───────────────────────────┼────────────────────┼────────┘
                │ Named Pipe IPC            │ Named Pipe IPC     │
                ▼                           ▼                    ▼
┌────────────────────────────────────────────────────────┐ ┌───────────────┐
│     MFS100Worker.exe (32-bit Background Worker)        │ │ %SystemRoot%\ │
│  - Hosts MFS100Dll.dll & iengine_ansi_iso.dll          │ │ System32\     │
│  - Streams 316x354 8-bit raw grayscale frame           │ │ WinBioDatabase│
│  - Extracts & Matches ISO 19794-2 Minutiae Templates   │ │ (*.DAT)       │
└───────────────────────┬────────────────────────────────┘ └───────────────┘
                        │
┌───────────────────────▼────────────────────────────────────────────────┐
│  Mantra MFS100 Vendor Driver (mfs100.sys) & Hardware (VID_2C0F&PID_1005)│
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Component Deliverables

### A. WBF Adapter DLLs (x64 Native C++)
1. **`MFS100SensorAdapter.dll`**:
   - Implements `WINBIO_SENSOR_INTERFACE` (`SensorAdapterAttach`, `SensorAdapterDetach`, `SensorAdapterStartCapture`, `SensorAdapterFinishCapture`, `SensorAdapterCancel`, `SensorAdapterPushDataToEngine`).
   - Connects to `\\.\pipe\MFS100Bridge` to request capture frames.
   - Converts frames into standard `WINBIO_BIR` structures containing raw biometric samples and biometric headers.
2. **`MFS100EngineAdapter.dll`**:
   - Implements `WINBIO_ENGINE_INTERFACE` (`EngineAdapterAttach`, `EngineAdapterDetach`, `EngineAdapterCreateKey`, `EngineAdapterIdentifyFeatureSet`, `EngineAdapterVerifyFeatureSet`, `EngineAdapterUpdateEngine`, `EngineAdapterCommitEnrollment`, `EngineAdapterExportEngineData`).
   - Interfaces with the worker engine to extract ISO minutiae and compare probe vs gallery.
   - Formats templates for persistence into standard WinBio database records.

### B. Device Registration & Driver Package
3. **`mfs100_wbf.inf`**:
   - Registers the device under `Class = Biometric` (`ClassGuid = {53D29EF7-377C-4D14-864B-EB763E22F6A5}`).
   - Declares the hardware ID `USB\VID_2C0F&PID_1005`.
   - Populates `HKR, "WinBio\Configurations\0"` with `SensorAdapterBinary`, `EngineAdapterBinary`, `StorageAdapterBinary = "winbiostorageadapter.dll"`.
4. **Certificate & Catalog Signing**:
   - Self-signed test certificate generation script (`create_test_cert.bat`) to sign the package for VM test environments.

### C. Validation & Diagnostics CLI
5. **`TestWbfUnit.exe`**:
   - Console tool using native `WinBio` APIs (`WinBioOpenSession`, `WinBioEnumBiometricUnits`, `WinBioLocateSensor`, `WinBioEnrollBegin`, `WinBioEnrollCaptureData`, `WinBioEnrollCommit`, `WinBioIdentify`).
   - Directly validates each adapter stage before launching the Windows Hello GUI.

---

## 3. Five-Stage Checkpoint Verification

| Checkpoint | Gate | Acceptance Criteria |
|---|---|---|
| **CP-1** | Driver & INF Installation | Device Manager displays Mantra MFS100 under `Biometric Devices` without error code 10/28/43. |
| **CP-2** | WBF Enumeration | `WbioSrvc` starts, loads both adapters, and `TestWbfUnit.exe` reports Unit ID with `WINBIO_TYPE_FINGERPRINT`. |
| **CP-3** | Windows Hello Acceptance | Windows Settings -> Accounts -> Sign-in options displays "Fingerprint recognition (Windows Hello)" as available (not "No compatible scanner found"). |
| **CP-4** | Native Enrollment | Launching "Set up" displays the native Windows Hello fingerprint setup wizard, captures touches, and commits enrollment to `%SystemRoot%\System32\WinBioDatabase`. |
| **CP-5** | Native Sign-In & Cold Boot | Pressing `Win + L` or booting the VM presents the Windows Hello fingerprint sign-in prompt; touching the sensor verifies through WBF and unlocks Windows. |
