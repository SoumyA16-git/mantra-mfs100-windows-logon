using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using MANTRA;

namespace MFS100Worker
{
    /// <summary>
    /// Result returned by a capture operation.
    /// </summary>
    internal sealed class CaptureResult
    {
        public bool   Ok          { get; set; }
        public int    ErrorCode   { get; set; }
        public string ErrorMsg    { get; set; } = "";
        public byte[] IsoTemplate { get; set; } = Array.Empty<byte>();
        public byte[] AnsiTemplate{ get; set; } = Array.Empty<byte>();
        public int    Quality     { get; set; }
        public int    Nfiq        { get; set; }
    }

    /// <summary>
    /// Result returned by a match operation.
    /// </summary>
    internal sealed class MatchResult
    {
        public bool   Ok        { get; set; }
        public int    ErrorCode { get; set; }
        public string ErrorMsg  { get; set; } = "";
        public int    Score     { get; set; }
    }

    /// <summary>
    /// Wraps the Mantra MFS100 .NET SDK (MANTRA.MFS100) for use in the worker process.
    ///
    /// Design decisions:
    ///   - Init() is called once at worker startup.
    ///   - Capture() uses IsDetectFinger=false so that the SDK honors the timeout parameter.
    ///     (Experimentally verified: IsDetectFinger=true returns immediately with -1324 if
    ///     no finger is present, ignoring the timeout.)
    ///   - ShowPreview=false so that no window/HWND is required.
    ///   - The worker process is single-threaded: one Capture or Match at a time.
    ///   - The bridge service kills this process if it does not respond within the deadline.
    ///     Therefore we do NOT implement our own hang detection here — the parent handles it.
    ///
    /// SDK-reported quality range: 0–100. NFIQ: 1 (best) – 5 (worst).
    /// Minimum acceptable quality for enrollment: 50. For logon: 40.
    /// </summary>
    internal sealed class SdkManager : IDisposable
    {
        const int MIN_CAPTURE_QUALITY = 30;

        MFS100? _mfs;
        bool    _initialized;

        public SdkManager()
        {
            try
            {
                _mfs = new MFS100();
            }
            catch (Exception ex)
            {
                WorkerLog.Error($"Failed to instantiate MFS100: {ex}");
            }
        }

        // -----------------------------------------------------------------------
        // Init / Uninit
        // -----------------------------------------------------------------------

        /// <summary>
        /// Initialise the SDK and open the device.
        /// Returns (true, "") on success; (false, errorMsg) on failure.
        /// </summary>
        public (bool ok, string errorMsg) Init()
        {
            if (_initialized) return (true, "");
            try
            {
                if (_mfs == null) _mfs = new MFS100();

                if (!_mfs.IsConnected())
                {
                    WorkerLog.Warn("MFS100.IsConnected() returned false before Init");
                    return (false, "MFS100 device not connected");
                }

                int ret = _mfs.Init();
                if (ret != 0)
                {
                    string msg = _mfs.GetErrorMsg(ret);
                    WorkerLog.Error($"MFS100.Init() failed: code={ret} msg={msg}");
                    return (false, $"Init failed (code {ret}): {msg}");
                }

                DeviceInfo info = _mfs.GetDeviceInfo();
                WorkerLog.Info($"SDK initialised: Make={info.Make} Model={info.Model} " +
                               $"Serial={info.SerialNo} W={info.Width} H={info.Height} " +
                               $"SDKVersion={_mfs.GetSDKVersion()}");

                _initialized = true;
                return (true, "");
            }
            catch (Exception ex)
            {
                WorkerLog.Error($"Init() exception: {ex}");
                return (false, $"Init exception: {ex.Message}");
            }
        }

        public void Uninit()
        {
            if (_initialized && _mfs != null)
            {
                try { MFS100Native.MFS100StopXcan(); } catch { }
                try { _mfs.StopCapture(); } catch { }
                try { _mfs.Uninit(); }
                catch (Exception ex) { WorkerLog.Warn($"Uninit exception: {ex.Message}"); }
                _initialized = false;
            }
        }

