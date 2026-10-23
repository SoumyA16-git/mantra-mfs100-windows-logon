using System;
using System.Collections.Generic;
using System.Linq;
using MFS100Worker;

namespace MFS100Enroll
{
    internal static class VerificationWorkflow
    {
        public const int MatchThreshold = 1400;

        public static bool VerifyAccount(SdkManager sdk, UserAccountInfo account)
        {
            var enrollment = TemplateManager.LoadEnrollment(account.Sid);
            if (enrollment == null || enrollment.Fingers.Count == 0)
            {
                Console.WriteLine($"[ERROR] No enrolled fingerprint templates found for {account.Caption} (SID: {account.Sid}).");
                return false;
            }

            Console.WriteLine();
            Console.WriteLine("==================================================");
            Console.WriteLine($"  1:1 Verification: {account.Caption}");
            Console.WriteLine($"  Enrolled fingers: {enrollment.Fingers.Count}");
            foreach (var f in enrollment.Fingers)
                Console.WriteLine($"    - Finger {f.FingerIndex}: {f.FingerLabel} (Quality: {f.Quality})");
            Console.WriteLine("==================================================");
            Console.Write("Place any enrolled finger on the scanner... [press Enter when ready] ");
            Console.ReadLine();

            CaptureResult cr = sdk.Capture(15000);
            if (!cr.Ok)
            {
                Console.WriteLine($"[FAIL] Live capture failed: code={cr.ErrorCode} ({cr.ErrorMsg})");
                return false;
            }

            Console.WriteLine($"Live scan captured: Quality = {cr.Quality}, NFIQ = {cr.Nfiq}, Size = {cr.IsoTemplate.Length} bytes");
            Console.WriteLine();

            if (TemplateManager.IsServiceAvailable())
            {
                Console.WriteLine("  [Broker: Matching via Privileged Service over Named Pipe]");
                var (ok, accessGranted, score, label) = TemplateManager.MatchUserViaService(account.Sid, cr.IsoTemplate, MatchThreshold);
                if (ok)
                {
                    Console.WriteLine();
                    if (accessGranted)
                    {
                        Console.WriteLine("==================================================");
                        Console.WriteLine("  VERIFICATION RESULT: [MATCH SUCCESS]            ");
                        Console.WriteLine($"  User:            {account.Caption}");
                        Console.WriteLine($"  SID:             {account.Sid}");
                        Console.WriteLine($"  Matched Finger:  {label ?? "Enrolled Finger"}");
                        Console.WriteLine($"  Match Score:     {score} (Threshold: {MatchThreshold})");
                        Console.WriteLine("==================================================");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("==================================================");
                        Console.WriteLine("  VERIFICATION RESULT: [ACCESS DENIED]            ");
                        Console.WriteLine($"  Target User:     {account.Caption}");
                        Console.WriteLine($"  Highest Score:   {score} (Threshold: {MatchThreshold})");
                        Console.WriteLine("  Fingerprint did not match any enrolled template.");
                        Console.WriteLine("==================================================");
                        return false;
                    }
                }
            }

            int bestScore = -1;
            EnrolledFinger? bestFinger = null;

            foreach (var f in enrollment.Fingers)
            {
                MatchResult mr = sdk.Match(cr.IsoTemplate, f.Template);
                Console.WriteLine($"  Matching against Finger {f.FingerIndex} ({f.FingerLabel}): Score = {mr.Score}");

                if (mr.Score > bestScore)
                {
                    bestScore = mr.Score;
                    bestFinger = f;
                }
            }

            Console.WriteLine();
            if (bestScore >= MatchThreshold)
            {
                Console.WriteLine("==================================================");
                Console.WriteLine("  VERIFICATION RESULT: [MATCH SUCCESS]            ");
                Console.WriteLine($"  User:            {account.Caption}");
                Console.WriteLine($"  SID:             {account.Sid}");
                Console.WriteLine($"  Matched Finger:  Finger {bestFinger?.FingerIndex} ({bestFinger?.FingerLabel})");
                Console.WriteLine($"  Match Score:     {bestScore} (Threshold: {MatchThreshold})");
                Console.WriteLine("==================================================");
                return true;
            }
            else
            {
                Console.WriteLine("==================================================");
                Console.WriteLine("  VERIFICATION RESULT: [ACCESS DENIED]            ");
                Console.WriteLine($"  Target User:     {account.Caption}");
                Console.WriteLine($"  Highest Score:   {bestScore} (Threshold: {MatchThreshold})");
                Console.WriteLine("  Fingerprint did not match any enrolled template.");
                Console.WriteLine("==================================================");
                return false;
            }
        }

