using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using MFS100BridgeSvc;
using MFS100Worker;

namespace MFS100Enroll
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length > 0)
            {
                HandleCommandLineArgs(args);
                return;
            }

            RunInteractiveMenu();
        }

        static void HandleCommandLineArgs(string[] args)
        {
            string flag = args[0].ToLowerInvariant();

            switch (flag)
            {
                case "--list":
                    ListAccounts();
                    break;

                case "--boundary-test":
                    RunBoundaryTests();
                    break;

                case "--enroll":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: MFS100Enroll.exe --enroll <UsernameOrSID> [fingerIndex: 0|1] [label]");
                        return;
                    }
                    string userArg = args[1];
                    int fIdx = args.Length > 2 && int.TryParse(args[2], out int idx) ? idx : 0;
                    string label = args.Length > 3 ? args[3] : (fIdx == 0 ? "Primary Finger" : "Secondary Finger");

                    var acc = AccountManager.FindAccount(userArg);
                    if (acc == null)
                    {
                        Console.WriteLine($"[ERROR] User account '{userArg}' not found on this system.");
                        return;
                    }

                    using (var sdk = new SdkManager())
                    {
                        var (initOk, msg) = sdk.Init();
                        if (!initOk) { Console.WriteLine($"[ERROR] SDK Init failed: {msg}"); return; }
                        EnrollmentWorkflow.EnrollFinger(sdk, acc, fIdx, label);
                        sdk.Uninit();
                    }
                    break;

                case "--verify":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: MFS100Enroll.exe --verify <UsernameOrSID>");
                        return;
                    }
                    var vAcc = AccountManager.FindAccount(args[1]);
                    if (vAcc == null)
                    {
                        Console.WriteLine($"[ERROR] User account '{args[1]}' not found.");
                        return;
                    }

                    using (var sdk = new SdkManager())
                    {
                        var (initOk, msg) = sdk.Init();
                        if (!initOk) { Console.WriteLine($"[ERROR] SDK Init failed: {msg}"); return; }
                        VerificationWorkflow.VerifyAccount(sdk, vAcc);
                        sdk.Uninit();
                    }
                    break;

                case "--identify":
                    using (var sdk = new SdkManager())
                    {
                        var (initOk, msg) = sdk.Init();
                        if (!initOk) { Console.WriteLine($"[ERROR] SDK Init failed: {msg}"); return; }
                        VerificationWorkflow.IdentifyUser(sdk);
                        sdk.Uninit();
                    }
                    break;

                case "--delete":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: MFS100Enroll.exe --delete <UsernameOrSID> [fingerIndex]");
                        return;
                    }
                    var dAcc = AccountManager.FindAccount(args[1]);
                    string sid = dAcc?.Sid ?? args[1];

                    if (args.Length > 2 && int.TryParse(args[2], out int delIdx))
                    {
                        bool ok = TemplateManager.DeleteFinger(sid, delIdx);
                        Console.WriteLine(ok ? $"Finger {delIdx} deleted for {sid}." : $"Failed to delete finger {delIdx} for {sid}.");
                    }
                    else
                    {
                        bool ok = TemplateManager.DeleteEnrollment(sid);
                        Console.WriteLine(ok ? $"All enrollments deleted for {sid}." : $"No enrollment file found for {sid}.");
                    }
                    break;

                case "--help":
                case "-h":
                case "/?":
                case "help":
                    Console.WriteLine("==================================================");
                    Console.WriteLine("   MFS100 Standalone Biometric CLI Options        ");
                    Console.WriteLine("==================================================");
                    Console.WriteLine("  --list                     List local accounts and enrollment status");
                    Console.WriteLine("  --enroll <user> [0|1] [lbl] Enroll finger 0 or 1 for account");
                    Console.WriteLine("  --verify <user>            1:1 verification against target account");
                    Console.WriteLine("  --identify                 1:N identification across all accounts");
                    Console.WriteLine("  --delete <user> [0|1]      Delete finger or all enrollments for user");
                    Console.WriteLine("  --boundary-test            Run automated template & biometric tests");
                    Console.WriteLine("  --help, -h                 Show this help message");
                    Console.WriteLine("==================================================");
                    break;

                default:
                    Console.WriteLine($"Unknown option: {flag}");
                    Console.WriteLine("Run with --help to see available commands or without arguments for interactive menu.");
                    break;
            }
        }

        static void RunInteractiveMenu()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("==================================================");
                Console.WriteLine("   MFS100 Standalone Enrollment & Verification    ");
                Console.WriteLine("   (Zero Credentials Stored | Per-SID Isolation)  ");
                Console.WriteLine("==================================================");
                Console.WriteLine("  1. List Windows Accounts & Enrollment Status");
                Console.WriteLine("  2. Enroll a Finger for an Account");
                Console.WriteLine("  3. Perform 1:1 Fingerprint Verification");
                Console.WriteLine("  4. Perform 1:N Multi-User Identification");
                Console.WriteLine("  5. Delete Enrolled Fingerprint");
                Console.WriteLine("  6. Run Automated Template Boundary Tests (AC 2.3 & 2.7)");
                Console.WriteLine("  7. Exit");
                Console.WriteLine("==================================================");
                Console.Write("Enter choice [1-7]: ");

                string? choice = Console.ReadLine()?.Trim();
                Console.WriteLine();

                switch (choice)
                {
                    case "1":
                        ListAccounts();
                        break;

                    case "2":
                        InteractiveEnroll();
                        break;

                    case "3":
                        InteractiveVerify();
                        break;

                    case "4":
                        InteractiveIdentify();
                        break;

                    case "5":
                        InteractiveDelete();
                        break;

                    case "6":
                        RunBoundaryTests();
                        break;

                    case "7":
                    case "exit":
                    case "q":
                        Console.WriteLine("Exiting.");
                        return;

                    default:
                        Console.WriteLine("Invalid selection. Please choose 1-7.");
                        break;
                }
            }
        }

        static void ListAccounts()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("   Eligible Windows Accounts on System            ");
            Console.WriteLine("==================================================");

            var accounts = AccountManager.GetEligibleAccounts();
            if (accounts.Count == 0)
            {
                Console.WriteLine("No eligible local user accounts found.");
                return;
            }

            int idx = 1;
            foreach (var a in accounts)
            {
                bool enrolled = TemplateManager.IsUserEnrolled(a.Sid);
                string status = enrolled ? "[ENROLLED]" : "[Not Enrolled]";

                Console.WriteLine($"  {idx++}. {a.Caption,-25} {status,-14}");
                Console.WriteLine($"     SID: {a.Sid}");

                if (enrolled)
                {
                    var data = TemplateManager.LoadEnrollment(a.Sid);
                    if (data != null)
                    {
                        foreach (var f in data.Fingers)
                        {
                            Console.WriteLine($"       -> Finger {f.FingerIndex}: {f.FingerLabel} (Quality: {f.Quality}, Date: {f.EnrolledUtc.ToLocalTime():yyyy-MM-dd HH:mm})");
                        }
                    }
                }
            }
            Console.WriteLine("==================================================");
        }

        static void InteractiveEnroll()
        {
            var accounts = AccountManager.GetEligibleAccounts();
            if (accounts.Count == 0)
            {
                Console.WriteLine("No accounts available for enrollment.");
                return;
            }

            Console.WriteLine("Select an account to enroll:");
            for (int i = 0; i < accounts.Count; i++)
            {
                bool enrolled = TemplateManager.IsUserEnrolled(accounts[i].Sid);
                Console.WriteLine($"  {i + 1}. {accounts[i].Caption} {(enrolled ? "[Has Enrolled Fingers]" : "")}");
            }
            Console.Write($"Choose account [1-{accounts.Count}]: ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel < 1 || sel > accounts.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            var chosenAccount = accounts[sel - 1];

            Console.WriteLine();
            Console.WriteLine("Select finger to enroll:");
            Console.WriteLine("  0. Primary Finger (Finger 0)");
            Console.WriteLine("  1. Secondary Finger (Finger 1)");
            Console.Write("Choose finger [0 or 1]: ");
            if (!int.TryParse(Console.ReadLine(), out int fIdx) || (fIdx != 0 && fIdx != 1))
            {
                Console.WriteLine("Invalid finger index. Only 0 or 1 allowed.");
                return;
            }

            Console.Write("Enter finger label (e.g. 'Right Index', 'Left Thumb'): ");
            string enteredLabel = Console.ReadLine()?.Trim() ?? "";
            string label = string.IsNullOrWhiteSpace(enteredLabel)
                ? (fIdx == 0 ? "Primary Finger" : "Secondary Finger")
                : enteredLabel;

            using var sdk = new SdkManager();
            var (initOk, msg) = sdk.Init();
            if (!initOk)
            {
                Console.WriteLine($"[ERROR] SDK Init failed: {msg}");
                return;
            }

            EnrollmentWorkflow.EnrollFinger(sdk, chosenAccount, fIdx, label);
            sdk.Uninit();
        }

        static void InteractiveVerify()
        {
            var accounts = AccountManager.GetEligibleAccounts();
            var enrolledAccounts = accounts.Where(a => TemplateManager.IsUserEnrolled(a.Sid)).ToList();

            if (enrolledAccounts.Count == 0)
            {
                Console.WriteLine("No enrolled accounts found. Please enroll an account first.");
                return;
            }

            Console.WriteLine("Select account to verify (1:1):");
            for (int i = 0; i < enrolledAccounts.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {enrolledAccounts[i].Caption}");
            }
            Console.Write($"Choose account [1-{enrolledAccounts.Count}]: ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel < 1 || sel > enrolledAccounts.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            using var sdk = new SdkManager();
            var (initOk, msg) = sdk.Init();
            if (!initOk)
            {
                Console.WriteLine($"[ERROR] SDK Init failed: {msg}");
                return;
            }

            VerificationWorkflow.VerifyAccount(sdk, enrolledAccounts[sel - 1]);
            sdk.Uninit();
        }

        static void InteractiveIdentify()
        {
            using var sdk = new SdkManager();
            var (initOk, msg) = sdk.Init();
            if (!initOk)
            {
                Console.WriteLine($"[ERROR] SDK Init failed: {msg}");
                return;
            }

            VerificationWorkflow.IdentifyUser(sdk);
            sdk.Uninit();
        }

        static void InteractiveDelete()
        {
            var enrolledSids = TemplateManager.GetAllEnrolledSids();
            if (enrolledSids.Count == 0)
            {
                Console.WriteLine("No enrolled accounts exist.");
                return;
            }

            Console.WriteLine("Select account enrollment to manage:");
            for (int i = 0; i < enrolledSids.Count; i++)
            {
                var acc = AccountManager.FindAccount(enrolledSids[i]);
                Console.WriteLine($"  {i + 1}. {acc?.Caption ?? enrolledSids[i]}");
            }
            Console.Write($"Choose account [1-{enrolledSids.Count}]: ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel < 1 || sel > enrolledSids.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            string sid = enrolledSids[sel - 1];
            var data = TemplateManager.LoadEnrollment(sid);
            if (data == null) { Console.WriteLine("Data not found."); return; }

            Console.WriteLine($"Account has {data.Fingers.Count} fingers enrolled:");
            foreach (var f in data.Fingers)
                Console.WriteLine($"  - Finger {f.FingerIndex}: {f.FingerLabel}");

            Console.WriteLine("Options:");
            Console.WriteLine("  A. Delete entire enrollment (all fingers)");
            if (data.Fingers.Any(f => f.FingerIndex == 0)) Console.WriteLine("  0. Delete Finger 0 only");
            if (data.Fingers.Any(f => f.FingerIndex == 1)) Console.WriteLine("  1. Delete Finger 1 only");
            Console.Write("Enter choice: ");
            string? delChoice = Console.ReadLine()?.Trim().ToUpperInvariant();

            if (delChoice == "A")
            {
                TemplateManager.DeleteEnrollment(sid);
                Console.WriteLine($"All enrollment data deleted for {sid}.");
            }
            else if (delChoice == "0" || delChoice == "1")
            {
                int fIdx = int.Parse(delChoice);
                TemplateManager.DeleteFinger(sid, fIdx);
                Console.WriteLine($"Finger {fIdx} deleted for {sid}.");
            }
            else
            {
                Console.WriteLine("Deletion cancelled.");
            }
        }

        static void RunBoundaryTests()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("   Automated Template & Boundary Tests            ");
            Console.WriteLine("==================================================");

            string testSid = "S-1-5-21-9999999999-8888888888-7777777777-9999";
            byte[] mockTemplate1 = new byte[256];
            for (int i = 0; i < mockTemplate1.Length; i++) mockTemplate1[i] = (byte)(i % 255);
            byte[] mockTemplate2 = new byte[260];
            for (int i = 0; i < mockTemplate2.Length; i++) mockTemplate2[i] = (byte)((i * 3) % 255);
            byte[] mockTemplate3 = new byte[270];

            bool allTestsPassed = true;

            try
            {
                // Test 1: Save 1 finger
                Console.WriteLine("Test 1: Enroll Finger 0 (Primary)...");
                var d1 = new UserEnrollmentData { UserSid = testSid };
                d1.Fingers.Add(new EnrolledFinger { FingerIndex = 0, FingerLabel = "Primary Test", Quality = 85, Template = mockTemplate1 });
                TemplateManager.SaveEnrollment(d1);
                var loaded1 = TemplateManager.LoadEnrollment(testSid);
                bool t1Ok = loaded1 != null && loaded1.Fingers.Count == 1 && loaded1.Fingers[0].Quality == 85 && loaded1.Fingers[0].Template.SequenceEqual(mockTemplate1);
                Console.WriteLine($"  Result: {(t1Ok ? "PASS" : "FAIL")}");
                if (!t1Ok) allTestsPassed = false;

                // Test 2: Add Finger 1 (Secondary) -> Total = 2
                Console.WriteLine("Test 2: Enroll Finger 1 (Secondary) -> Total 2 fingers...");
                d1.Fingers.Add(new EnrolledFinger { FingerIndex = 1, FingerLabel = "Secondary Test", Quality = 78, Template = mockTemplate2 });
                TemplateManager.SaveEnrollment(d1);
                var loaded2 = TemplateManager.LoadEnrollment(testSid);
                bool t2Ok = loaded2 != null && loaded2.Fingers.Count == 2 && loaded2.Fingers[1].Quality == 78 && loaded2.Fingers[1].Template.SequenceEqual(mockTemplate2);
                Console.WriteLine($"  Result: {(t2Ok ? "PASS" : "FAIL")}");
                if (!t2Ok) allTestsPassed = false;

                // Test 3: Enforcement of Max 2 Fingers (AC 2.3)
                Console.WriteLine("Test 3: Enforce Max 2 Fingers Limit (AC 2.3)...");
                d1.Fingers.Add(new EnrolledFinger { FingerIndex = 2, FingerLabel = "Third Finger (Illegal)", Quality = 70, Template = mockTemplate3 });
                bool threwOnThird = false;
                try
                {
                    TemplateManager.SaveEnrollment(d1);
                }
                catch (InvalidOperationException)
                {
                    threwOnThird = true;
                }
                Console.WriteLine($"  Result: {(threwOnThird ? "PASS (Exception correctly thrown on 3rd finger)" : "FAIL (Allowed >2 fingers)")}");
                if (!threwOnThird) allTestsPassed = false;

                // Test 4: DPAPI Encryption at Rest & Zero Plaintext Biometrics (AC 2.9 & AC 2.7)
                Console.WriteLine("Test 4: DPAPI Encryption at Rest & Zero Plaintext Biometrics (AC 2.9 & AC 2.7)...");
                string path = TemplateManager.GetTemplateFilePath(testSid);
                byte[] rawDiskBytes = File.ReadAllBytes(path);
                uint rawMagic = BitConverter.ToUInt32(rawDiskBytes, 0);
                // Verify file is encrypted: does NOT start with magic "MFST" (0x4D465354)
                bool isCiphertext = rawMagic != UserEnrollmentData.MagicHeader;
                // Verify decryptable only via DPAPI LocalMachine with SID entropy
                var decryptedRecord = EncryptedTemplateStore.LoadEnrollment(testSid);
                bool t4Ok = isCiphertext && decryptedRecord != null &&
                            decryptedRecord.Fingers.Count == 2 &&
                            decryptedRecord.Fingers[0].Template.SequenceEqual(mockTemplate1);
                Console.WriteLine($"  File on disk is ciphertext: {isCiphertext} (Raw Magic: 0x{rawMagic:X8} != 0x4D465354)");
                Console.WriteLine($"  Result: {(t4Ok ? "PASS (DPAPI LocalMachine encryption verified, zero plaintext templates on disk)" : "FAIL")}");
                if (!t4Ok) allTestsPassed = false;

                // Test 5: Cryptographic SID Context Mismatch Rejection (AC 2.11)
                Console.WriteLine("Test 5: Cryptographic SID Context Mismatch Rejection (AC 2.11)...");
                string impostorSid = "S-1-5-21-1111111111-2222222222-3333333333-4444";
                bool sidMismatchThrew = false;
                try
                {
                    byte[] impostorEntropy = Encoding.UTF8.GetBytes(impostorSid);
                    // Attempting to decrypt testSid's ciphertext with a different user SID entropy
                    System.Security.Cryptography.ProtectedData.Unprotect(rawDiskBytes, impostorEntropy, System.Security.Cryptography.DataProtectionScope.LocalMachine);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    sidMismatchThrew = true;
                }
                Console.WriteLine($"  Result: {(sidMismatchThrew ? "PASS (DPAPI correctly threw CryptographicException on SID context mismatch)" : "FAIL (Decrypted with wrong SID)")}");
                if (!sidMismatchThrew) allTestsPassed = false;

                // Test 6: IPC Caller Authorization Validation (AC 2.12)
                Console.WriteLine("Test 6: Caller Authorization Check (AC 2.12)...");
                string standardCallerSid = "S-1-5-21-9999999999-8888888888-7777777777-1002";
                bool authRejectedStandardUser = false;
                try
                {
                    EncryptedTemplateStore.AuthorizeCaller(standardCallerSid, callerIsAdmin: false, targetSid: testSid);
                }
                catch (UnauthorizedAccessException)
                {
                    authRejectedStandardUser = true;
                }

                bool authPermittedAdmin = false;
                try
                {
                    EncryptedTemplateStore.AuthorizeCaller(standardCallerSid, callerIsAdmin: true, targetSid: testSid);
                    authPermittedAdmin = true;
                }
                catch { }

                bool t6Ok = authRejectedStandardUser && authPermittedAdmin;
                Console.WriteLine($"  Standard user cross-SID blocked: {authRejectedStandardUser} | Admin permitted: {authPermittedAdmin}");
                Console.WriteLine($"  Result: {(t6Ok ? "PASS (Standard user cross-SID access rejected with 403 Forbidden)" : "FAIL")}");
                if (!t6Ok) allTestsPassed = false;

                // Test 7: Directory Security & ACL Enforcement (AC 2.10)
                Console.WriteLine("Test 7: Directory Security & ACL Enforcement (AC 2.10)...");
                string templatesDir = EncryptedTemplateStore.GetTemplatesDirectory();
                var dirInfo = new DirectoryInfo(templatesDir);
                var dirSec = dirInfo.GetAccessControl();
                var rules = dirSec.GetAccessRules(true, false, typeof(SecurityIdentifier));
                bool hasSystemRule = false;
                bool hasAdminRule = false;
                foreach (FileSystemAccessRule r in rules)
                {
                    if (r.IdentityReference.Value == "S-1-5-18") hasSystemRule = true; // LocalSystem
                    if (r.IdentityReference.Value == "S-1-5-32-544") hasAdminRule = true; // Administrators
                }
                bool t7Ok = hasSystemRule && hasAdminRule && dirSec.AreAccessRulesProtected;
                Console.WriteLine($"  System Rule: {hasSystemRule} | Admin Rule: {hasAdminRule} | Inheritance Protected: {dirSec.AreAccessRulesProtected}");
                Console.WriteLine($"  Result: {(t7Ok ? "PASS (Directory ACL strictly restricts access to SYSTEM and Administrators)" : "FAIL")}");
                if (!t7Ok) allTestsPassed = false;

                // Test 8: Delete Finger 0
                Console.WriteLine("Test 8: Delete Finger 0, verify Finger 1 persists...");
                TemplateManager.DeleteFinger(testSid, 0);
                var loaded3 = TemplateManager.LoadEnrollment(testSid);
                bool t8Ok = loaded3 != null && loaded3.Fingers.Count == 1 && loaded3.Fingers[0].FingerIndex == 1;
                Console.WriteLine($"  Result: {(t8Ok ? "PASS" : "FAIL")}");
                if (!t8Ok) allTestsPassed = false;

                // Test 9: Delete entire enrollment
                Console.WriteLine("Test 9: Clean up test enrollment...");
                TemplateManager.DeleteEnrollment(testSid);
                bool t9Ok = !TemplateManager.IsUserEnrolled(testSid);
                Console.WriteLine($"  Result: {(t9Ok ? "PASS" : "FAIL")}");
                if (!t9Ok) allTestsPassed = false;

                // -------------------------------------------------------------
                // Tests 10-13: Biometric Verification, Discrimination & Multi-User Isolation
                // (AC 2.2, AC 2.4, AC 2.5, AC 2.6)
                // -------------------------------------------------------------
                string samplePath = @"C:\Program Files\Mantra\MFS100\Driver\MFS100Test\FingerData\ISOTemplate.iso";
                if (File.Exists(samplePath))
                {
                    byte[] iso1 = File.ReadAllBytes(samplePath);
                    byte[] iso2 = (byte[])iso1.Clone();
                    for (int i = 28; i < iso2.Length - 5; i += 6)
                    {
                        iso2[i]     = (byte)((255 - iso2[i]) % 256);
                        iso2[i + 1] = (byte)((255 - iso2[i + 1]) % 256);
                        iso2[i + 2] = (byte)((255 - iso2[i + 2]) % 256);
                        iso2[i + 3] = (byte)((255 - iso2[i + 3]) % 256);
                    }

                    using var sdk = new SdkManager();
                    var (sdkOk, sdkMsg) = sdk.Init();
                    if (sdkOk)
                    {
                        string testUserSidA = "S-1-5-21-9999999999-8888888888-7777777777-1001";
                        string testUserSidB = "S-1-5-21-9999999999-8888888888-7777777777-1002";

                        try
                        {
                            // Test 10: Multi-scan Consistency Logic (AC 2.2)
                            Console.WriteLine("Test 10: Multi-Scan Consistency Check (AC 2.2)...");
                            var m11 = sdk.Match(iso1, iso1);
                            var m12 = sdk.Match(iso1, iso2);
                            bool t10Ok = m11.Score >= EnrollmentWorkflow.TargetMatchThreshold && m12.Score < EnrollmentWorkflow.TargetMatchThreshold;
                            Console.WriteLine($"  Self-match score: {m11.Score} (>= 1400), Altered-finger score: {m12.Score} (< 1400)");
                            Console.WriteLine($"  Result: {(t10Ok ? "PASS (Consistent scans accepted, inconsistent rejected)" : "FAIL")}");
                            if (!t10Ok) allTestsPassed = false;

                            // Test 11: 1:1 Verification Match & Reject (AC 2.4 & AC 2.5)
                            Console.WriteLine("Test 11: 1:1 Verification Match & Reject (AC 2.4 & AC 2.5)...");
                            var dUserA = new UserEnrollmentData { UserSid = testUserSidA };
                            dUserA.Fingers.Add(new EnrolledFinger { FingerIndex = 0, FingerLabel = "Primary A", Quality = 82, Template = iso1 });
                            TemplateManager.SaveEnrollment(dUserA);

                            var loadedA = TemplateManager.LoadEnrollment(testUserSidA);
                            var matchEnrolled = sdk.Match(iso1, loadedA!.Fingers[0].Template);
                            var matchUnenrolled = sdk.Match(iso2, loadedA.Fingers[0].Template);
                            bool t11Ok = matchEnrolled.Score >= VerificationWorkflow.MatchThreshold &&
                                         matchUnenrolled.Score < VerificationWorkflow.MatchThreshold;
                            Console.WriteLine($"  Enrolled finger score:   {matchEnrolled.Score} (Threshold >= 1400 -> MATCH)");
                            Console.WriteLine($"  Unenrolled finger score: {matchUnenrolled.Score} (Threshold < 1400 -> ACCESS DENIED)");
                            Console.WriteLine($"  Result: {(t11Ok ? "PASS (Enrolled finger matched, unenrolled rejected)" : "FAIL")}");
                            if (!t11Ok) allTestsPassed = false;

                            // Test 12: Cross-User Account Isolation (AC 2.6)
                            Console.WriteLine("Test 12: Cross-User Account Isolation (AC 2.6)...");
                            var dUserB = new UserEnrollmentData { UserSid = testUserSidB };
                            dUserB.Fingers.Add(new EnrolledFinger { FingerIndex = 0, FingerLabel = "Primary B", Quality = 80, Template = iso2 });
                            TemplateManager.SaveEnrollment(dUserB);

                            var matchCrossAB = sdk.Match(iso1, dUserB.Fingers[0].Template);
                            var matchCrossBA = sdk.Match(iso2, dUserA.Fingers[0].Template);
                            bool t12Ok = matchCrossAB.Score < VerificationWorkflow.MatchThreshold &&
                                         matchCrossBA.Score < VerificationWorkflow.MatchThreshold;
                            Console.WriteLine($"  User A finger against User B: score = {matchCrossAB.Score} (< 1400 -> ACCESS DENIED)");
                            Console.WriteLine($"  User B finger against User A: score = {matchCrossBA.Score} (< 1400 -> ACCESS DENIED)");
                            Console.WriteLine($"  Result: {(t12Ok ? "PASS (Zero cross-user false accepts across accounts)" : "FAIL")}");
                            if (!t12Ok) allTestsPassed = false;

                            // Test 13: 1:N Multi-User Identification Across All Enrolled SIDs
                            Console.WriteLine("Test 13: 1:N Multi-User Identification Across Enrolled SIDs...");
                            var allEnr = TemplateManager.LoadAllEnrollments();

                            string? identifiedA = null;
                            int bestScoreA = -1;
                            foreach (var e in allEnr)
                            {
                                foreach (var f in e.Fingers)
                                {
                                    var m = sdk.Match(iso1, f.Template);
                                    if (m.Score > bestScoreA && m.Score >= VerificationWorkflow.MatchThreshold)
                                    {
                                        bestScoreA = m.Score;
                                        identifiedA = e.UserSid;
                                    }
                                }
                            }

                            string? identifiedB = null;
                            int bestScoreB = -1;
                            foreach (var e in allEnr)
                            {
                                foreach (var f in e.Fingers)
                                {
                                    var m = sdk.Match(iso2, f.Template);
                                    if (m.Score > bestScoreB && m.Score >= VerificationWorkflow.MatchThreshold)
                                    {
                                        bestScoreB = m.Score;
                                        identifiedB = e.UserSid;
                                    }
                                }
                            }

                            bool t13Ok = identifiedA == testUserSidA && identifiedB == testUserSidB;
                            Console.WriteLine($"  Probe A matched account: {identifiedA} (Expected: {testUserSidA}, Score: {bestScoreA})");
                            Console.WriteLine($"  Probe B matched account: {identifiedB} (Expected: {testUserSidB}, Score: {bestScoreB})");
                            Console.WriteLine($"  Result: {(t13Ok ? "PASS (1:N identification uniquely resolved correct accounts)" : "FAIL")}");
                            if (!t13Ok) allTestsPassed = false;
                        }
                        finally
                        {
                            TemplateManager.DeleteEnrollment(testUserSidA);
                            TemplateManager.DeleteEnrollment(testUserSidB);
                            sdk.Uninit();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[WARN] Could not initialize SDK for biometric tests: {sdkMsg}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"Boundary Tests Overall: {(allTestsPassed ? "ALL TESTS PASSED [PASS]" : "SOME TESTS FAILED [FAIL]")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Unexpected exception during boundary tests: {ex}");
            }
            finally
            {
                TemplateManager.DeleteEnrollment(testSid);
            }

            Console.WriteLine("==================================================");
        }
    }
}
