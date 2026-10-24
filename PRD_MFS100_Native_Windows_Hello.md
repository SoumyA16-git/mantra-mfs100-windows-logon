# PRD — Mantra MFS100 as a Native Windows Hello Fingerprint Sensor (Windows 11)

| Field | Value |
|---|---|
| Document ID | PRD-MFS100-WH-001 |
| Version / status | 0.1 — **Draft for engineering review** (PRD only; no code written, no system settings changed) |
| Date | 2026-10-24 |
| Target repo | `https://github.com/SoumyA16-git/mantra-mfs100-windows-logon` (local: `C:\Users\soumy\Documents\Mantra Device Windows Log-on System`) |
| Repository basis | GitHub snapshot, branch default, single commit `b26d705` ("fix(wbf): register GUID_DEVINTERFACE_BIOMETRIC_READER and configure WbioSrvc database"), cloned into an isolated analysis sandbox on 2026-10-24. **The local working copy was not accessible to the author.** Anything that exists only locally (USBPcap analyzer, PowerShell evidence helpers, raw captures, logs) is marked *not located* and is classified E2 at best. |
| Test platform | Windows 11 Pro, VMware Workstation Pro guest, build 26300.9457; Mantra MFS100 `USB\VID_2C0F&PID_1005\8E46E90C49AC`; Mantra SDK 9.0.2.5 (x86) |
| Host safety | Host Windows installation is out of scope for any change. All driver/boot/registry work is VM-only (Section 19). |

---

## 0. How to read this document

**Decision labels** (applied to every architectural choice):

| Label | Meaning |
|---|---|
| **[Verified]** | Confirmed in this review by inspecting a named artifact (file, binary, export table) or an official Microsoft page listed in Appendix D. |
| **[Proposed]** | Engineering recommendation. Not yet validated. |
| **[Hypothesis]** | Believed plausible; an experiment (test ID given) must confirm or refute it. |
| **[Blocked]** | Cannot be decided or built until the named evidence exists. |

**Evidence classes** (applied to facts about the existing system):

| Class | Meaning |
|---|---|
| **E1** | Verified here: artifact inspected during this review. |
| **E2** | Project-reported: stated by the owner or in repo documentation; artifact not located or not re-run. |
| **E3** | Hypothesis: inferred, needs an experiment. |
| **E4** | Untested or unsupported claim (including claims in repo docs that contradict code or Microsoft documentation). |

No plan, assumption or project claim is promoted to "Verified" without an artifact. Where this PRD says something about Microsoft platform behaviour it cites an official page (Appendix D); where official documentation is silent the item is labelled **[Hypothesis]** or **[Blocked]**.

---

## 1. Document purpose, scope and definitions

### 1.1 Purpose
Define an implementation-ready, evidence-driven plan to make the original MFS100 optical fingerprint scanner be accepted and used by the **native Windows Hello fingerprint experience** on Windows 11, and to define how that claim will be proven or disproven.

### 1.2 In scope
- WBF integration for the MFS100: driver package, sensor adapter, engine adapter, storage adapter selection, IPC to the existing 32-bit SDK host.
- Native Windows Hello Settings enrollment, verification, lock-screen/sign-in unlock, PIN/NGC coexistence, and the biometric-enforcement (MFA) coverage study.
- Test tooling (`TestWbfUnit.exe` and successors), VM test harness, install/rollback runbook, security and threat model, Microsoft approval/signing gates.

### 1.3 Out of scope / non-goals
- A custom credential provider, password autofill, password vault, simulated Windows Hello UI, or standalone matcher **as a substitute** for R1–R4 (these may exist as legacy artifacts but never count toward acceptance).
- Falsifying or bypassing a Microsoft-issued biometric approval signature, impersonating another vendor's certified sensor, or patching protected authentication binaries.
- Emulating a secure enclave or claiming ESS / match-on-chip behaviour for a software-matching sensor.
- Any change to the host Windows installation.
- Production certification (WHCP/HLK submission) — planned as a decision gate (Section 17), not as committed scope.

### 1.4 Definitions