        public static bool IdentifyUser(SdkManager sdk)
        {
            var allEnrollments = TemplateManager.LoadAllEnrollments();
            if (allEnrollments.Count == 0)
            {
                Console.WriteLine("[ERROR] No user accounts have enrolled fingerprints in the system.");
                return false;
            }

            Console.WriteLine();
            Console.WriteLine("==================================================");
            Console.WriteLine("  1:N Multi-User Identification                   ");
            Console.WriteLine($"  Evaluating across {allEnrollments.Count} enrolled user accounts");
            foreach (var e in allEnrollments)
            {
                var acc = AccountManager.FindAccount(e.UserSid);
                Console.WriteLine($"    - {acc?.Caption ?? e.UserSid} ({e.Fingers.Count} fingers enrolled)");
            }
            Console.WriteLine("==================================================");
            Console.Write("Place any enrolled finger on the scanner... [press Enter when ready] ");
            Console.ReadLine();

            CaptureResult cr = sdk.Capture(15000);
            if (!cr.Ok)
            {
                Console.WriteLine($"[FAIL] Live capture failed: code={cr.ErrorCode} ({cr.ErrorMsg})");
                return false;
            }

            Console.WriteLine($"Live scan captured: Quality = {cr.Quality}, NFIQ = {cr.Nfiq}, Size = {cr.IsoTemplate.Length} bytes");
            Console.WriteLine();

            if (TemplateManager.IsServiceAvailable())
            {
                Console.WriteLine("  [Broker: 1:N Identification via Privileged Service over Named Pipe]");
                var (ok, matched, userSid, score) = TemplateManager.IdentifyUserViaService(cr.IsoTemplate, MatchThreshold);
                if (ok)
                {
                    Console.WriteLine();
                    if (matched && userSid != null)
                    {
                        var matchedAcc = AccountManager.FindAccount(userSid);
                        Console.WriteLine("==================================================");
                        Console.WriteLine("  IDENTIFICATION RESULT: [USER RECOGNIZED]        ");
                        Console.WriteLine($"  Identified User: {matchedAcc?.Caption ?? userSid}");
                        Console.WriteLine($"  SID:             {userSid}");
                        Console.WriteLine($"  Match Score:     {score} (Threshold: {MatchThreshold})");
                        Console.WriteLine("==================================================");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("==================================================");
                        Console.WriteLine("  IDENTIFICATION RESULT: [UNKNOWN USER / REJECT]  ");
                        Console.WriteLine($"  Highest Score:   {score} (Threshold: {MatchThreshold})");
                        Console.WriteLine("  Fingerprint does not match any enrolled user.");
                        Console.WriteLine("==================================================");
                        return false;
                    }
                }
            }

            Console.WriteLine("--- Matching across all enrolled accounts ---");

            string? bestSid = null;
            EnrolledFinger? bestFinger = null;
            int highestScore = -1;

            foreach (var e in allEnrollments)
            {
                var acc = AccountManager.FindAccount(e.UserSid);
                string displayName = acc?.Caption ?? e.UserSid;

                foreach (var f in e.Fingers)
                {
                    MatchResult mr = sdk.Match(cr.IsoTemplate, f.Template);
                    Console.WriteLine($"  User: {displayName,-25} | Finger {f.FingerIndex} ({f.FingerLabel,-15}) | Score: {mr.Score}");

                    if (mr.Score > highestScore)
                    {
                        highestScore = mr.Score;
                        bestSid = e.UserSid;
                        bestFinger = f;
                    }
                }
            }

            Console.WriteLine();
            if (highestScore >= MatchThreshold && bestSid != null)
            {
                var matchedAcc = AccountManager.FindAccount(bestSid);
                Console.WriteLine("==================================================");
                Console.WriteLine("  IDENTIFICATION RESULT: [USER RECOGNIZED]        ");
                Console.WriteLine($"  Identified User: {matchedAcc?.Caption ?? bestSid}");
                Console.WriteLine($"  SID:             {bestSid}");
                Console.WriteLine($"  Matched Finger:  Finger {bestFinger?.FingerIndex} ({bestFinger?.FingerLabel})");
                Console.WriteLine($"  Match Score:     {highestScore} (Threshold: {MatchThreshold})");
                Console.WriteLine("==================================================");
                return true;
            }
            else
            {
                Console.WriteLine("==================================================");
                Console.WriteLine("  IDENTIFICATION RESULT: [UNKNOWN USER / REJECT]  ");
                Console.WriteLine($"  Highest Score:   {highestScore} (Threshold: {MatchThreshold})");
                Console.WriteLine("  Fingerprint does not match any enrolled user.");
                Console.WriteLine("==================================================");
                return false;
            }
        }
    }
}
