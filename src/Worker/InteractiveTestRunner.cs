using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace MFS100Worker
{
    /// <summary>
    /// Gate 1A + 1C: Interactive FAR/FRR test runner with strict biometric discrimination validation.
    /// Run with: MFS100Worker.exe --test
    ///
    /// Walks the user through:
    ///   1. SDK init and device info check
    ///   2. 5 captures of PRIMARY finger with in-memory duplicate detection
    ///   3. Genuine match score distribution (10 pairwise comparisons)
    ///   4. 3 captures of a DIFFERENT finger with in-memory primary-finger cross-checks
    ///   5. Impostor match score distribution (15 pairwise comparisons)
    ///   6. FAR/FRR evaluation on the 0–10000 MFS100 score scale
    ///   7. Strict discrimination validation: genuine and impostor distributions MUST NOT overlap
    ///   8. Uninit/Init recovery test
    ///
    /// No biometric templates, raw images, or hashes are written to persistent logs.
    /// Results summary is written to docs\test_results.md
    /// </summary>
    internal static class InteractiveTestRunner
    {
        public static void Run()
        {
            // Reset Console.Out to standard output so prompts and tables display in console window
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });

            Console.WriteLine("==================================================");
            Console.WriteLine("  MFS100Worker — Gate 1 Biometric Test Runner     ");
            Console.WriteLine($"  Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine("==================================================");
            Console.WriteLine();

            var results = new TestResults();
            using var sdk = new SdkManager();

            // ------------------------------------------------------------------
            // Step 1: Init
            // ------------------------------------------------------------------
            Section("Step 1: SDK Initialization");
            var (initOk, initMsg) = sdk.Init();
            Record(results, "1.1 SDK Init", initOk, initOk ? "OK" : initMsg);
            if (!initOk)
            {
                Fail("Cannot proceed without SDK init. Please check device connection.");
                PauseAndExit();
                return;
            }

            var (make, model, serial, w, h) = sdk.GetDeviceInfo();
            Console.WriteLine($"  Device detected: {make} {model} | Serial: {serial} | Resolution: {w}x{h}");
            results.DeviceInfo = $"{make} {model} Serial={serial} {w}x{h}";
            Console.WriteLine();

            // ------------------------------------------------------------------
            // Step 2: Primary finger captures (5 scans)
            // ------------------------------------------------------------------
            Section("Step 2: Primary Finger Captures (5 scans — use the SAME finger)");
            Console.WriteLine("  Quality target: >= 60 (Minimum threshold: 40)");
            Console.WriteLine("  NOTE: Please LIFT your finger completely between scans.");
            Console.WriteLine();

            var finger1Templates = new List<byte[]>();
            var finger1Qualities = new List<int>();

            for (int i = 1; i <= 5; i++)
            {
                Console.WriteLine($"  Scan {i}/5:");
                Console.WriteLine("  -> Lift finger, ensure sensor is clear.");
                Prompt($"     Place your PRIMARY finger on the scanner when ready... [press Enter]");

                CaptureResult cr = sdk.Capture(15000);
                ReportCapture(i, cr);

                if (!cr.Ok || cr.IsoTemplate == null || cr.IsoTemplate.Length == 0)
                {
                    Record(results, $"2.{i} Capture (finger1)", false, $"Capture failed: {cr.ErrorMsg}");
                    continue;
                }

                // In-memory byte diversity check against previous scans of this session
                bool isDuplicate = finger1Templates.Any(prev => prev.SequenceEqual(cr.IsoTemplate));
                if (isDuplicate)
                {
                    Warn($"Scan {i} produced byte-identical template to an earlier scan! (Buffer or latent print).");
                    Record(results, $"2.{i} Diversity", false, "FAIL: Duplicate template bytes returned");
                }
                else
                {
                    Record(results, $"2.{i} Diversity", true, $"Unique template ({cr.IsoTemplate.Length} bytes)");
                }

                finger1Templates.Add((byte[])cr.IsoTemplate.Clone());
                finger1Qualities.Add(cr.Quality);
                Record(results, $"2.{i} Capture (finger1)", true, $"Q={cr.Quality} NFIQ={cr.Nfiq} ISOLen={cr.IsoTemplate.Length}");
                Console.WriteLine();
            }

            if (finger1Templates.Count < 2)
            {
                Fail("Insufficient valid primary captures (< 2). Cannot perform matching tests.");
                WriteResultsFile(results, new List<int>(), new List<int>());
                PauseAndExit();
                return;
            }

            // ------------------------------------------------------------------
            // Step 3: Genuine match distribution (10 pairs)
            // ------------------------------------------------------------------
            Section("Step 3: Genuine Match Score Distribution (Same Finger Pairs)");
            Console.WriteLine("  Score scale: 0 to 10000 (10000 = exact identical match)");
            Console.WriteLine();

            var genuineScores = new List<int>();
            for (int i = 0; i < finger1Templates.Count; i++)
            {
                for (int j = i + 1; j < finger1Templates.Count; j++)
                {
                    MatchResult mr = sdk.Match(finger1Templates[i], finger1Templates[j]);
                    string label = $"  Genuine pair ({i + 1},{j + 1})";
                    if (mr.Ok)
                    {
                        genuineScores.Add(mr.Score);
                        Console.WriteLine($"{label}: score = {mr.Score}");
                    }
                    else
                    {
                        Console.WriteLine($"{label}: FAILED code={mr.ErrorCode} {mr.ErrorMsg}");
                    }
                }
            }

            PrintScoreStats("Genuine (same finger)", genuineScores);
            bool genuineOk = genuineScores.Count >= 1 && genuineScores.All(s => s > 0);
            Record(results, "3.0 Genuine Matching", genuineOk,
                genuineOk ? $"Scores: min={genuineScores.Min()} max={genuineScores.Max()} mean={genuineScores.Average():F0} count={genuineScores.Count}"
                          : "FAILED: Genuine matching failed or produced zero scores");
            Console.WriteLine();

            // ------------------------------------------------------------------
            // Step 4: Impostor captures with DIFFERENT finger (3 scans)
            // ------------------------------------------------------------------
            Section("Step 4: Impostor Captures (3 scans — MUST USE A DIFFERENT FINGER)");
            Console.WriteLine("  ATTENTION: You MUST place a DIFFERENT finger (e.g. index on opposite hand or thumb).");
            Console.WriteLine("  Wipe the scanner surface and lift your hand before proceeding.");
            Console.WriteLine();

            var finger2Templates = new List<byte[]>();
            for (int i = 1; i <= 3; i++)
            {
                Console.WriteLine($"  Different Finger Scan {i}/3:");
                Console.WriteLine("  -> Place a DIFFERENT finger on the scanner.");
                Prompt($"     Press Enter when ready...");

                CaptureResult cr = sdk.Capture(15000);
                ReportCapture(i, cr);

                if (!cr.Ok || cr.IsoTemplate == null || cr.IsoTemplate.Length == 0)
                {
                    Record(results, $"4.{i} Capture (finger2)", false, $"Capture failed: {cr.ErrorMsg}");
                    continue;
                }

                // In-memory cross-check against primary finger templates
                bool matchesPrimaryBytes = finger1Templates.Any(t1 => t1.SequenceEqual(cr.IsoTemplate));
                if (matchesPrimaryBytes)
                {
                    Fail($"Scan {i} returned IDENTICAL bytes to a primary finger scan! The same finger was placed or buffer was not refreshed.");
                    Record(results, $"4.{i} Distinctness", false, "FAIL: Template bytes identical to primary finger");
                }
                else
                {
                    Record(results, $"4.{i} Distinctness", true, $"Distinct template bytes ({cr.IsoTemplate.Length} bytes)");
                }

                Record(results, $"4.{i} Capture (finger2)", cr.Ok, $"Q={cr.Quality} NFIQ={cr.Nfiq} ISOLen={cr.IsoTemplate.Length}");
                finger2Templates.Add((byte[])cr.IsoTemplate.Clone());
                Console.WriteLine();
            }

            // ------------------------------------------------------------------
            // Step 4B: Impostor match distribution (15 pairs)
            // ------------------------------------------------------------------
            Section("Step 4B: Impostor Match Score Distribution (Cross-Finger Pairs)");
            var impostorScores = new List<int>();

            if (finger1Templates.Count > 0 && finger2Templates.Count > 0)
            {
                for (int i = 0; i < finger1Templates.Count; i++)
                {
                    for (int j = 0; j < finger2Templates.Count; j++)
                    {
                        MatchResult mr = sdk.Match(finger1Templates[i], finger2Templates[j]);
                        string label = $"  Cross pair (P{i + 1}, D{j + 1})";
                        if (mr.Ok)
                        {
                            impostorScores.Add(mr.Score);
                            Console.WriteLine($"{label}: score = {mr.Score}");
                        }
                        else
                        {
                            Console.WriteLine($"{label}: FAILED code={mr.ErrorCode} {mr.ErrorMsg}");
                        }
                    }
                }
                PrintScoreStats("Impostor (cross-finger)", impostorScores);
            }
            else
            {
                Console.WriteLine("  Skipped — insufficient captures for impostor matching.");
            }
            Console.WriteLine();

            // ------------------------------------------------------------------
            // Step 5: FAR / FRR Analysis on MFS100 Scale (0–10000)
            // ------------------------------------------------------------------
            Section("Step 5: FAR / FRR Evaluation on MFS100 Scale (0–10000)");
            Console.WriteLine("  (1400 is the documented standard threshold for 1:1 verification in Mantra/Innovatrics)");
            Console.WriteLine();

            int[] thresholds = { 500, 1000, 1400, 2000, 3000, 4000, 5000, 6000, 7000, 8000 };
            Console.WriteLine($"  {"Threshold",-12} {"FAR%",-10} {"FRR%",-10} {"FAR count",-12} {"FRR count",-12}");
            Console.WriteLine($"  {new string('-', 58)}");

            foreach (int t in thresholds)
            {
                int far = impostorScores.Count(s => s >= t);
                int frr = genuineScores.Count(s => s < t);
                double farPct = impostorScores.Count > 0 ? 100.0 * far / impostorScores.Count : -1;
                double frrPct = genuineScores.Count  > 0 ? 100.0 * frr / genuineScores.Count  : -1;
                Console.WriteLine($"  {t,-12} {farPct,9:F1}% {frrPct,9:F1}% {far,-12} {frr,-12}");
                results.FarFrr.Add((t, farPct, frrPct, far, frr));
            }
            Console.WriteLine();

            // ------------------------------------------------------------------
            // Step 5.5: Biometric Discrimination Validation (The Core Guardrail)
            // ------------------------------------------------------------------
            Section("Step 5.5: Biometric Discrimination Guardrail");
            bool discriminationPassed = false;

            if (genuineScores.Count > 0 && impostorScores.Count > 0)
            {
                int genuineMin = genuineScores.Min();
                int genuineMax = genuineScores.Max();
                int impostorMin = impostorScores.Min();
                int impostorMax = impostorScores.Max();
                double genuineMean = genuineScores.Average();
                double impostorMean = impostorScores.Average();

                // Check 1: Are genuine and impostor scores identical?
                bool identicalScores = (genuineMin == impostorMax && genuineMax == impostorMin);
                if (identicalScores)
                {
                    Fail($"CRITICAL ERROR: Genuine and Impostor scores are identical (all = {genuineMin})!");
                    Record(results, "5.1 Discrimination", false, "FAIL: Matcher returned identical score for genuine and impostor pairs");
                }
                // Check 2: Score separation margin (genuineMin must be strictly greater than impostorMax)
                else if (genuineMin <= impostorMax)
                {
                    Fail($"CRITICAL ERROR: Score distributions overlap! Genuine min ({genuineMin}) <= Impostor max ({impostorMax}).");
                    Record(results, "5.1 Discrimination", false, $"FAIL: Overlap detected (genuine min {genuineMin} <= impostor max {impostorMax})");
                }
                else
                {
                    int margin = genuineMin - impostorMax;
                    Console.WriteLine($"  [PASS] Score separation verified:");
                    Console.WriteLine($"         Genuine range:  {genuineMin} to {genuineMax} (mean: {genuineMean:F0})");
                    Console.WriteLine($"         Impostor range: {impostorMin} to {impostorMax} (mean: {impostorMean:F0})");
                    Console.WriteLine($"         Safety margin:  {margin} points");
                    Record(results, "5.1 Discrimination", true, $"PASS: Separation verified (margin = {margin} points)");
                    discriminationPassed = true;
                }

                // Check 3: FAR at standard threshold (1400)
                int farAt1400 = impostorScores.Count(s => s >= 1400);
                if (farAt1400 > 0)
                {
                    Fail($"FAR target failed: {farAt1400}/{impostorScores.Count} impostors accepted at threshold 1400!");
                    Record(results, "5.2 FAR at Threshold 1400", false, $"FAIL: FAR = {farAt1400}/{impostorScores.Count}");
                }
                else
                {
                    Console.WriteLine($"  [PASS] FAR at threshold 1400: 0/{impostorScores.Count} (0.0% False Accepts)");
                    Record(results, "5.2 FAR at Threshold 1400", true, "PASS: 0.0% False Accepts");
                }
            }
            else
            {
                Record(results, "5.1 Discrimination", false, "FAIL: Missing match score data");
            }
            Console.WriteLine();

            // ------------------------------------------------------------------
            // Step 6: Process recovery test (Uninit + Re-Init)
            // ------------------------------------------------------------------
            Section("Step 6: SDK Recovery Test (Uninit + Re-Init)");
            sdk.Uninit();
            var (reinitOk, reinitMsg) = sdk.Init();
            Record(results, "6.1 Re-Init after Uninit", reinitOk, reinitOk ? "OK" : reinitMsg);

            if (reinitOk)
            {
                Prompt("  Place your PRIMARY finger for recovery capture... [press Enter]");
                CaptureResult recCr = sdk.Capture(15000);
                Record(results, "6.2 Capture after Re-Init", recCr.Ok,
                    recCr.Ok ? $"Q={recCr.Quality} NFIQ={recCr.Nfiq}" : recCr.ErrorMsg);
                ReportCapture(1, recCr);
            }
            Console.WriteLine();

            // ------------------------------------------------------------------
            // Summary
            // ------------------------------------------------------------------
            Section("Overall Gate 1 Summary");
            bool allPassed = results.Steps.All(s => s.passed) && discriminationPassed;

            Console.WriteLine("==================================================");
            if (allPassed)
            {
                Console.WriteLine("   GATE 1 RESULT: ALL TESTS PASSED                ");
                Console.WriteLine("   Biometric discrimination verified successfully.");
            }
            else
            {
                Console.WriteLine("   GATE 1 RESULT: FAILED                          ");
                Console.WriteLine("   One or more validation criteria failed.        ");
                Console.WriteLine("   Review details above and in test_results.md    ");
            }
            Console.WriteLine("==================================================");
            Console.WriteLine();

            // Write results file to repo root docs directory
            string outPath = WriteResultsFile(results, genuineScores, impostorScores, allPassed);
            Console.WriteLine($"  Detailed report written to: {outPath}");
            Console.WriteLine();

            PauseAndExit();
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        static void Section(string title) => Console.WriteLine($"--- {title} ---");

        static void Prompt(string msg)
        {
            Console.Write(msg + " ");
            Console.ReadLine();
        }

        static void ReportCapture(int idx, CaptureResult cr)
        {
            if (cr.Ok)
                Console.WriteLine($"  → OK: Quality={cr.Quality} NFIQ={cr.Nfiq} ISOLen={cr.IsoTemplate.Length}");
            else
                Console.WriteLine($"  → FAILED: code={cr.ErrorCode} msg={cr.ErrorMsg}");
        }

        static void PrintScoreStats(string label, List<int> scores)
        {
            if (scores.Count == 0) { Console.WriteLine($"  {label}: no data"); return; }
            Console.WriteLine($"  {label}: min={scores.Min()} max={scores.Max()} mean={scores.Average():F0} count={scores.Count}");
        }

        static void Record(TestResults r, string step, bool passed, string detail)
        {
            r.Steps.Add((step, passed, detail));
            string status = passed ? "PASS" : "FAIL";
            Console.WriteLine($"  [{status}] {step}: {detail}");
        }

        static void Warn(string msg) => Console.WriteLine($"  [WARN] {msg}");
        static void Fail(string msg) => Console.WriteLine($"  [FAIL] {msg}");

        static void PauseAndExit()
        {
            Console.WriteLine();
            Console.Write("Press Enter to exit...");
            Console.ReadLine();
        }

        static string WriteResultsFile(TestResults r, List<int> genuine, List<int> impostor, bool overallPassed = false)
        {
            // Traverse up to repository root docs directory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\docs"));
            if (!Directory.Exists(dir))
            {
                dir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\docs"));
                if (!Directory.Exists(dir))
                    dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs");
            }
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "test_results.md");

            var sb = new StringBuilder();
            sb.AppendLine("# Gate 1 Biometric Test Results");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Device: {r.DeviceInfo}");
            sb.AppendLine($"Overall Gate 1 Status: {(overallPassed ? "**PASSED**" : "**FAILED**")}");
            sb.AppendLine();
            sb.AppendLine("## Step Results & Diagnostic Checks");
            sb.AppendLine("| Step | Result | Detail |");
            sb.AppendLine("|---|---|---|");
            foreach (var (step, passed, detail) in r.Steps)
                sb.AppendLine($"| {step} | {(passed ? "PASS" : "**FAIL**")} | {detail} |");
            sb.AppendLine();

            sb.AppendLine("## Genuine Match Scores (Same Finger)");
            sb.AppendLine($"Sample Pairs Evaluated: {genuine.Count}");
            if (genuine.Count > 0)
            {
                sb.AppendLine($"Min: {genuine.Min()} | Max: {genuine.Max()} | Mean: {genuine.Average():F0}");
                sb.AppendLine("Observed Pair Scores: " + string.Join(", ", genuine));
            }
            else
            {
                sb.AppendLine("No genuine score data available.");
            }
            sb.AppendLine();

            sb.AppendLine("## Impostor Match Scores (Different Fingers)");
            sb.AppendLine($"Sample Pairs Evaluated: {impostor.Count}");
            if (impostor.Count > 0)
            {
                sb.AppendLine($"Min: {impostor.Min()} | Max: {impostor.Max()} | Mean: {impostor.Average():F0}");
                sb.AppendLine("Observed Pair Scores: " + string.Join(", ", impostor));
            }
            else
            {
                sb.AppendLine("No impostor score data available.");
            }
            sb.AppendLine();

            sb.AppendLine("## FAR / FRR Evaluation Table (0–10000 Scale)");
            sb.AppendLine("| Threshold | FAR% | FRR% | FAR count | FRR count |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var (t, far, frr, farCnt, frrCnt) in r.FarFrr)
                sb.AppendLine($"| {t} | {far:F1}% | {frr:F1}% | {farCnt} | {frrCnt} |");
            sb.AppendLine();

            sb.AppendLine("## Limitations & Privacy Confirmation");
            sb.AppendLine("- Test fingerprints evaluated solely in volatile memory; zero biometric templates, raw images, or hashes written to persistent storage.");
            sb.AppendLine("- FAR/FRR statistics reflect the experimental test set of pairwise comparisons and are not certified commercial accuracy ratings.");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        class TestResults
        {
            public string DeviceInfo = "";
            public List<(string step, bool passed, string detail)> Steps = new();
            public List<(int threshold, double far, double frr, int farCnt, int frrCnt)> FarFrr = new();
        }
    }
}