| Term | Definition |
|---|---|
| WBF / WBS | Windows Biometric Framework / Windows Biometric Service (`WbioSrvc`). |
| WBDI | Windows Biometric Driver Interface — IOCTL-based driver interface that WBS uses to talk to biometric device drivers. |
| UMDF | User-Mode Driver Framework (Microsoft's recommended model for WBDI drivers). |
| Sensor / Engine / Storage adapter | WBF plug-ins: capture and sample flow; template creation + matching + indexing; template database management. |
| Biometric unit | WBF software object that represents one sensor plus its adapters. |
| BIR | Biometric Information Record — the container WBF passes between adapters. |
| NGC | Next Generation Credentials — Windows Hello key container/credential architecture (PIN and biometric gestures both unlock NGC keys). |
| ESS | Enhanced Sign-in Security — VBS/TPM-backed Windows Hello mode; for fingerprint it requires match-on-sensor hardware with an embedded Microsoft-issued certificate. |
| SDCP | Secure Device Connection Protocol used by ESS-capable sensors. |
| PAD | Presentation Attack Detection (anti-spoofing; ISO/IEC 30107). |
| Windows Hello signature | The bio-specific signature (`SignatureAttributes.WindowsHello`) that Microsoft issues through the Partner Center process and that the OS checks on adapter binaries. |
| Worker / Bridge | Existing `MFS100Worker.exe` (x86, hosts SDK) and `MFS100BridgeSvc.exe` (x86 service, named pipe `\\.\pipe\MFS100Bridge`). |

### 1.5 Success definition (anti-redefinition guard)
Success means the acceptance criteria in Section 14 pass **on native Windows surfaces** (Settings enrollment wizard, lock screen, sign-in screen, Microsoft-Windows-Biometrics logs). The following **do not** count as success for any of R1–R4: a custom credential-provider tile, password replay/vault release after a fingerprint match, a simulated Hello dialog, a CLI that prints "MATCH", or a green `TestWbfUnit.exe` run.

---

## 2. Executive summary and product vision

### 2.1 Vision
An MFS100 plugged into a Windows 11 PC appears under **Settings → Accounts → Sign-in options → Fingerprint recognition (Windows Hello)**, enrolls through the OS wizard, unlocks the same NGC credential the PIN unlocks, and leaves PIN, password and recovery flows untouched.

### 2.2 State of play (what the repository actually contains)
1. A working standalone enrollment/matching suite built on the x86 Mantra SDK (worker + service + CLI + DPAPI-encrypted template store). Results are **E2** (documented, raw logs not in repo).
2. A **first-draft WBF layer already exists** in `src/WbfAdapters/` (x64 sensor adapter, x64 engine adapter, INF, registration utility, diagnostic CLI). Binaries are committed, **unsigned**, no catalog, and **no evidence artifact shows they have ever been loaded by `WbioSrvc`**.
3. A custom credential provider (`MFS100CP.dll`) and a **DPAPI-backed Windows-password vault** exist. These are the opposite of the stated goal (native Hello/NGC) and are excluded from the target architecture.
4. Repo documentation is internally contradictory (Appendix E): one document says native Hello is "impossible", another says "achievable", the README says "hard blocked", yet adapters were committed.

### 2.3 Three decisive findings from this review
| # | Finding | Evidence |
|---|---|---|
| D1 | **WBS discovers devices through `GUID_DEVINTERFACE_BIOMETRIC_READER` = `{E2B5183A-99EA-4cc3-AD6B-80CA8D715B80}`, opens a handle, and queries capabilities via the WBDI control interface.** `RegisterWbfDevice.cpp` registers `{E2830FF2-DD38-4de6-AF5D-82990439237E}` — a different GUID — by hand-writing `DeviceClasses` registry keys. As written, WBS has no documented reason to enumerate the MFS100. | **[Verified]** F-01; Microsoft *Plug-in architecture* & *Biometric Unit Life Cycle* (App. D #1, #2) |
| D2 | **Microsoft enforces Windows Hello compatibility with an OS check for a specific signature obtainable only through the DevCenter/Partner Center review** (HLK Fingerprint test, security review template, FAR/FRR/PAD self-validation). Obtaining it is *approval*, not a technical switch. Whether the check is enforced on a VM in test-signing mode is **not documented**. | **[Verified]** (requirement); **[Blocked]** (VM behaviour) — App. D #3 |
| D3 | **The MFS100 is an optical, host-matched sensor.** It cannot satisfy ESS (match-on-sensor + embedded certificate), so non-ESS operation is the only possible mode, and Microsoft's published Hello requirements (FAR, FRR, anti-spoofing) must still be evidenced for any approval. | **[Verified]** ESS requirement (App. D #4); MFS100 optical/host-match nature is E2 |

### 2.4 Critical pre-read: security findings that block any use with real credentials
The existing bridge service has issues that must be remediated (Section 10.4, tasks T-001…T-006) **before** a real fingerprint or real account is used with it, even in the VM:
- `identify_user` accepts a **client-supplied threshold** (default 500; a client may send 0), requires **no caller authorization**, and **returns the vault-stored Windows password in plaintext** on a "match" (`PipeServer.cs`, `identify_user`). The pipe DACL grants *Authenticated Users* read/write.
- Every request and response is logged verbatim (`PIPE REQ:` / `PIPE RSP:`), i.e. **biometric templates and the released password are written to `%ProgramData%\MFS100CP\logs\` log files**.
- `capture` and `match` (forwarded by the `default:` branch) are unauthenticated; any local user can trigger a capture or use the service as a match oracle.

These are **[Verified]** by source inspection (F-20…F-24). They do not affect the *design* of the WBF path, but they make the current service unsafe as the IPC target for adapters running in `WbioSrvc`.

### 2.5 Go / No-Go gates (cheap evidence first)
| Gate | Question | Pass evidence | If it fails |
|---|---|---|---|
| **G0** | Is the baseline sane? (security remediation, clean VM snapshot, evidence ledger rebuilt) | T-001…T-010 done | Stop; fix baseline |
| **G1** | Does `WbioSrvc` create a biometric unit for *any* MFS100-backed device node? (event 1108 + `WinBioEnumBiometricUnits`) | AC-03 | Re-architect device node (Path A2 / B) |
| **G2** | Does Windows Settings accept the unit as a Hello fingerprint sensor in the VM, without the Windows Hello signature? | AC-05 | **Native Hello for this sensor is gated by Microsoft's process (Section 17).** Stop engineering spend on enrollment/sign-in; decide: pursue Microsoft approval with Mantra, or use a certified sensor |
| **G3** | Does the native enrollment wizard complete and persist a template? | AC-06/07 | Fix engine/storage; or stop |
| **G4** | Does lock-screen / sign-in unlock work natively? | AC-11…AC-14 | Document limits |
| **G5** | MFA coverage study complete with evidence | AC-24 | Publish limits |

**Outlook (honest):** G1 and G2 are the real risks. The plan is ordered so that they are tested before any of the expensive work (full engine, storage integration, hardening) is done.

---

## 3. User problems and intended user experience

| # | Persona | Problem today | Intended experience |
|---|---|---|---|
| U1 | Device owner (local account / MSA) | MFS100 works in vendor test app only; Windows says no compatible fingerprint scanner | Settings shows fingerprint option; wizard enrolls; Win+L unlock by touch |
| U2 | Administrator | Needs predictable, reversible driver/adapter install and a clear statement of what is and is not enforced | Single signed package, `pnputil` install/uninstall, coverage matrix of sign-in paths |
| U3 | Security reviewer | Needs to know where templates live, who can read them, and what is logged | Templates only in WBF database; no passwords stored; no biometric data in logs |
| U4 | Help desk / recovery | Locked out when scanner missing, service stopped, or enrollment corrupt | PIN and password always work; documented recovery ladder |

**Intended native flow (Microsoft behaviour, not custom UI):** a PIN must exist before biometrics are offered (App. D #9, #10); user selects *Fingerprint recognition (Windows Hello) → Set up*; OS wizard prompts touches; OS commits enrollment; lock screen offers fingerprint plus PIN/password; every failure falls back to PIN.

**UX constraints:** no custom dialogs in the sign-in path; vendor LED/beep behaviour is not required; all error text comes from Windows; our only UX deliverables are the diagnostic CLI output and the runbook.

---

## 4. Functional requirements (R1–R4)

Priority: **P0** = required for the requirement to be considered met; **P1** = required for release readiness; **P2** = desirable. "AC" = acceptance criterion in Section 14. Labels apply to the *requirement's feasibility*, not to its importance.

### R1 — Native Windows Hello fingerprint

| ID | Requirement | Pri | Label | AC |
|---|---|---|---|---|
| R1.1 | `WinBioEnumBiometricUnits(WINBIO_TYPE_FINGERPRINT)` returns a unit backed by the MFS100 device instance, and `Microsoft-Windows-Biometrics/Operational` records the sensor being loaded (event ID 1108 per Microsoft ESS documentation). | P0 | [Blocked] on G1 | AC-03 |
| R1.2 | The device appears under *Device Manager → Biometric devices* with no problem code, and its driver package is installed through the driver store (`pnputil`), not by manual file copy. | P0 | [Proposed] | AC-01, AC-02 |
| R1.3 | A physical touch yields a sample through the WBF pipeline (sensor adapter or WBDI driver → engine) reachable via WinBio APIs. | P0 | [Proposed] | AC-04 |
| R1.4 | *Settings → Accounts → Sign-in options* offers *Fingerprint recognition (Windows Hello)* as available for the MFS100 (not "couldn't find a fingerprint scanner compatible with Windows Hello"). | P0 | [Blocked] on G2 | AC-05 |
| R1.5 | The native enrollment wizard completes; the engine creates a template from the captured samples, rejects bad samples with correct `WINBIO_REJECT_DETAIL`, and commits it through the storage adapter. | P0 | [Blocked] on G2 | AC-06, AC-07 |
| R1.6 | A subsequent touch of the enrolled finger verifies; an un-enrolled finger, partial touch, or no touch does not. | P0 | [Proposed] | AC-08, AC-09 |
| R1.7 | Enrollment detects duplicate templates (same finger enrolled twice) when WBF requests it. | P1 | [Proposed] | AC-06 |
| R1.8 | Removing the fingerprint in Settings deletes the template from WBF storage. | P0 | [Proposed] | AC-26 |
| R1.9 | Engine template format is versioned; an incompatible upgrade forces re-enrollment rather than mis-matching. | P1 | [Proposed] | AC-07 |
| R1.10 | Match decisions are produced by the SDK's ISO/ANSI engine through the bridge, with the threshold fixed in one place and justified by the evaluation protocol (Section 13.6). | P0 | [Proposed] (compatibility **[Hypothesis]** until demonstrated, T-040) | AC-08, AC-27 |
| R1.11 | The integration does not claim ESS or match-on-sensor properties. | P0 | [Verified] policy | AC-19 |

### R2 — Native PIN and NGC compatibility

| ID | Requirement | Pri | Label | AC |
|---|---|---|---|---|
| R2.1 | Windows Hello PIN creation, sign-in, change and reset behave exactly as on a machine without the MFS100 integration. (Microsoft: a PIN is required before/along with biometrics and is the backup when biometrics fail.) | P0 | [Verified] platform behaviour | AC-15 |
| R2.2 | No component of this product stores, caches, transmits or replays a Windows password or PIN. The existing `CredentialVault` and the password-bearing `identify_user` response are removed from the product and from the Hello test build. | P0 | [Proposed] | AC-21 |
| R2.3 | Release of the NGC credential after a successful biometric match is performed by Windows (WBS → NGC), not by custom code. Tests run with `MFS100CP.dll` **unregistered** so that only native paths are exercised. | P0 | [Proposed] | AC-22 |
| R2.4 | Loss of the sensor, bridge, worker, or adapters never removes the ability to sign in with PIN or password. | P0 | [Proposed] | AC-10, AC-15 |
| R2.5 | PIN reset / "I forgot my PIN" flows work with and without an enrolled fingerprint. | P1 | [Proposed] | AC-16 |

### R3 — Sign-in and unlock

| ID | Requirement | Pri | Label | AC |
|---|---|---|---|---|
| R3.1 | Fingerprint unlock at **workstation lock** (Win+L, idle lock). | P0 | [Blocked] on G3 | AC-11 |
| R3.2 | Fingerprint sign-in after **sign-out**. | P0 | [Blocked] on G3 | AC-12 |
| R3.3 | Fingerprint sign-in after **restart**. | P0 | [Blocked] on G3 | AC-13 |
| R3.4 | Fingerprint availability at **cold boot** (first sign-in after power-on). Requires bridge service + worker + SDK init to complete before the sign-in UI needs the sensor; if not achievable, document PIN/password-first behaviour. | P0 | [Hypothesis] — depends on service start order | AC-14 |
| R3.5 | Behaviour documented for: UAC prompts, Fast User Switching, Remote Desktop, Safe Mode, sleep/resume, hot-unplug. | P1 | [Proposed] | AC-29 |
| R3.6 | Results recorded per account type: local account, Microsoft account, Entra ID, domain. Unsupported types are explicitly listed. | P1 | [Hypothesis] | AC-25 |

### R4 — MFA / biometric enforcement

| ID | Requirement | Pri | Label | AC |
|---|---|---|---|---|
| R4.1 | Produce a **sign-in/unlock path inventory** (interactive, lock, UAC, RDP, network, runas, safe mode, WinRE, offline) with, for each path, whether biometrics can be offered, required, or are bypassed. | P0 | [Proposed] | AC-24 |
| R4.2 | Enumerate **bypass and recovery routes** (password, PIN, PIN reset, local admin reset, alternate credential providers, safe mode, offline attacks, unsupported account types). | P0 | [Proposed] | AC-24 |
| R4.3 | Evaluate the supported policy controls (Windows Hello for Business "Use biometrics", biometric Group Policy, interactive-logon policies). Candidate controls are **not verified here** and must be tested; Microsoft guidance indicates Hello replaces the password as the interactive factor and PIN is always the backup, so *biometric-only* enforcement is **[Hypothesis: not supported natively]**. | P1 | [Hypothesis] | AC-24 |
| R4.4 | Publish a coverage matrix with test evidence per path. **Universal MFA enforcement is never claimed** unless every row passes. | P0 | [Proposed] | AC-24 |
| R4.5 | Any enforcement mechanism must have a hardware-independent fail-open recovery path (scanner absent ≠ lockout) and be test-reverted via snapshot. | P0 | [Proposed] | AC-16, AC-24 |

### Remediation requirements (preconditions)

| ID | Requirement | Pri | AC |
|---|---|---|---|
| REM-1 | Remove/disable `CredentialVault`, `save/has/delete_vault_credential`, and any password field in `identify_user`. | P0 | AC-21 |
| REM-2 | Remove template/probe/password content from all logs; log only lengths, hashes of non-secret IDs, scores, error codes. | P0 | AC-23 |
| REM-3 | Authorize every bridge command; bind biometric commands used by adapters to the `WbioSrvc` caller identity; no client-supplied thresholds. | P0 | AC-17, AC-18 |
| REM-4 | Fix the interface GUID and replace hand-written `DeviceClasses`/`Enum` registry writes with a PnP-installed driver package. | P0 | AC-01, AC-03 |
| REM-5 | Scrub repository of sensitive/irrelevant artifacts (Section 11.6). | P1 | AC-30 |

---

## 5. Non-functional requirements

Rule applied to every numeric target: *a number is only a target once its measurement method and basis exist*. Where the basis is "baseline", the target is set at milestone M2/M3 from measured data and recorded here by amendment. Provisional numbers are labelled **[Hypothesis]**.

| ID | Area | Requirement | Measurement method | Basis for target |
|---|---|---|---|---|
| NFR-REL-01 | Reliability | `WbioSrvc`, bridge, worker or sensor failure never blocks PIN/password sign-in. | Fault-injection suite (AC-10) | Binary pass/fail (R2.4) |
| NFR-REL-02 | Reliability | Worker crash/hang is detected and the next request succeeds without manual action. | Kill worker mid-capture; unplug/replug; measure time-to-first-successful-capture | Baseline in M2; existing design uses hard deadline `timeoutMs + 2500 ms` (E1, `PipeServer.cs`) |
| NFR-REL-03 | Reliability | No handle/memory leaks in adapters across ≥ N enroll/verify cycles. | Driver Verifier + handle/private-bytes trend in `WbioSrvc` over a soak run | N chosen at M8 from observed trend; **[Hypothesis]** N = 500 cycles |
| NFR-LAT-01 | Latency | Touch-to-decision (capture complete → match result) is measured and reported at p50/p95 for 100 verifications. | ETW timestamps at sensor-adapter capture-complete and engine verify-return | Baseline M3; Hello UI timeouts are not documented in sources reviewed → target set empirically so lock-screen use is not worse than vendor test app |
| NFR-LAT-02 | Latency | Per-request bridge overhead excluding capture/match. | Bridge ETW | Baseline M2; compare against current per-disconnect worker restart (F-13) |
| NFR-CMP-01 | Compatibility | Supported: Windows 11 x64 build 26300.9457 (test VM) plus the latest GA 24H2/25H2 build verified by test; ARM64 out of scope. | Test matrix | Environment fact (E1 vmx; build is user-reported E2) |
| NFR-CMP-02 | Compatibility | Coexists with the original vendor driver (restorable) and with Microsoft's inbox biometric stack. | AC-01 rollback | Requirement E |
| NFR-CMP-03 | Compatibility | x64 adapters ↔ x86 SDK host only via IPC; no x64 SDK is assumed (none exists in repo — E1). | PE-header check in CI | E1 |
| NFR-ACC-01 | Accessibility | All user-facing UI is OS-native (Settings, lock screen). Our docs/CLI output must be screen-reader friendly plain text. | Manual review | Inherit Windows accessibility |
| NFR-MNT-01 | Maintainability | One solution, reproducible build, signed artifacts, pinned toolchain versions, unit tests for adapters' pure logic (framing, parsing, state machines). | CI build log | Engineering practice |
| NFR-MNT-02 | Maintainability | Single source of truth for threshold, GUIDs, IPC schema (shared header + schema doc). | Code review | F-10, F-11 |
| NFR-PRF-01 | Performance | Adapters run in `WbioSrvc`; they must not block its threads on unbounded waits (F-12). | Static review + hang test | Design rule |
| NFR-SEC-01 | Security | Zero biometric payload or credential in logs/ETW (REM-2). | Log grep suite AC-23 | Privacy requirement |
| NFR-SEC-02 | Security | All third-party DLLs loaded by adapters are signed and listed in the INF per Microsoft's signing guidance. | INF lint + signtool verify | App. D #3 |

---

## 6. Existing-system inventory and evidence classification

### 6.1 Component inventory (GitHub snapshot `b26d705`)

| Component | Path | Technology / bitness (E1) | What the code actually does | Runtime evidence |
|---|---|---|---|---|
| MFS100Worker | `src/Worker/` | .NET Framework 4.8, **x86** (PE `0x14C`) | Hosts the managed wrapper `MANTRA.MFS100.dll` → `MFS100Dll.dll`. Commands `init`, `capture`, `match`, `uninit`, `ping` over stdin/stdout JSON. Capture = `AutoCapture(ref FingerData, timeoutMs, IsDetectFinger=false, ShowPreview=false)`; returns ISO + ANSI templates, quality, NFIQ. Match = `MatchISO(probe, gallery, ref score)`. **No raw-frame command** (`MFS100GetFrame` is exported by the SDK but unused). | E2 (docs/test_results.md, dated 2026-10-23; raw logs not in repo) |
| MFS100BridgeSvc | `src/BridgeService/` | .NET, **x86** | Windows service; pipe `\\.\pipe\MFS100Bridge`; spawns/kills worker; DPAPI(LocalMachine)+SID-entropy template store at `%ProgramData%\MFS100CP\templates\<SID>.dat`; **CredentialVault** (password store). | E2 |
| MFS100Enroll | `src/EnrollmentTool/` | .NET, x86 | Console enrollment/verify/identify and an embedded 13-test "boundary test" suite. | E2 (README "13/13 passed"; no log artifact) |
| Sensor adapter | `src/WbfAdapters/MFS100SensorAdapter.*` | C++ **x64** (`0x8664`), exports `WbioQuerySensorInterface`, unsigned | On `StartCapture` spawns a thread that connects to the bridge, requests `capture`, extracts `isoTemplate` by substring search, wraps it in a BIR. | **None** (no log shows it loaded) |
| Engine adapter | `src/WbfAdapters/MFS100EngineAdapter.*` | C++ x64, exports `WbioQueryEngineInterface`, unsigned | Copies sample bytes as "feature set"; "enrollment" = copy of last sample after 3 updates; verify = bridge `match` against the in-memory enrollment copy, threshold 500. | **None** |
| INF | `src/WbfAdapters/mfs100_wbf.inf` | `Class=Extension`, `CatalogFile=mfs100_wbf.cat` (**file absent**) | Intended to add `WinBio\Configurations` values to the vendor device node. | **None** |
| RegisterWbfDevice | `src/WbfAdapters/RegisterWbfDevice.*` | C++ x64 | SetupAPI interface creation + direct registry writes (Section 6.2 F-01…F-06). | **None** |
| TestWbfUnit | `src/WbfAdapters/TestWbfUnit.*` | C++ x64 | Enumerate units → open session → locate sensor → begin enrollment, 3 captures, discard. | **None** (no run output) |
| Credential provider | `src/CredentialProvider/` | C++ x64 | Custom tile + password release via vault. **Excluded from target architecture.** | E2 (docs) |
| SDK | `sdk/` | `MFS100Dll.dll`, `iengine_ansi_iso.dll`, `MANTRA.MFS100.dll`: all **x86** (E1); only the managed DLL is Authenticode-signed (E1) | See 6.3 | E1 |
| VM definition | `vm/Windows 11 x64.vmx` | VMware HW v20, UEFI + Secure Boot **on**, NVMe, USB 2.0 EHCI with `vid:2c0f pid:1005` auto-connect, read-only host share | **No vTPM, no nested-virtualization (`vhv`) entry** in the committed file | E1 (file); running VM config not inspected |
| Deployment bundle | `dist/MFS100_Deployment/` | Same binaries as `src/` (SHA-256 of adapters and INF identical — E1), numbered `.bat` scripts | — | E1 |
| **Not located** | — | USBPcap analysis tools, PowerShell evidence helpers, raw USB captures, raw test logs, `implementation_plan.md` (referenced by README) | — | E2 (owner statement) |

### 6.2 Findings register (source-verified unless stated)

Severity: **Crit** = blocks the requirement or breaches the security boundary; **High** = likely to cause failure; **Med/Low** = quality.

| ID | Finding | Evidence | Sev | Affects |
|---|---|---|---|---|
| F-01 | `RegisterWbfDevice.cpp` uses interface GUID `{E2830FF2-DD38-4de6-AF5D-82990439237E}`; Microsoft documents `GUID_DEVINTERFACE_BIOMETRIC_READER` as `{E2B5183A-99EA-4cc3-AD6B-80CA8D715B80}`. | E1 + App. D #1 | Crit | R1.1 |
| F-02 | Interface/registry state is fabricated by hand (`SetupDiCreateDeviceInterfaceW` on a node owned by the vendor driver; direct writes under `Control\DeviceClasses` and `Enum\…\Device Parameters`). These keys are PnP-managed; this is not a supported install path and is not what a driver package does. | E1 | Crit | R1.1, R1.2 |
| F-03 | No WBDI-capable driver exists in the repo. WBS "opens a handle to the device" and queries capabilities through the WBDI control interface (App. D #2); the vendor driver's WBDI status is **unknown** (repo docs contradict each other: "KMDF `MFS100.sys`" vs "proprietary USB bulk driver, not WBDI"). | E1 (absence) + E4 (docs) | Crit | R1.1 |
| F-04 | INF: `Class=Extension`; first `AddReg` line `HKR,,"WinBio\Configurations\0",0x00010001,0` appears to create a DWORD whose *name* contains backslashes, not a key; no `[SourceDisksFiles]`/`[DestinationDirs]`/`CopyFiles` for the adapter DLLs; no `Exclusive`; no database-service section; no `[SignatureAttributes]`/`WindowsHello=true`; referenced `.cat` absent; `docs/wbf_native_windows_hello_blueprint.md` says `Class=Biometric`, INF says otherwise. | E1 | High | R1.2 |
| F-05 | Scripts bypass the driver store (copy DLLs into `System32`, `reg add` WBF keys). `8_Unregister…bat` does not remove the fabricated `DeviceClasses`/`Enum` keys or policy keys, so rollback is incomplete. | E1 | High | Req E |
| F-06 | `RegisterWbfDevice` writes `HKLM\SOFTWARE\Policies\Microsoft\Biometrics\Enabled=1`, `…\Biometrics\Credential Provider\Domain Accounts=1`, `…\Windows\System\AllowDomainPINLogon=1` — policy changes outside device registration, never reverted. | E1 | Med | R2, R4 |
| F-07 | Engine adapter has **no storage integration** (no `WbioStorage*` calls anywhere); `CommitEnrollment` returns `S_OK` without persisting; verify compares against an in-memory copy that dies with the pipeline; `Identity`/`SubFactor` ignored; `IdentifyFeatureSet` aliases verify. | E1 (grep) | Crit | R1.5–R1.6 |
| F-08 | "Enrollment" keeps only the **last** sample; no multi-sample consistency, no merge (SDK exports `ISO_MergeTemplates`), no quality gate, `CheckForDuplicate` always FALSE, no hash support. | E1 | High | R1.5, R1.7 |
| F-09 | Format semantics inconsistent: sensor adapter emits an ISO-style **minutiae template** flagged `WINBIO_DATA_FLAG_RAW`; engine advertises `WINBIO_ANSI_381` (the ANSI finger-*image* format name) as preferred; "feature extraction" is a `memcpy`. | E1; naming semantics **[Hypothesis]** until checked against `winbio_types.h` | High | R1.3, R1.5 |
| F-10 | Three different thresholds: engine adapter **500**, bridge defaults **500**, documented/enrollment **1400**. | E1 | High | R1.10 |
| F-11 | Adapter↔bridge IPC fragility: fixed 16 KB/4 KB buffers, substring "JSON parsing", single `ReadFile` for match reply, new connection per call, no version/request ID. A raw frame (316 × 354 = 111,864 bytes) cannot fit. | E1 | High | R1.3 |
| F-12 | Sensor adapter uses one global `g_CaptureTask` (not per pipeline); `SensorAdapterCancel` closes `hActivePipe` while the capture thread also calls `CloseHandle` (double-close race); `FinishCapture` waits `INFINITE`; `hCancelEvent` unused. | E1 | High | NFR-PRF-01 |
| F-13 | Bridge calls `KillAndRestart()` on **every** client disconnect (`PipeServer.cs:193`): an engine `match` connection can kill a concurrent capture; each call pays worker re-init. | E1 | High | NFR-LAT, NFR-REL |
| F-14 | Only the first pipe instance receives the explicit `PipeSecurity`; later instances use the default DACL; DACL grants *Authenticated Users* ReadWrite; no first-instance squatting guard. | E1 | High | AC-17 |
| F-15 | Whether `WbioSrvc` (service hardening, session 0, restricted token) can open `\\.\pipe\MFS100Bridge` is untested. | E3 | Med | R1.3 |
| F-16 | `TestWbfUnit` opens the session with `WINBIO_FLAG_RAW` then calls enrollment APIs (flag suitability unverified), never commits/verifies/identifies/deletes/cancels, reads no event logs or device state, emits no machine-readable result. No recorded run exists. | E1 | Med | AC-28 |
| F-17 | All first-party binaries unsigned (E1: Authenticode directory empty on both adapters, worker, service, CP, tools); no `.cat`, `.cer`. | E1 | High | AC-20 |
| F-18 | Bitness is consistent with the proposed IPC split: adapters x64; SDK/worker/service x86; no x64 SDK. | E1 | — | NFR-CMP-03 |
| F-19 | Documentation contradictions (Appendix E). | E1 | Med | all |
| **F-20** | `identify_user`: caller-supplied `threshold` (default 500, client can send 0), no `AuthorizeCaller`, and on "match" returns `CredentialVault.LoadCredential()` plaintext password in the reply. | E1 | **Crit** | R2.2 |
| **F-21** | `ServiceLog` records `PIPE REQ:`/`PIPE RSP:` verbatim → templates, probes and released passwords land in `%ProgramData%\MFS100CP\logs\bridge_svc_<date>.log` (directory created with default ACLs, so standard-user readability is **[Hypothesis]**, T-006); worker also logs `CMD: {line}`. | E1 | **Crit** | AC-23 |
| **F-22** | `capture`, `status`, `has_vault_credential`, and the `default:` branch (which forwards `match`/`init`/`uninit` to the worker) perform **no caller authorization**. | E1 | **Crit** | AC-17 |
| F-23 | Vault uses DPAPI `LocalMachine` scope + static entropy: any SYSTEM/administrator process can decrypt; README states reversible software encryption of passwords is "strictly prohibited". | E1 | **Crit** | R2.2 |
| F-24 | Probes are caller-supplied templates, not bound to a live capture session → any logged/leaked probe is replayable. | E1 | High | AC-18 |
| F-25 | Repo hygiene: vendor SDK and built binaries committed to a public repo (redistribution/licensing unchecked), `.vmx` embeds a host path/username, docs embed user SIDs and device serial, README links a non-existent file, no solution file/CI, no isolated unit-test project (tests are console commands). | E1 | Med | AC-30 |
| F-26 | Accuracy evidence is thin (6.4). | E1 (docs) | Med | AC-27 |

### 6.3 SDK surface actually present (E1: PE export tables and `SdkManager.cs`)

| Capability needed | Present in SDK? | Evidence | Open point |
|---|---|---|---|
| Raw frame capture | `MFS100GetFrame`, `MFS100GetWidth/Height`, `MFS100StartXcan/StopXcan` exported by `MFS100Dll.dll` | E1 | Signature/semantics **[Blocked]** — no headers/docs in repo; get from Mantra SDK documentation |
| ISO/ANSI template from capture | `MFS100ExtractISOTemplate`, `MFS100ExtractANSITemplate`; managed `AutoCapture` returns both | E1 | — |
| Template matching | `MFS100MatchISO/ANSI` (managed `MatchISO(byte[], byte[], ref int)` used in `SdkManager`) ; `iengine_ansi_iso.dll`: `ISO_VerifyMatch(Ex)`, `ANSI_VerifyMatch(Ex)` | E1 | Score scale: code comment says "0–1200+", observed scores saturate at 10,000 (6 of 10 genuine pairs) — scale must be established |
| Template merge (multi-sample enrollment) | `ISO_MergeTemplates`, `ANSI_MergeTemplates` | E1 (export only) | Behaviour **[Hypothesis]** until tested (T-041) |
| Quality / NFIQ / liveness | `MFS100GetQuality`, `IWSQLIB_CalculateNFIQ`, `MFS100IsLive` | E1 | `IsLive` is a software heuristic per repo docs (E2); PAD performance **[Blocked]** |
| Licensing / device binding | `MFS100InitWithKey`, `IEngine_InitWithChallenge`, `IEngine_SetLicenseContent`, `IEngine_GetHwid` | E1 (export names) | **Risk:** SDK/engine may be license- or hardware-ID-bound; redistribution terms unknown (R-12) |
| x64 build | None | E1 | IPC split is mandatory |

### 6.4 Evidence ledger for the project's known observations

| # | Observation | Class | Notes / what would upgrade it |
|---|---|---|---|
| 1 | Windows detects `USB\VID_2C0F&PID_1005\8E46E90C49AC` | E2 | The ID is hard-coded in `RegisterWbfDevice.cpp` as a fallback, so repo agreement is not independent. Upgrade: `pnputil /enum-devices /connected` + `Get-PnpDeviceProperty` output stored as evidence. |
| 2 | Mantra Test Application captured a fingerprint in the VM | E2 | Upgrade: screenshot + hash, SDK/driver version, VM snapshot name. |
| 3 | USBPcap analyzer parsed 7,509 events; VID/PID display filter matched 0 packets | E2 (tool not located) | Interpretation **[Hypothesis]**: USBPcap packet headers carry bus/device **address**, not VID/PID; vendor/product IDs appear only in descriptor transfers captured *during enumeration*. A zero match therefore does not show absence of MFS100 traffic. Plan T-020: capture across a replug, resolve the device address from the captured `GET_DESCRIPTOR`, then filter by address/endpoint. |
| 4 | PowerShell evidence-helper self-tests passed in the guest | E2 (not located) | Upgrade: commit scripts + transcript hash. |
| 5 | Earlier genuine-vs-impostor tests "promising" | E2 | Per `docs/test_results.md`: 5 captures of one finger → 10 genuine pairs (all pairs from the same 5 captures, **not independent**); 5 captures × 3 captures of a second finger → 15 impostor pairs; 6 of 10 genuine scores are exactly 10,000 (saturation). 0/15 false accepts bounds FAR only to ≲ 20% at 95% confidence (rule of three: 3/15). **Not** an accuracy or security certification. |
| 6 | Native Hello acceptance / enrollment / sign-in not yet demonstrated | E1-consistent | No artifact in the repo shows `WbioSrvc` loading the adapters or any `WinBio*` call succeeding. |
| 7 | "13/13 boundary tests passed" (README, `phase2_results.md`) | E2 | Test code exists (E1, `Program.cs --boundary-test`); results not reproduced. Upgrade: re-run, store transcript. |
| 8 | "MFS100.sys is KMDF" vs "proprietary bulk driver, not WBDI" | E4 (contradiction) | T-021: inspect vendor INF/driver (`pnputil /enum-drivers`, INF text, `sigcheck`). |
| 9 | Sensor 316 × 354 px, "500 DPI" | resolution E2 (cited from `GetDeviceInfo`); DPI **E4** | Upgrade: SDK documentation. |
| 10 | "ESS must be disabled via `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard`" | **E4** | Microsoft documents a Settings toggle (App. D #4); no registry key is documented. Remove from repo docs. |
| 11 | Adapters "registered / configured WbioSrvc database" (last commit message) | E4 | No log or screenshot. |

---

## 7. Proposed architecture, component responsibilities and data flows

### 7.1 Architecture decisions A–F (from the design questionnaire), validated against evidence

| ID | Proposed direction | Label | What the evidence says | Decision gate |
|---|---|---|---|---|
| **A** | x64 sensor + engine adapters ↔ existing x86 worker via IPC; keep vendor driver first | **[Proposed]** for IPC split; **[Hypothesis: insufficient]** for the "adapters-only, vendor driver" variant | x64/x86 split is real (E1, F-18). But WBS enumerates through the biometric device interface and queries WBDI capabilities (App. D #1, #2) — a proprietary USB driver that does neither gives WBS nothing to build a unit from (F-03). | G1 |
| **B** | Feature extraction + matching through the SDK ISO/ANSI engine via bridge | **[Proposed]**; "100% compatibility" **[Hypothesis]** | Required exports exist (6.3, E1). Template compatibility enrollment→verification, merge behaviour, score scale and saturation are unproven (F-08, F-26). | M3, T-040…T-043 |
| **C** | Inbox storage adapter (`winbiostorageadapter.dll`) | **[Proposed]** | Microsoft's own examples pair vendor engine adapters with `winbiostorageadapter.dll` (App. D #3, #5) — **[Verified]** that the combination is a supported pattern. That an arbitrary vendor template blob round-trips through it, and that selecting it makes an unapproved sensor acceptable to Hello, are **not** established (the latter is explicitly *not* implied: D2). | M3 / G3 |
| **D** | Non-ESS configuration in the disposable VM | **[Verified]** that ESS fingerprint requires match-on-sensor + embedded Microsoft-issued certificate (App. D #4) → MFS100 can only ever be non-ESS. Mechanism to *get* non-ESS in the VM: documented Settings toggle; registry `DeviceGuard` claim is **E4** | The VM as committed has no vTPM/VBS entries, so ESS is probably already off — to be **observed** (event 1108: "System process" vs "Virtual Secure Mode"), not assumed. | AC-19 |
| **E** | Reproducible, reversible driver package with INF, test signing, WBDI config | **[Proposed]** | Microsoft documents: `Class=Biometric`; UMDF WBDI drivers must set `Exclusive=1`; plug-in config under `HKR,WinBio\Configurations\<n>`; DB-service keys; own GUIDs (App. D #5). Current INF deviates (F-04). Test-signed installs require Secure Boot **off** + `testsigning` (App. D #8) — the committed VM has Secure Boot **on**. | AC-01 |
| **F** | `TestWbfUnit.exe` diagnostic before end-to-end | **[Proposed]** | Existing tool is a stub (F-16). Passing it never proves Hello acceptance (stated in AC-28). | AC-28 |

### 7.2 Device-node strategy — the central design question

WBS builds a biometric unit only for hardware that exposes `GUID_DEVINTERFACE_BIOMETRIC_READER` (`{E2B5183A-…}`), opens a handle, queries capabilities over the WBDI control interface, reads the **device** registry key for adapter plug-ins, then applies service-level overrides (App. D #2). Three ways to satisfy that contract are on the table; they are tried **in cost order** and each has a falsifiable test.

| Path | Description | Keeps vendor driver? | Label | Cheapest falsifying test |
|---|---|---|---|---|
| **A1** | Current repo approach, corrected: vendor driver stays; extension INF/registry attaches adapters to the vendor device node; interface GUID fixed. | Yes | **[Hypothesis: will not enumerate]** — the vendor stack would have to answer WBDI IOCTLs | T-010: with correct GUID + proper INF, does event 1108 appear / does `WinBioEnumBiometricUnits` list a unit? Capture with ProcMon + `Microsoft-Windows-Biometrics` ETW. 1–2 days. |
| **A2a** | New **UMDF 2 WBDI front-end** on a *separate, root-enumerated software device*. It exposes the biometric interface, answers the mandatory WBDI IOCTLs, and forwards "capture" to the bridge. The vendor driver keeps owning the USB device. Interface state follows physical presence reported by the bridge (`AssignDeviceInterfaceState` on arrival/removal). | Yes | **[Hypothesis]** (feasible in principle; presence tracking and Hello's treatment of a software device are unproven) | T-011: minimal front-end returning canned WBDI attributes; does WBS create a unit? |
| **A2b** | UMDF **filter** added by an extension INF on the vendor device stack, handling WBDI IOCTLs. | Yes | **[Hypothesis]**, **[Blocked]** on vendor INF review (T-021) and on whether the vendor driver blocks a second opener | T-021 then T-012 |
| **B** | **Replace** the vendor driver with a UMDF + WinUSB WBDI driver that speaks the MFS100 USB protocol (Microsoft's recommended WBDI model, App. D #6, #7). | No (rollback restores it) | **[Blocked]** — needs protocol documentation or defensible capture evidence (T-020), SDK licensing/IP review (R-12), and an answer to whether the SDK engine can consume raw frames (T-043) | Only after A1/A2 fail and Mantra/legal clear it |

**Decision rule:** advance to Path A2 only if T-010 fails; advance to Path B only if A2 fails *and* protocol access is lawful. Whichever path wins, **G2 (Hello acceptance without Microsoft's signature) is evaluated before the engine/storage work is perfected**, because G2 can end the effort (Section 17).

### 7.3 Target logical architecture (Path A2a shown; A1/B swap the bottom-left box)

```
 Windows Settings (Hello)        Lock / Sign-in UI (LogonUI)
          │                                  │
          └───────────── NGC / Windows Hello components ───────────┐
                                 │  (credential release is OS-native; R2)
                    ┌────────────▼────────────┐
                    │  Windows Biometric Svc  │  WbioSrvc (x64, LocalSystem)
                    │  ┌─────────┐ ┌────────┐ │
                    │  │ Sensor  │ │ Engine │ │   Storage adapter (inbox)
                    │  │ adapter │ │ adapter│ │   winbiostorageadapter.dll
                    │  │(inbox   │ │ (ours, │ │   → WinBio database (OS-owned)
                    │  │ WBDI or │ │  x64)  │ │
                    │  │ ours)   │ └───┬────┘ │
                    │  └────┬────┘     │      │
                    └───────┼──────────┼──────┘
                  WBDI IOCTL│          │ authenticated IPC (v2, Appendix B)
                  ┌─────────▼───┐      │
                  │ UMDF2 WBDI  │      │
                  │ front-end   │──────┤  capture/frame, presence
                  │ (A2a) / WinUSB driver (B) / none (A1)
                  └─────────────┘      │
                                 ┌─────▼─────────────────────────────┐
                                 │ MFS100BridgeSvc (SYSTEM, x86)      │  authz: only WbioSrvc for biometric cmds;
                                 │  • session/request IDs, deadlines  │  no vault, no template store, no logging of payloads
                                 └─────┬─────────────────────────────┘
                                       │ stdin/stdout (or private pipe), framed
                                 ┌─────▼─────────────┐   ┌────────────────────┐
                                 │ MFS100Worker x86  │──▶│ MFS100Dll + iengine │──▶ MFS100 USB (vendor driver)
                                 └───────────────────┘   └────────────────────┘
```

### 7.4 Design rules (apply to all paths)
1. **Adapters are thin.** They implement the WBF contract, own per-pipeline state, and never contain SDK logic. *[Proposed]*
2. **All SDK calls happen in the worker.** Adapters exchange only framed, versioned, size-bounded messages. *[Proposed]*
3. **Hello-path templates live only in the WBF database** (storage adapter). The DPAPI template store and `CredentialVault` are not part of the Hello build. *[Proposed]*
4. **No payload logging** (frames, templates, probes) at any level; diagnostics use lengths, scores, codes, correlation IDs. *[Proposed]*
5. **Bounded everything:** timeouts on every wait, size limits on every buffer, no `INFINITE` waits in `WbioSrvc`. *[Proposed]*
6. **Fail toward PIN/password:** any adapter failure returns a defined WinBio error, never hangs the sign-in UI. *[Proposed]*
7. **Template format carries a version/magic;** unknown versions are rejected, not mis-matched. *[Proposed]*
8. **One threshold, one place,** set from the evaluation protocol (13.6). *[Proposed]*

---

## 8. Driver, WBF Sensor Adapter, Engine Adapter, Storage Adapter and IPC responsibilities

| Component | Must | Must not | Interfaces | Label |
|---|---|---|---|---|
| **Driver package** (INF + catalog + binaries) | Install via driver store; declare hardware/root ID; `Class=Biometric` for WBDI; `Exclusive=1` (UMDF WBDI); `WinBio\Configurations` plug-in keys; WinBio database-service keys with own GUID; `[SignatureAttributes]` listing every adapter DLL and any DLL they load (`WindowsHello=true`); uninstall cleanly | Copy files by script; write `DeviceClasses`/`Enum` keys manually; write policy keys | PnP, driver store | **[Proposed]**; keys per App. D #5; INF details to be diffed against the WDK `WudfBioUsbSample` INX (T-012) |
| **WBDI front-end / WinUSB driver** (A2a / B) | Expose `GUID_DEVINTERFACE_BIOMETRIC_READER`; implement **all mandatory WBDI IOCTLs** (list transcribed from the WDK headers in T-012, not restated here); serialize requests; cancel cleanly; report attributes honestly (no ESS/secure-sensor flags); handle power/PnP | Claim match-on-chip/secure properties; open the USB device if the vendor driver owns it (A2a) | WBS (IOCTL), bridge (IPC, A2a) | **[Hypothesis]** (A2a) / **[Blocked]** (B) |
| **Sensor adapter** (only if a custom one is used, i.e. A1) | Per-pipeline context; async capture via overlapped I/O; honest reject details; cancel without races; bounded waits | Use globals; block `WbioSrvc` threads indefinitely; parse JSON by substring; return a template as "raw" | `WINBIO_SENSOR_INTERFACE`, IPC v2 | **[Proposed]** (rewrite of F-11/F-12) |
| **Engine adapter** (x64) | `AcceptSampleData`: validate sample, request extraction from worker, hold feature set. `Create/Update/CommitEnrollment`: ≥ N consistent samples, merge, quality gate, duplicate check, call storage-adapter helper functions to persist. `Verify/IdentifyFeatureSet`: fetch candidate record(s) from storage, request match, apply the single threshold, return `Match`, `RejectDetail`. Version template blobs. | Keep enrollment only in memory; ignore `Identity`/`SubFactor`; use a private threshold; hold templates in plaintext logs | `WINBIO_ENGINE_INTERFACE`, `WbioStorage*` helper calls (names to be confirmed vs `winbio_adapter.h`, T-050), IPC v2 | **[Proposed]** (rewrite of F-07…F-10) |
| **Storage adapter** (inbox) | Platform provides: database file creation/ACL/protection, record add/query/delete, identity↔record mapping. Our config: DB GUID, registration. | Be replaced by our own store on the Hello path | Registry DB entries; inbox DLL | **[Verified]** pattern (App. D #3, #5); **[Hypothesis]** blob round-trip & size limits (T-050) |
| **Bridge service** | Authenticate callers (token user = SYSTEM *and* service SID/path of `WbioSrvc`; admin-only for diagnostics); framing, versioning, request IDs, deadlines; serialize worker access; **do not** kill the worker for an unrelated disconnect; payload-free logging; restart policy | Release secrets; accept client thresholds; expose `identify_user`/vault commands | IPC v2 (Appendix B) | **[Proposed]** |
| **Worker** | `init`, `capture` (image and/or template), `extract`, `match`, `merge` (if confirmed), `health`; quality/NFIQ/score outputs; hard timeouts | Print payloads; hold state across sessions | SDK | **[Proposed]**; `extract(frame)` and `merge` **[Hypothesis]** |
| **IPC** | Versioned, length-prefixed binary framing; max sizes; request/response IDs; cancel message; error taxonomy mapped to `WINBIO_E_*` | Hand-rolled JSON substring parsing; fixed 4 KB template buffers; per-call reconnect storms | Named pipe (ACL: SYSTEM + Administrators; per-command authz) | **[Proposed]** |
| **Diagnostics** (`TestWbfUnit` v2) | See 13.3 | Emit biometric payloads | WinBio client API, event log | **[Proposed]** |

---

## 9. Enrollment, verification, authentication and recovery flows

### 9.1 Native enrollment (target)
| Step | Actor | Action | Failure handling |
|---|---|---|---|
| 1 | User | Settings → Sign-in options → Fingerprint recognition → Set up. PIN is created/confirmed first (Microsoft behaviour). | PIN failures are Windows' |
| 2 | WBS | Opens pipeline for the MFS100 unit; engine `CreateEnrollment`. | Unit missing → option unavailable (AC-05 fail) |
| 3 | User | Touches sensor. | Timeout → `WINBIO_E_…` mapped from IPC `E_TIMEOUT`; wizard re-prompts |
| 4 | Sensor path | Capture → sample to engine (`AcceptSampleData`). | Quality below gate → reject detail (`WINBIO_FP_POOR_QUALITY` etc.) |
| 5 | Engine | Extract features via bridge; `UpdateEnrollment` returns "more data" until N consistent samples. | Inconsistent samples → reject; never enroll a mixture |
| 6 | Engine | `CheckForDuplicate`; `CommitEnrollment` → storage adapter record. | Duplicate → fail with documented code; storage failure → discard, no partial template |
| 7 | WBS/NGC | Associates the biometric gesture with the user's NGC container. | OS-owned |
| 8 | Evidence | Event log + `TestWbfUnit --list-enrollments` show a record **without** printing payloads. | — |

### 9.2 Verification / identification
1. Sign-in or Settings triggers `WinBioVerify/Identify`. 2. Sensor path captures a sample. 3. Engine requests extraction from the worker. 4. Engine loads the identity's record(s) from the storage adapter (never from an in-memory copy). 5. Worker returns a score. 6. Engine applies the **single** threshold and returns match/no-match with reject detail. 7. On match WBS tells NGC to release the key (OS-native, R2). 8. No payload is logged.

### 9.3 Authentication at sign-in / unlock
| Scenario | Expected | Notes |
|---|---|---|
| Lock → touch | Unlock | Needs bridge + worker warm or fast-start; adapter must not block LogonUI |
| Sign-out → touch | Sign-in | Same |
| Restart | Fingerprint offered once WBS + bridge are up | Bridge start type automatic with restart-on-failure; dependency/ordering **[Hypothesis]**, tested in AC-13 |
| Cold boot | Fingerprint offered or PIN/password-first, documented | After a cold boot Windows may require PIN/password first for some account types — **verify per account type**, do not assume (AC-14, AC-25) |
| Sensor/bridge down | PIN/password available | AC-10 |

### 9.4 Recovery ladder (documented in runbook, Section 19)
1. Choose PIN or password under *Sign-in options*. 2. PIN reset flow (Microsoft) if PIN fails; Microsoft's guidance for a non-working PIN is to remove and re-add it (App. D #4). 3. Remove fingerprint in Settings. 4. Safe Mode boot → uninstall driver package (`pnputil`). 5. WinRE offline registry repair. 6. VM snapshot revert. Each rung is tested (AC-16).

---

## 10. Biometric data lifecycle, template protection, service security and threat model

### 10.1 Data inventory, handling and retention
All biometric samples, templates, USB captures and evidence logs are **sensitive data**.

| Data | Where it may exist | Protection | Retention | Cleanup |
|---|---|---|---|---|
| Raw frame (316×354 8-bit ≈ 111,864 B) | Worker/bridge memory only, for the duration of one request | Never written to disk/log; zeroed after use | ≤ request lifetime | Process restart on failure |
| Minutiae template (probe) | Memory only | Same | ≤ request lifetime | Same |
| Enrolled template (Hello path) | **WBF database only** (inbox storage adapter) | OS-managed database; template blobs versioned | Until user removes fingerprint (Settings) or account deleted | Verified by AC-26 |
| Legacy DPAPI template store (`%ProgramData%\MFS100CP\templates`) | Standalone tool only; **disabled in the Hello build** | DPAPI LocalMachine + SID entropy (any admin/SYSTEM can decrypt — not a user-bound protection) | Test only | Wipe on VM revert; excluded from release |
| Windows password / PIN | **Nowhere** (REM-1) | — | — | Delete vault directory and code |
| USB captures (`.pcapng`) | Encrypted VM disk / evidence vault outside the repo | They may contain fingerprint image data and device serials: treat as biometric data; no commits to Git | Delete when the protocol question (T-020) is closed, or ≤ 30 days **[Proposed]** | Secure delete + snapshot prune |
| Evidence logs | `evidence/` outside repo; payload-free by construction (REM-2) | Hash-chained manifest | Per release | Review before sharing |
| Test-subject accuracy data | Pseudonymised, local, consent recorded | Access-controlled | Until report signed, then delete raw templates | Documented |

### 10.2 Assets, actors, trust boundaries
Assets: enrolled templates, NGC credential release decision, device availability, sign-in availability, evidence logs. Actors: standard local user, other local user, local administrator, malware in a user session, malware as SYSTEM, physical attacker with the scanner, physical attacker with the disk, remote network attacker (out of scope except RDP). Boundaries: `WbioSrvc` ↔ bridge pipe; bridge ↔ worker; user session ↔ SYSTEM service; disk at rest.

### 10.3 Threat model (STRIDE-style, mapped to requirements and tests)

| ID | Threat | Present state (finding) | Required mitigation | Test |
|---|---|---|---|---|
| TH-01 | **Spoofing of the biometric service**: any process can open the pipe and issue `capture`/`match`/`identify_user` (F-22, F-14) | Open to Authenticated Users; pipe squatting possible | Pipe ACL SYSTEM + Administrators; per-command authz; caller must be `WbioSrvc` (token user/service SID + image path + signature); `FILE_FLAG_FIRST_PIPE_INSTANCE` equivalent; ACL on *every* instance | AC-17 |
| TH-02 | **Credential theft** via vault/`identify_user` (F-20, F-23) | Plaintext password in reply, DPAPI-machine vault | Delete vault; no secrets in the product (REM-1) | AC-21 |
| TH-03 | **Information disclosure through logs** (F-21) | Templates, probes, passwords in log files | Payload-free logging; log review gate | AC-23 |
| TH-04 | **Replay** of a captured probe/sample (F-24) | Caller-supplied probe accepted | Probes only from the live capture bound to a session nonce; engine accepts samples only from the sensor path of the same pipeline; reject unsolicited probes | AC-18 |
| TH-05 | **Tampering** with stored templates | Legacy store: DPAPI; Hello DB: OS-owned | Rely on WBF database protections on Hello path; template blob has version + integrity field; mismatch → reject and force re-enroll | AC-18 |
| TH-06 | **Match oracle / hill-climbing** (unauthenticated `match`) | Possible today | Matching is only reachable from the engine adapter within a verification flow; rate/lockout rely on WBF/NGC lockout policy — **to be verified** | AC-18 |
| TH-07 | **Denial of service**: kill worker on disconnect, malformed JSON, oversized frames, hung SDK call (F-13, F-11) | Present | Framed protocol with caps; deadlines; no cross-client kill; watchdog | AC-10 |
| TH-08 | **Elevation via adapters in `WbioSrvc`** (code running as SYSTEM) | Adapters unsigned; global state | Signed binaries; memory-safe parsing; fuzz the framing and BIR/Template parsers; minimal DLL deps | Section 13.5 |
| TH-09 | **Presentation attack** (fake finger) | SDK `IsLive` heuristic only (E2) | PAD evaluation per ISO/IEC 30107-3 methodology before any claim; Microsoft requires anti-spoofing for fingerprint sensors (App. D #11). Until measured, **no spoof-resistance claim** | AC-27 |
| TH-10 | **Biometric downgrade of PIN/NGC** (e.g., fingerprint bypassing PIN lockout) | Unknown | NGC lockout semantics are Windows'; we add nothing; verify behaviour on N consecutive failures | AC-15, AC-24 |
| TH-11 | **Lockout**: scanner absent/stuck at sign-in | Bridge/worker dependency | Fail-open to PIN/password; adapter timeouts; recovery ladder | AC-10, AC-16 |
| TH-12 | **Offline disk attack** | VM disk | BitLocker/Device Encryption is outside this product; templates not stored by us on Hello path | AC-17 |
| TH-13 | **Supply chain**: vendor SDK/third-party binaries committed and unsigned (F-25, F-17) | Present | Hash-pinned SDK, SBOM, sign everything we ship, list loaded DLLs in INF signature attributes | AC-20, AC-30 |

### 10.4 Security remediation tasks (precede any real-finger / real-account testing)
T-001 delete vault + vault commands; T-002 remove password from `identify_user` and remove client thresholds; T-003 per-command authorization; T-004 payload-free logging + purge existing logs; T-005 pipe ACL on all instances + squatting guard; T-006 check log/vault directory ACLs (is `%ProgramData%\MFS100CP\logs` standard-user readable?); T-007 un-register `MFS100CP.dll` in the Hello test VM image.

---

## 11. Installation, signing, configuration, upgrades and rollback

### 11.1 Package (target layout) — **[Proposed]**
```
MFS100WH\
  mfs100wh.inf              (Class=Biometric for WBDI paths; extension INF only if A1/A2b proven)
  mfs100wh.cat              (signed; test cert in VM, production via Partner Center)
  MFS100EngineAdapter.dll   (x64, signed; Hello signature attribute)
  [MFS100WbdiFrontEnd.dll]  (UMDF2, A2a)  /  [MFS100SensorAdapter.dll] (A1 only)
  bridge\ MFS100BridgeSvc.exe, MFS100Worker.exe, MFS100Dll.dll, iengine_ansi_iso.dll, MANTRA.MFS100.dll  (x86; licensing per R-12)
  tools\ TestWbfUnit.exe, collect-evidence.ps1, rollback.ps1
  SBOM + SHA-256 manifest
```
INF requirements to be derived from Microsoft documentation and diffed against the WDK `WudfBioUsbSample` (T-012): class/GUID, `Exclusive`, `HKR,WinBio\Configurations`, database-service registry entries with an own GUID, feature score (to distinguish WBDI from legacy), `[SignatureAttributes]` for **every** adapter binary and any DLL they load (App. D #3, #5).

### 11.2 Signing
| Stage | Method | Constraint |
|---|---|---|
| Development (VM) | Locally generated test certificate; catalog with `inf2cat`; `signtool`; trust installed **only in the VM** | Microsoft's documented requirement for installing test-signed driver packages: Secure Boot **disabled** and `bcdedit /set testsigning on` (App. D #8). The committed VM has Secure Boot **on** → boot-config change required → snapshot first (Section 19). The Windows Hello signature is **absent** in this stage. |
| Pre-production | HLK fingerprint test suite + security review template + DevCenter submission to obtain the Hello signature | Microsoft's approval process, not a build option (App. D #3). Access eligibility for an individual developer: **open question OQ-3**. |
| Production | WHCP/attestation as required for the driver type; sign adapters for the shipped OS architecture | App. D #3, #6 |

**Explicitly prohibited:** forging or copying a Microsoft Hello signature, borrowing another vendor's certified identity/IDs, patching protected authentication binaries, disabling OS integrity features beyond the documented test-signing mode in the disposable VM.

### 11.3 Configuration
- Single config source: registry under the product key + INF-delivered values; threshold, timeouts, IPC limits, log level.
- Service: `MFS100BridgeSvc` Automatic start, failure actions (restart ×3), least privilege review (does it need SYSTEM?), dependency/ordering relative to `WbioSrvc` evaluated in AC-13/14.
- Policy keys: **none written by the product.** Any Group Policy/MDM biometric settings are applied by the tester through supported tooling and recorded.

### 11.4 Upgrades
Version in INF `DriverVer`, adapter file version, and template header. Rules: (1) template-format change ⇒ bump template version ⇒ engine refuses old blobs ⇒ user re-enrolls (documented); (2) driver upgrade via `pnputil /add-driver … /install`; (3) tested upgrade path N→N+1 and downgrade (AC-01); (4) service/worker upgraded atomically with adapters (version handshake rejects mismatches).

### 11.5 Rollback (details in Section 19)
Tier 1 UI: sign in with PIN/password, remove fingerprint. Tier 2: `pnputil /delete-driver <oemNN.inf> /uninstall /force`, restore vendor driver, compare registry exports to baseline. Tier 3: Safe Mode / WinRE repair. Tier 4: VMware snapshot revert. Baseline registry exports and the vendor driver package are captured **before** the first install.

### 11.6 Repository hygiene (REM-5)
Remove `vm/*.vmx` host paths/usernames, user SIDs, device serials; stop committing built binaries and vendor SDK files unless redistribution is confirmed (R-12); add `.gitattributes`, SBOM, CI that checks PE machine types and signatures; fix README reference to `implementation_plan.md`; reconcile contradictory docs (Appendix E).

---

## 12. Test environment and tools

### 12.1 Environment
| Item | Requirement | Current state |
|---|---|---|
| Hypervisor | VMware Workstation Pro, guest Windows 11 Pro x64, build 26300.9457 | E2 (user) |
| USB | EHCI passthrough of `vid:2c0f pid:1005` | E1 (vmx) |
| Firmware | UEFI; **Secure Boot must be turned off for test-signed drivers** (App. D #8) | Secure Boot **on** (E1) — change only after snapshot |
| vTPM | Needed for realistic NGC/PIN/Hello provisioning | **Not in committed vmx** (E1) → verify the running VM; add if absent |
| VBS / nested virtualization | Only needed to *observe* ESS-state behaviour (App. D #4); not required to run non-ESS | Not in committed vmx |
| Accounts | `TestAdmin` (local admin), `TestUser` (standard), plus one MSA and one Entra/domain account if available | E2 (runbook) |
| Snapshots | `S0_Clean_Baseline`, `S1_Tools`, `S2_PreDriver`, `S3_PostDriver`, `S4_PostEnroll`, … | Baseline named `Clean_Pre_CP_Baseline` in repo docs (E2) — **re-verify it has no credential provider registered** |
| Network | Host-only/NAT; no corporate network; no real accounts | E1 (NAT) |

### 12.2 Tool set
| Purpose | Tool |
|---|---|
| Build | VS (C++/.NET), WDK (UMDF2 + biometric samples), pinned versions in CI |
| Driver/INF | `inf2cat`, `signtool`, `pnputil`, `setupapi.dev.log`, WDK INF verifier, Driver Verifier |
| WBF diagnostics | `TestWbfUnit` v2, `wevtutil` for `Microsoft-Windows-Biometrics/Operational`, ETW via `logman`/WPR, Process Monitor |
| USB | USBPcap + Wireshark (evidence handled per 10.1) |
| Debug | WinDbg (user-mode attach to `svchost` hosting `WbioSrvc`), Application Verifier on the worker |
| Security | fuzzing harness for IPC framing/BIR parsing, `accesschk`/`icacls` for ACL checks, log-grep suite |
| Evidence | PowerShell helpers (existing, to be located) + manifest/hash chain |
| Regulatory-style testing (later) | HLK, only if Microsoft path is pursued |

---

## 13. Test strategy: unit, integration, system, reliability, negative, security and recovery

### 13.1 Test levels

| Level | Scope | Where it runs | Gate |
|---|---|---|---|
| Unit | IPC framing/parsing, BIR/template (de)serialization, state machines, error mapping, threshold logic — pure code, no device | Host build agent (no driver install) | CI, every commit |
| Component | Worker + SDK + real device: capture, extract, match, merge, health; bridge authz | VM with USB passthrough | M2 |
| Integration | WBS ↔ driver/adapters ↔ bridge ↔ worker via `TestWbfUnit` | VM | G1, M3 |
| System | Native Windows Hello UI, lock/sign-in, reboot/cold boot | VM | G2–G4 |
| Reliability | Fault injection, soak, lifecycle (sleep/unplug/RDP) | VM | M8 |
| Negative | Wrong finger, no finger, partial, bad frames, malformed IPC | VM | M3–M8 |
| Security | AuthZ, ACL, replay, tamper, fuzz, log scan, secret scan | VM + CI | M8, every release |
| Recovery | Every rung of the ladder (11.5/19) | VM | Before any driver change |

### 13.2 Test case catalog (abridged; each maps to ACs in Section 14)

| TC | Level | Description | AC |
|---|---|---|---|
| TC-001 | Integration | Install package → `pnputil /enum-drivers` → device started → uninstall → registry/file residue diff = ∅ | AC-01 |
| TC-002 | Integration | Cycle `WbioSrvc` 50× with adapters registered: no crash, no handle growth | AC-02 |
| TC-003 | Integration | `WinBioEnumBiometricUnits` + event 1108 content check + unplug/replug | AC-03 |
| TC-004 | Integration | 20 touches via `WinBioCaptureSample`/enroll capture; 3 no-touch timeouts; 3 cancels | AC-04 |
| TC-005 | System | Settings availability screenshot + UI-automation text capture | AC-05 |
| TC-006 | System | Native enrollment ×10; early-lift; swapped finger mid-enrollment; duplicate finger | AC-06 |
| TC-007 | System | Persistence across `WbioSrvc` restart, reboot, snapshot revert-to-post-enroll | AC-07 |
| TC-008 | System | Genuine verification scenarios, each ×3 (Settings, lock, sign-in) | AC-08 |
| TC-009 | Negative | Impostor finger, no finger, partial finger, oversized pressure, wet/dry finger | AC-09 |
| TC-010 | Reliability | Fault-injection matrix (worker kill, bridge stop, `WbioSrvc` stop, unplug, SDK hang) | AC-10 |
| TC-011–014 | System | Lock, sign-out, restart, cold boot per account type | AC-11…14, AC-25 |
| TC-015 | System | PIN lifecycle before/after enrollment and after uninstall | AC-15 |
| TC-016 | Recovery | Ladder tiers 1–4 | AC-16 |
| TC-017 | Security | ACL/authz tests: standard user vs admin vs service identity; pipe squatting | AC-17 |
| TC-018 | Security | Replay + tamper + fuzz | AC-18 |
| TC-019 | System | ESS-state observation, enumeration with ESS on/off | AC-19 |
| TC-020 | Build | Signature, catalog, INF lint, PE machine, dependency list | AC-20, AC-30 |
| TC-021 | Security | Static scan for credential APIs; IPC schema review; runtime procmon | AC-21, AC-22 |
| TC-022 | Security | Log/dump/ETW secret and template scan | AC-23 |
| TC-023 | System | MFA coverage study (13.7) | AC-24 |
| TC-024 | System | Remove fingerprint; verify record gone | AC-26 |
| TC-025 | Evaluation | Accuracy protocol (13.6) | AC-27 |
| TC-026 | Integration | `TestWbfUnit` v2 full run | AC-28 |
| TC-027 | Reliability | Sleep/resume, hot-unplug, FUS, RDP, concurrent sessions, `WbioSrvc` restart mid-enroll | AC-29 |

### 13.3 `TestWbfUnit.exe` v2 specification

Purpose: verify WBF-level behaviour **before** end-to-end Hello testing. **A passing run is never evidence of Hello acceptance**; the tool prints that sentence in its summary.

| Req | Behaviour |
|---|---|
| TW-01 | Enumerate units for *all* biometric types, print unit ID, pool type, device instance ID, description; flag whether the MFS100 instance ID is present. |
| TW-02 | Query `Microsoft-Windows-Biometrics/Operational` for the last N events (incl. 1108) and report unit-load status and isolation (System vs VSM) — no payloads. |
| TW-03 | Report device state: PnP problem code, driver provider/version, `WbioSrvc` state, bridge service state, worker health (IPC `health`). |
| TW-04 | Open sessions with documented-appropriate flags per operation (raw capture vs enrollment) — flag choice justified from `WinBioOpenSession` documentation (fixes F-16). |
| TW-05 | `capture-raw`: `WinBioCaptureSample`, print only length, format ID, quality, reject detail, elapsed ms. |
| TW-06 | `enroll`: `WinBioEnrollBegin` → `WinBioEnrollCapture` (loop) → `WinBioEnrollCommit` for a **dedicated test identity**; `--discard` option; duplicate check. |
| TW-07 | `verify` / `identify`: `WinBioVerify` / `WinBioIdentify` with match/no-match + reject detail. |
| TW-08 | `delete`: `WinBioDeleteTemplate` of the test identity; confirm count = 0. |
| TW-09 | `cancel`: issue `WinBioCancel` during pending capture; assert return time ≤ deadline. |
| TW-10 | Symbolic error names for `WINBIO_E_*`/`WINBIO_I_*`, exit codes per stage, JSON output file with schema version, timestamps, tool/OS build. |
| TW-11 | `--soak N`: repeated enroll/verify/delete cycles with handle/private-bytes sampling of the service host. |
| TW-12 | Never prints or stores biometric payloads. |

### 13.4 Reliability and soak
Fault matrix (TC-010): kill worker mid-capture; stop bridge; stop `WbioSrvc`; unplug mid-capture; simulate SDK hang (worker `Sleep`); malformed/oversized IPC; bridge restart mid-enrollment; low-memory. Expected for each: bounded wait, defined `WINBIO_E_*`, **PIN/password unaffected**, automatic recovery measured (NFR-REL-02). Soak: `TestWbfUnit --soak` with handle/memory trend; N chosen at M8 from the trend (NFR-REL-03).

### 13.5 Security and negative tests
- **AuthZ matrix:** {standard user, other standard user, admin, SYSTEM non-`WbioSrvc`, `WbioSrvc`} × {every IPC command}; expected allow/deny table checked in.
- **ACL tests:** `icacls`/`accesschk` on pipe, log dir, any product data dir; confirm no standard-user read of anything biometric.
- **Replay/tamper:** record a legitimate IPC exchange (in a test build with a flag that is *not* in release) and replay; flip bits/truncate/version-mismatch template blobs; wrong-SID record.
- **Fuzzing:** IPC framing parser and BIR/template parsers; campaign length by **coverage-plateau criterion** (stop only after ≥ 24 h *and* no new coverage for ≥ 4 h) — chosen because a fixed iteration count has no justification [Proposed].
- **Log/secret scan:** regex + entropy scan of logs, ETW traces, crash dumps, evidence folders, repo history for passwords, base64 blobs above a length cutoff, template magic bytes.
- **Dependency:** every DLL loaded into `WbioSrvc` is listed and signed (AC-20).

### 13.6 Biometric evaluation protocol (replaces arbitrary accuracy targets)
Principle: *report what was measured with its uncertainty; claim nothing beyond it.*
1. **Scope.** Algorithm-level (SDK matcher) and system-level (through WBF) are measured separately. Protocol follows the structure of ISO/IEC 19795-1 (performance testing methodology) — **exact conformance claims are not made**.
2. **Population/data.** Multiple volunteers with informed consent; ≥ 2 sessions on different days; all fingers or a declared subset; capture counts per finger declared up front; all raw data handled under 10.1.
3. **Genuine comparisons** are only *between different captures of the same finger*, counted with the number of **independent** captures (not pairs derived from the same few captures — the problem in the existing 5-capture set, 6.4 #5).
4. **Impostor comparisons** are cross-finger/cross-person; independence caveats are stated.
5. **Statistics.** Report FAR/FRR at the selected threshold with exact (Clopper–Pearson) 95% intervals; DET curve over the scale; score saturation (10,000) examined — if the scale clips, the threshold cannot be calibrated near the cap and the scale must be re-established (6.3).
6. **Why not set a FAR target now.** Microsoft's published fingerprint-sensor requirements (App. D #11) are far stricter than a hobbyist dataset can demonstrate. By the rule of three, showing FAR < 0.001% (1×10⁻⁵) at 95% confidence with **zero** observed false accepts needs ≈ 3 / 10⁻⁵ = **300,000 independent impostor comparisons**; cross-comparing n distinct fingers yields n(n−1)/2 pairs, so roughly n ≈ 775 distinct fingers (with the independence caveat). FRR with/without anti-spoofing has separate published limits. Therefore in the VM phase we **measure and report** and do **not** claim conformance; conformance evidence is a Microsoft-process activity (Section 17).
7. **Threshold selection.** Chosen from the DET at a stated operating point, recorded in one place (NFR-MNT-02), and re-justified whenever the engine, sensor firmware or template version changes. The three thresholds currently in code (500/500/1400, F-10) are all **unjustified** until this is done.
8. **PAD.** Separate protocol modelled on ISO/IEC 30107-3 (species of artefacts, bona-fide/attack presentation counts, APCER/BPCER). No spoof-resistance statement until measured.

### 13.7 MFA coverage study protocol (R4)
1. **Inventory** every path where Windows asks for a credential: lock screen, sign-in screen, post-boot first sign-in, fast user switching, UAC consent/credential prompts, Remote Desktop (NLA), `runas`/credential prompts in apps, network logon, "Sign-in options" alternates, PIN reset, local admin password reset, Safe Mode, WinRE/offline, Microsoft-account recovery, alternate credential providers (including the custom one, expected unregistered).
2. For each path run: *(a)* is a fingerprint offered? *(b)* can it be the **only** accepted factor? *(c)* can PIN/password still be used? *(d)* behaviour with the scanner absent.
3. Apply each candidate control (Hello for Business "Use biometrics"; biometric Group Policy; interactive-logon policy options) **one at a time** on a snapshot; record effect per path; revert via snapshot.
4. Classify per path: **Offered**, **Required**, **Bypassable (how)**, **Not applicable**. Publish the table with evidence IDs.
5. Wording rule: "biometric required" may be stated *only for the paths and account types where the row says Required*. Anything else is "offered".
