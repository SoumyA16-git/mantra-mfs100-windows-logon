using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MFS100Worker
{
    /// <summary>
    /// Minimal standalone test for the MFS100 matcher.
    /// Evaluates MatchISO return values, score scales, and discrimination between
    /// identical and different templates using documented API calls.
    /// Reports only numeric scores, status codes, and boolean flags — no biometric data is stored.
    /// </summary>
    internal static class StandaloneMatcherTest
    {
        [DllImport("MFS100Dll.dll", EntryPoint = "MFS100MatchISO", CallingConvention = CallingConvention.StdCall)]
        private static extern int NativeMatchISO(IntPtr probe, IntPtr gallery, int maxRotation, ref int score);

        public static void Run()
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });

            Console.WriteLine("==================================================");
            Console.WriteLine("   MFS100 Standalone Matcher Diagnostics          ");
            Console.WriteLine($"   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine("==================================================");
            Console.WriteLine();

            // Locate official sample template
            string samplePath = @"C:\Program Files\Mantra\MFS100\Driver\MFS100Test\FingerData\ISOTemplate.iso";
            if (!File.Exists(samplePath))
            {
                Console.WriteLine($"[FAIL] Sample ISO template not found at: {samplePath}");
                return;
            }

            byte[] sample1 = File.ReadAllBytes(samplePath);
            Console.WriteLine($"Loaded official sample template: length = {sample1.Length} bytes");
            Console.WriteLine();

            // Create sample2: modify minutiae coordinates to simulate a non-matching finger
            // ISO 19794-2: Minutiae data starts at byte 28. Each minutia is 6 bytes.
            byte[] sample2 = (byte[])sample1.Clone();
            for (int i = 28; i < sample2.Length - 5; i += 6)
            {
                sample2[i]     = (byte)((255 - sample2[i]) % 256);
                sample2[i + 1] = (byte)((255 - sample2[i + 1]) % 256);
                sample2[i + 2] = (byte)((255 - sample2[i + 2]) % 256);
                sample2[i + 3] = (byte)((255 - sample2[i + 3]) % 256);
            }

            // Create sample3: dummy template with valid header but zeros/random in minutiae
            byte[] sampleDummy = new byte[sample1.Length];
            Array.Copy(sample1, 0, sampleDummy, 0, 28); // Keep 28-byte ISO header
            for (int i = 28; i < sampleDummy.Length; i++)
                sampleDummy[i] = (byte)(i % 251);

            // Initialize SDK
            var mfs = new MANTRA.MFS100();
            int initRet = mfs.Init();
            Console.WriteLine($"SDK Init(): ret = {initRet} (0 = OK)");
            if (initRet != 0)
            {
                Console.WriteLine($"[FAIL] SDK initialization failed with code {initRet}");
                return;
            }

            try
            {
                Console.WriteLine();
                Console.WriteLine("--- Test 1: Managed MANTRA.MFS100.MatchISO ---");

                // 1A: Self-match (Sample1 vs Sample1)
                int scoreSelf = -1;
                int retSelf = mfs.MatchISO(sample1, sample1, ref scoreSelf);
                bool selfByteEqual = sample1.SequenceEqual(sample1);
                Console.WriteLine($"  [Self-Match]     ret={retSelf}, score={scoreSelf}, inMemoryByteEqual={selfByteEqual}");

                // 1B: Different minutiae (Sample1 vs Sample2)
                int scoreDiff = -1;
                int retDiff = mfs.MatchISO(sample1, sample2, ref scoreDiff);
                bool diffByteEqual = sample1.SequenceEqual(sample2);
                Console.WriteLine($"  [Diff-Minutiae]  ret={retDiff}, score={scoreDiff}, inMemoryByteEqual={diffByteEqual}");

                // 1C: Dummy minutiae (Sample1 vs SampleDummy)
                int scoreDummy = -1;
                int retDummy = mfs.MatchISO(sample1, sampleDummy, ref scoreDummy);
                bool dummyByteEqual = sample1.SequenceEqual(sampleDummy);
                Console.WriteLine($"  [Dummy-Minutiae] ret={retDummy}, score={scoreDummy}, inMemoryByteEqual={dummyByteEqual}");

                Console.WriteLine();
                Console.WriteLine("--- Test 2: Native P/Invoke MFS100Dll.dll::MFS100MatchISO ---");

                IntPtr ptr1 = Marshal.AllocHGlobal(sample1.Length);
                IntPtr ptr2 = Marshal.AllocHGlobal(sample2.Length);
                Marshal.Copy(sample1, 0, ptr1, sample1.Length);
                Marshal.Copy(sample2, 0, ptr2, sample2.Length);

                try
                {
                    int nativeScoreSelf = -1;
                    int nativeRetSelf = NativeMatchISO(ptr1, ptr1, 180, ref nativeScoreSelf);
                    Console.WriteLine($"  [Native Self]    ret={nativeRetSelf}, score={nativeScoreSelf}");

                    int nativeScoreDiff = -1;
                    int nativeRetDiff = NativeMatchISO(ptr1, ptr2, 180, ref nativeScoreDiff);
                    Console.WriteLine($"  [Native Diff]    ret={nativeRetDiff}, score={nativeScoreDiff}");
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr1);
                    Marshal.FreeHGlobal(ptr2);
                }

                Console.WriteLine();
                Console.WriteLine("--- Test 3: Matcher Discrimination Evaluation ---");
                bool behavesCorrectly = (retSelf == 0 && scoreSelf >= 9000) &&
                                        (retDiff == 0 && scoreDiff < 500) &&
                                        (scoreDummy == 0);

                if (behavesCorrectly)
                {
                    Console.WriteLine("  [PASS] Matcher demonstrates clear biometric discrimination:");
                    Console.WriteLine($"         - Genuine self-match score: {scoreSelf} (>= 9000 on 0-10000 scale)");
                    Console.WriteLine($"         - Impostor/altered score:   {scoreDiff} (< 500 on 0-10000 scale)");
                    Console.WriteLine($"         - Dummy score:              {scoreDummy}");
                    Console.WriteLine($"         - Discrimination ratio:     {scoreSelf} : {scoreDiff}");
                }
                else
                {
                    Console.WriteLine("  [FAIL] Matcher does NOT discriminate samples correctly!");
                    Console.WriteLine($"         Self: {scoreSelf}, Diff: {scoreDiff}, Dummy: {scoreDummy}");
                }
            }
            finally
            {
                mfs.Uninit();
                mfs.Dispose();
            }

            Console.WriteLine();
            Console.WriteLine("==================================================");
        }
    }
}