        // -----------------------------------------------------------------------
        // Capture
        // -----------------------------------------------------------------------

        /// <summary>
        /// Capture a fingerprint with ultra-low latency.
        ///
        /// 1. Tries direct streaming frame polling via MFS100StartXcan (~300-500ms).
        ///    Eliminates 200ms sleep, WSQ image compression, and GC.Collect overhead.
        /// 2. If StartXcan is unsupported, falls back to direct native AutoCapture
        ///    with null preview callback and null bitmap to avoid slow wrapper overhead.
        /// </summary>
        public CaptureResult Capture(int timeoutMs)
        {
            if (!EnsureReady(out string notReadyMsg))
                return Fail(-1, notReadyMsg);

            WorkerLog.Info($"Capture start: timeoutMs={timeoutMs}");
            var sw = Stopwatch.StartNew();

            try
            {
                // Primary: Ultra-low-latency direct frame streaming (~300-500ms trigger)
                var fastResult = TryFastCapture(timeoutMs, sw);
                if (fastResult != null)
                {
                    return fastResult;
                }

                if (sw.ElapsedMilliseconds >= timeoutMs)
                {
                    return Fail(-1140, "No finger detected or timeout");
                }

                // Fallback: Direct native AutoCapture (bypasses slow WSQ compression and GC.Collect)
                return DirectNativeAutoCapture(timeoutMs, sw);
            }
            catch (Exception ex)
            {
                sw.Stop();
                WorkerLog.Error($"Capture exception after {sw.ElapsedMilliseconds}ms: {ex}");
                return Fail(-99, $"Capture exception: {ex.Message}");
            }
        }

        private CaptureResult? TryFastCapture(int timeoutMs, Stopwatch sw)
        {
            int width = MFS100Native.MFS100GetWidth();
            int height = MFS100Native.MFS100GetHeight();
            if (width <= 0 || height <= 0)
            {
                width = 316;
                height = 354;
            }

            int frameSize = width * height;
            IntPtr frameBuf = Marshal.AllocHGlobal(frameSize);
            IntPtr isoBuf = Marshal.AllocHGlobal(2048);
            IntPtr ansiBuf = Marshal.AllocHGlobal(2048);

            try
            {
                int ret = MFS100Native.MFS100StartXcan();
                if (ret != 0)
                {
                    WorkerLog.Warn($"MFS100StartXcan returned code {ret}, falling back to AutoCapture");
                    return null;
                }

                int quality = 0, contrast = 0, nfiq = 0;
                int consecutiveGoodFrames = 0;
                int bestQuality = 0;

                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    int fRet = MFS100Native.MFS100GetFrame(frameBuf);
                    if (fRet == 0)
                    {
                        MFS100Native.MFS100GetQuality(frameBuf, ref quality, ref contrast, ref nfiq);
                        if (quality > bestQuality)
                        {
                            bestQuality = quality;
                        }

                        // Low-latency acceptance logic:
                        // If quality >= 55: 1 frame is immediately accepted.
                        // If quality >= 45: 2 consecutive frames ensure optical contact has stabilized.
                        if (quality >= 55 || (quality >= 45 && ++consecutiveGoodFrames >= 2))
                        {
                            int isoLen = 2048;
                            int tRet = MFS100Native.MFS100ExtractISOTemplate(frameBuf, isoBuf, ref isoLen);

                            MFS100Native.MFS100StopXcan();
                            sw.Stop();

                            if (tRet == 0 && isoLen > 0)
                            {
                                byte[] isoData = new byte[isoLen];
                                Marshal.Copy(isoBuf, isoData, 0, isoLen);

                                int ansiLen = 2048;
                                byte[] ansiData = Array.Empty<byte>();
                                if (MFS100Native.MFS100ExtractANSITemplate(frameBuf, ansiBuf, ref ansiLen) == 0 && ansiLen > 0)
                                {
                                    ansiData = new byte[ansiLen];
                                    Marshal.Copy(ansiBuf, ansiData, 0, ansiLen);
                                }

                                WorkerLog.Info($"FastCapture success: elapsed={sw.ElapsedMilliseconds}ms quality={quality} nfiq={nfiq} isoLen={isoLen}");
                                return new CaptureResult
                                {
                                    Ok = true,
                                    IsoTemplate = isoData,
                                    AnsiTemplate = ansiData,
                                    Quality = quality,
                                    Nfiq = nfiq
                                };
                            }
                            else
                            {
                                WorkerLog.Warn($"MFS100ExtractISOTemplate failed (code {tRet}, len {isoLen})");
                                break;
                            }
                        }
                        else if (quality < 35)
                        {
                            consecutiveGoodFrames = 0;
                        }
                    }

                    Thread.Sleep(20);
                }

                MFS100Native.MFS100StopXcan();

                if (sw.ElapsedMilliseconds >= timeoutMs)
                {
                    sw.Stop();
                    WorkerLog.Warn($"FastCapture timed out after {sw.ElapsedMilliseconds}ms (bestQuality={bestQuality})");
                    return Fail(-1140, "No finger detected or timeout");
                }

                return null;
            }
            catch (Exception ex)
            {
                try { MFS100Native.MFS100StopXcan(); } catch { }
                WorkerLog.Warn($"FastCapture exception, will try fallback: {ex.Message}");
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(frameBuf);
                Marshal.FreeHGlobal(isoBuf);
                Marshal.FreeHGlobal(ansiBuf);
            }
        }

