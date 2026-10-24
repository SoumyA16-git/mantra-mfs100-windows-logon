using System;
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
        /// Capture a fingerprint.
        ///
        /// Uses IsDetectFinger=false so the SDK polls for the full timeoutMs
        /// and returns MFS100_E_TIMEOUT (-1140) if no good scan is obtained.
        ///
        /// Returns a CaptureResult with Ok=true and templates on success,
        /// or Ok=false with ErrorCode/ErrorMsg on failure.
        /// </summary>
        public CaptureResult Capture(int timeoutMs)
        {
            if (!EnsureReady(out string notReadyMsg))
                return Fail(-1, notReadyMsg);

            WorkerLog.Info($"Capture start: timeoutMs={timeoutMs}");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                FingerData fd = new FingerData();

                // Small pause so previous touch can be lifted and optical prism settles
                System.Threading.Thread.Sleep(200);

                // IsDetectFinger=false: SDK polls until timeout or good scan.
                // ShowPreview=false: no HWND required — works in Session 0.
                int ret = _mfs!.AutoCapture(ref fd, timeoutMs, false, false);

                sw.Stop();
                WorkerLog.Info($"AutoCapture returned: code={ret} elapsed={sw.ElapsedMilliseconds}ms " +
                               $"quality={fd.Quality} nfiq={fd.Nfiq}");

                if (ret != 0)
                {
                    string msg = _mfs!.GetErrorMsg(ret);
                    WorkerLog.Warn($"Capture failed: code={ret} msg={msg}");
                    return Fail(ret, msg);
                }

                if (fd.Quality < MIN_CAPTURE_QUALITY)
                {
                    WorkerLog.Warn($"Capture quality too low: {fd.Quality} < {MIN_CAPTURE_QUALITY}");
                    return Fail(-2, $"Capture quality too low ({fd.Quality}); please lift and re-place finger");
                }

                if (fd.ISOTemplate == null || fd.ISOTemplate.Length == 0)
                {
                    WorkerLog.Error("Capture succeeded but ISOTemplate is null/empty");
                    return Fail(-3, "SDK returned empty template despite success code");
                }

                WorkerLog.Info($"Capture success: quality={fd.Quality} nfiq={fd.Nfiq} " +
                               $"isoLen={fd.ISOTemplate.Length} ansiLen={fd.ANSITemplate?.Length ?? 0}");

                return new CaptureResult
                {
                    Ok           = true,
                    IsoTemplate  = (byte[])fd.ISOTemplate.Clone(),
                    AnsiTemplate = fd.ANSITemplate != null ? (byte[])fd.ANSITemplate.Clone() : Array.Empty<byte>(),
                    Quality      = fd.Quality,
                    Nfiq         = fd.Nfiq
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                WorkerLog.Error($"Capture exception after {sw.ElapsedMilliseconds}ms: {ex}");
                return Fail(-99, $"Capture exception: {ex.Message}");
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
}
