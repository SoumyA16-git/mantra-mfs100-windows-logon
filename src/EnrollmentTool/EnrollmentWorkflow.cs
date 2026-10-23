using System;
using System.Collections.Generic;
using System.Linq;
using MFS100Worker;

namespace MFS100Enroll
{
    internal static class EnrollmentWorkflow
    {
        public const int MinEnrollmentQuality = 60;
        public const int TargetMatchThreshold = 1400;

        public static bool EnrollFinger(SdkManager sdk, UserAccountInfo account, int fingerIndex, string label)
        {
            if (fingerIndex < 0 || fingerIndex >= UserEnrollmentData.MaxFingersPerUser)
            {
                Console.WriteLine($"[ERROR] Invalid finger index {fingerIndex}. Only fingers 0 (Primary) and 1 (Secondary) are supported.");
                return false;
            }

            Console.WriteLine();
            Console.WriteLine("==================================================");
            Console.WriteLine($"  Enrollment: {account.Caption}");
            Console.WriteLine($"  SID:        {account.Sid}");
            Console.WriteLine($"  Finger:     {(fingerIndex == 0 ? "Primary (0)" : "Secondary (1)")} - {label}");
            Console.WriteLine("==================================================");
            Console.WriteLine("To ensure enrollment accuracy, you will provide 3 consecutive scans.");
            Console.WriteLine("Each scan will be validated for quality (>= 60) and cross-checked for consistency.");
            Console.WriteLine("Remember to LIFT your finger completely between each scan.");
            Console.WriteLine();

            var enrollmentData = TemplateManager.LoadEnrollment(account.Sid) ?? new UserEnrollmentData
            {
                UserSid = account.Sid,
                EnrolledUtc = DateTime.UtcNow
            };

            // Check if user already has 2 fingers enrolled and this is a new index
            if (enrollmentData.Fingers.Count >= UserEnrollmentData.MaxFingersPerUser &&
                !enrollmentData.Fingers.Any(f => f.FingerIndex == fingerIndex))
            {
                Console.WriteLine($"[ERROR] User already has {UserEnrollmentData.MaxFingersPerUser} fingers enrolled.");
                Console.WriteLine("You must delete or replace an existing enrolled finger.");
                return false;
            }

            var capturedTemplates = new List<byte[]>();
            var qualities = new List<int>();

            for (int scan = 1; scan <= 3; scan++)
            {
                Console.WriteLine($"--- Scan {scan} of 3 ---");
                Console.WriteLine("  -> Clean platen, lift finger.");
                Console.Write($"  Place {label} on scanner when ready... [press Enter] ");
                Console.ReadLine();

                CaptureResult cr = sdk.Capture(15000);

                if (!cr.Ok)
                {
                    Console.WriteLine($"  [FAIL] Capture failed: code={cr.ErrorCode} ({cr.ErrorMsg})");
                    Console.WriteLine("Enrollment aborted. Please try again.");
                    return false;
                }

                if (cr.Quality < MinEnrollmentQuality)
                {
                    Console.WriteLine($"  [FAIL] Quality too low: {cr.Quality} (Minimum required: {MinEnrollmentQuality})");
                    Console.WriteLine("Please place finger firmly and flatly. Enrollment aborted.");
                    return false;
                }

                Console.WriteLine($"  [PASS] Scan {scan} captured: Quality={cr.Quality}, NFIQ={cr.Nfiq}, Size={cr.IsoTemplate.Length} bytes");
                capturedTemplates.Add((byte[])cr.IsoTemplate.Clone());
                qualities.Add(cr.Quality);
                Console.WriteLine();
            }

            // Consistency check: Verify all 3 scans match each other (score >= 1400)
            Console.WriteLine("Verifying multi-scan consistency...");
            MatchResult m12 = sdk.Match(capturedTemplates[0], capturedTemplates[1]);
            MatchResult m23 = sdk.Match(capturedTemplates[1], capturedTemplates[2]);
            MatchResult m13 = sdk.Match(capturedTemplates[0], capturedTemplates[2]);

            Console.WriteLine($"  Scan 1 vs 2 score: {m12.Score}");
            Console.WriteLine($"  Scan 2 vs 3 score: {m23.Score}");
            Console.WriteLine($"  Scan 1 vs 3 score: {m13.Score}");

            if (m12.Score < TargetMatchThreshold || m23.Score < TargetMatchThreshold || m13.Score < TargetMatchThreshold)
            {
                Console.WriteLine("  [FAIL] Inconsistent scans! The 3 captures did not match each other above threshold 1400.");
                Console.WriteLine("  Ensure the exact same finger is placed for all 3 scans. Enrollment rejected.");
                return false;
            }

            Console.WriteLine("  [PASS] Consistency verified across all 3 scans.");

            // Choose the highest-quality scan
            int bestIdx = 0;
            int maxQ = -1;
            for (int i = 0; i < qualities.Count; i++)
            {
                if (qualities[i] > maxQ)
                {
                    maxQ = qualities[i];
                    bestIdx = i;
                }
            }

            var chosenTemplate = capturedTemplates[bestIdx];
            var enrolledFinger = new EnrolledFinger
            {
                FingerIndex = fingerIndex,
                FingerLabel = label,
                Quality = maxQ,
                Template = chosenTemplate,
                EnrolledUtc = DateTime.UtcNow
            };

            // Update or add finger record
            enrollmentData.Fingers.RemoveAll(f => f.FingerIndex == fingerIndex);
            enrollmentData.Fingers.Add(enrolledFinger);
            enrollmentData.Fingers = enrollmentData.Fingers.OrderBy(f => f.FingerIndex).ToList();
            enrollmentData.EnrolledUtc = DateTime.UtcNow;

            TemplateManager.SaveEnrollment(enrollmentData);

            Console.WriteLine();
            Console.WriteLine("==================================================");
            Console.WriteLine("  ENROLLMENT SUCCESSFUL                           ");
            Console.WriteLine($"  Account:      {account.Caption}");
            Console.WriteLine($"  SID:          {account.Sid}");
            Console.WriteLine($"  Finger Index: {fingerIndex} ({label})");
            Console.WriteLine($"  Best Quality: {maxQ}");
            Console.WriteLine($"  Template:     {chosenTemplate.Length} bytes (ISO-19794-2)");
            Console.WriteLine($"  Total Enrolled Fingers for User: {enrollmentData.Fingers.Count}/2");
            Console.WriteLine("==================================================");
            Console.WriteLine();

            return true;
        }
    }
}