        private CaptureResult DirectNativeAutoCapture(int timeoutMs, Stopwatch sw)
        {
            int width = MFS100Native.MFS100GetWidth();
            int height = MFS100Native.MFS100GetHeight();
            if (width <= 0 || height <= 0) { width = 316; height = 354; }

            int frameSize = width * height;
            IntPtr frameBuf = Marshal.AllocHGlobal(frameSize);
            IntPtr isoBuf = Marshal.AllocHGlobal(2048);
            IntPtr ansiBuf = Marshal.AllocHGlobal(2048);

            try
            {
                int isoLen = 2048;
                int ansiLen = 2048;
                int quality = 0;
                int nfiq = 0;
                int remainingTimeout = Math.Max(1000, timeoutMs - (int)sw.ElapsedMilliseconds);

                // Call native MFS100AutoCapture directly with callbackPointer = NULL and bitmap = NULL!
                // This avoids managed callback context switches, bitmap allocations, WSQ wavelet compression, and GC.Collect()
                int ret = MFS100Native.MFS100AutoCapture(
                    IntPtr.Zero,
                    remainingTimeout,
                    frameBuf,
                    IntPtr.Zero,
                    isoBuf,
                    ref isoLen,
                    ansiBuf,
                    ref ansiLen,
                    ref quality,
                    ref nfiq,
                    0 // IsDetectFinger=false
                );

                sw.Stop();
                WorkerLog.Info($"DirectNativeAutoCapture returned: code={ret} elapsed={sw.ElapsedMilliseconds}ms quality={quality} nfiq={nfiq}");

                if (ret != 0 || isoLen <= 0)
                {
                    string msg = _mfs?.GetErrorMsg(ret) ?? $"Error code {ret}";
                    return Fail(ret != 0 ? ret : -1, msg);
                }

                byte[] isoData = new byte[isoLen];
                Marshal.Copy(isoBuf, isoData, 0, isoLen);

                byte[] ansiData = Array.Empty<byte>();
                if (ansiLen > 0)
                {
                    ansiData = new byte[ansiLen];
                    Marshal.Copy(ansiBuf, ansiData, 0, ansiLen);
                }

                return new CaptureResult
                {
                    Ok = true,
                    IsoTemplate = isoData,
                    AnsiTemplate = ansiData,
                    Quality = quality,
                    Nfiq = nfiq
                };
            }
            finally
            {
                Marshal.FreeHGlobal(frameBuf);
                Marshal.FreeHGlobal(isoBuf);
                Marshal.FreeHGlobal(ansiBuf);
            }
        }

        // -----------------------------------------------------------------------
        // Match
        // -----------------------------------------------------------------------

        /// <summary>
        /// Match two ISO templates. Returns a MatchResult with the score.
        /// Score range from the iengine: 0–1200+ (higher = better match).
        /// The caller decides the acceptance threshold.
        /// </summary>
        public MatchResult Match(byte[] probe, byte[] gallery)
        {
            if (_mfs == null)
            {
                try { _mfs = new MFS100(); } catch { }
            }

            if (_mfs == null)
            {
                return FailMatch(-1, "MFS100 SDK object is null");
            }

            if (!_initialized)
            {
                var (initOk, initMsg) = Init();
                if (!initOk)
                {
                    WorkerLog.Warn($"Match: Init() was not successful ({initMsg}), proceeding with MatchISO");
                }
            }

            if (probe == null || probe.Length == 0)
                return FailMatch(-10, "probe template is null or empty");
            if (gallery == null || gallery.Length == 0)
                return FailMatch(-10, "gallery template is null or empty");

            WorkerLog.Info($"Match start: probeLen={probe.Length} galleryLen={gallery.Length}");

            try
            {
                int score = 0;
                int ret = _mfs!.MatchISO(probe, gallery, ref score);

                if (ret != 0)
                {
                    string msg = _mfs.GetErrorMsg(ret);
                    WorkerLog.Warn($"MatchISO failed: code={ret} msg={msg}");
                    return FailMatch(ret, msg);
                }

                WorkerLog.Info($"Match result: score={score}");
                return new MatchResult { Ok = true, Score = score };
            }
            catch (Exception ex)
            {
                WorkerLog.Error($"Match exception: {ex}");
                return FailMatch(-99, $"Match exception: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------
        // Device info (for diagnostics)
        // -----------------------------------------------------------------------

        public (string make, string model, string serial, int width, int height) GetDeviceInfo()
        {
            if (_mfs == null || !_initialized)
                return ("", "", "", 0, 0);
            try
            {
                DeviceInfo d = _mfs.GetDeviceInfo();
                return (d.Make, d.Model, d.SerialNo, d.Width, d.Height);
            }
            catch { return ("", "", "", 0, 0); }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        bool EnsureReady(out string errorMsg)
        {
            if (_mfs == null || !_initialized)
            {
                errorMsg = "SDK not initialised";
                return false;
            }
            try
            {
                if (!_mfs.IsConnected())
                {
                    errorMsg = "MFS100 device disconnected";
                    WorkerLog.Warn("IsConnected() returned false mid-operation");
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMsg = $"IsConnected check failed: {ex.Message}";
                return false;
            }
            errorMsg = "";
            return true;
        }

        static CaptureResult Fail(int code, string msg) =>
            new CaptureResult { Ok = false, ErrorCode = code, ErrorMsg = msg };

        static MatchResult FailMatch(int code, string msg) =>
            new MatchResult { Ok = false, ErrorCode = code, ErrorMsg = msg };

        public void Dispose() => Uninit();
    }

    internal static class MFS100Native
    {
        const string DLL = "MFS100Dll.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int MFS100StartXcan();

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int MFS100StopXcan();

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int MFS100GetFrame(IntPtr FinalFrame);

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int MFS100GetQuality(IntPtr RawImage, ref int Quality, ref int Contrast, ref int NFIQ);

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int MFS100ExtractISOTemplate(IntPtr RawImage, IntPtr Iso_19794_2_Template, ref int TemplateLength);

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int MFS100ExtractANSITemplate(IntPtr RawImage, IntPtr Ansi_19794_2_Template, ref int TemplateLength);

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int MFS100GetWidth();

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int MFS100GetHeight();

        [DllImport(DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int MFS100AutoCapture(
            IntPtr callbackPointer,
            int Timeout,
            IntPtr Final_frame_500DPI,
            IntPtr BitmapImageData,
            IntPtr Iso_19794_2_Template,
            ref int ISO_TemplateLength,
            IntPtr Ansi_378_2004_Template,
            ref int ANSI_TemplateLength,
            ref int Quality,
            ref int NFIQ,
            int Detect_Finger);
    }
}
