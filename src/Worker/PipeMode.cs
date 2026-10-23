using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MFS100Worker
{
    /// <summary>
    /// Pipe mode: reads one-line JSON commands from stdin, writes one-line JSON responses
    /// to the raw stdout handle. Used by the bridge service.
    ///
    /// Protocol (each message is a single UTF-8 JSON line):
    ///
    ///   Command: {"cmd":"init"}
    ///   Response: {"ok":true,"make":"MANTRA","model":"MFS100","serial":"49007331"}
    ///             {"ok":false,"errorCode":-1,"errorMsg":"..."}
    ///
    ///   Command: {"cmd":"capture","timeoutMs":10000}
    ///   Response: {"ok":true,"isoTemplate":"<base64>","ansiTemplate":"<base64>",
    ///              "quality":75,"nfiq":2}
    ///             {"ok":false,"errorCode":-1140,"errorMsg":"Timeout"}
    ///
    ///   Command: {"cmd":"match","probe":"<base64>","gallery":"<base64>"}
    ///   Response: {"ok":true,"score":234}
    ///             {"ok":false,"errorCode":-10,"errorMsg":"..."}
    ///
    ///   Command: {"cmd":"uninit"}
    ///   Response: {"ok":true}
    ///
    ///   Command: {"cmd":"ping"}
    ///   Response: {"ok":true,"pid":1234}
    ///
    /// The bridge service kills this process on deadline expiry.
    /// stdin EOF → worker exits cleanly.
    /// </summary>
    internal static class PipeMode
    {
        public static void Run()
        {
            WorkerLog.Info("Pipe mode started — awaiting commands on stdin");

            using var sdk = new SdkManager();

            // Use raw stdin/stdout for the JSON protocol so SDK stdout redirect
            // does not interfere.
            var stdinReader  = new StreamReader(
                new FileStream(
                    new Microsoft.Win32.SafeHandles.SafeFileHandle(
                        GetStdin(), false),
                    FileAccess.Read, 1, false),
                new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);

            var stdoutWriter = WorkerLog.RawStdout
                ?? new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
                   { AutoFlush = true };

            while (true)
            {
                string? line;
                try { line = stdinReader.ReadLine(); }
                catch { break; }

                if (line == null) break; // stdin closed — parent exited

                line = line.Trim();
                if (line.Length == 0) continue;

                WorkerLog.Info($"CMD: {line}");
                string response;
                try
                {
                    response = Dispatch(sdk, line);
                }
                catch (Exception ex)
                {
                    WorkerLog.Error($"Unhandled dispatch exception: {ex}");
                    response = Error(-99, $"Internal error: {ex.Message}");
                }

                WorkerLog.Info($"RSP: {response}");
                try { stdoutWriter.WriteLine(response); }
                catch { break; }
            }

            WorkerLog.Info("Pipe mode ended — process will exit");
            sdk.Uninit();
        }

        static string Dispatch(SdkManager sdk, string line)
        {
            JObject cmd;
            try { cmd = JObject.Parse(line); }
            catch { return Error(-1, "Invalid JSON command"); }

            string? cmdName = cmd["cmd"]?.Value<string>();
            if (cmdName == null) return Error(-1, "Missing 'cmd' field");

            switch (cmdName.ToLowerInvariant())
            {
                case "ping":
                    return Ok(new { pid = System.Diagnostics.Process.GetCurrentProcess().Id });

                case "init":
                    {
                        var (ok, msg) = sdk.Init();
                        if (!ok) return Error(-1, msg);
                        var (make, model, serial, w, h) = sdk.GetDeviceInfo();
                        return Ok(new { make, model, serial, width = w, height = h });
                    }

                case "capture":
                    {
                        int timeoutMs = cmd["timeoutMs"]?.Value<int>() ?? 10000;
                        CaptureResult r = sdk.Capture(timeoutMs);
                        if (!r.Ok) return Error(r.ErrorCode, r.ErrorMsg);
                        return Ok(new {
                            isoTemplate  = Convert.ToBase64String(r.IsoTemplate),
                            ansiTemplate = Convert.ToBase64String(r.AnsiTemplate),
                            quality      = r.Quality,
                            nfiq         = r.Nfiq
                        });
                    }

                case "match":
                    {
                        string? probeB64   = cmd["probe"]?.Value<string>();
                        string? galleryB64 = cmd["gallery"]?.Value<string>();
                        if (probeB64 == null || galleryB64 == null)
                            return Error(-10, "Missing 'probe' or 'gallery' field");

                        byte[] probe, gallery;
                        try
                        {
                            probe   = Convert.FromBase64String(probeB64);
                            gallery = Convert.FromBase64String(galleryB64);
                        }
                        catch { return Error(-10, "Invalid base64 in probe or gallery"); }

                        MatchResult r = sdk.Match(probe, gallery);
                        if (!r.Ok) return Error(r.ErrorCode, r.ErrorMsg);
                        return Ok(new { score = r.Score });
                    }

                case "uninit":
                    sdk.Uninit();
                    return Ok(new { });

                default:
                    return Error(-1, $"Unknown command: {cmdName}");
            }
        }

        static string Ok(object payload) =>
            JsonConvert.SerializeObject(
                JObject.FromObject(new { ok = true }).MergeWith(JObject.FromObject(payload)));

        static string Error(int code, string msg) =>
            JsonConvert.SerializeObject(new { ok = false, errorCode = code, errorMsg = msg });

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int nStdHandle);
        const int STD_INPUT_HANDLE = -10;
        static IntPtr GetStdin() => GetStdHandle(STD_INPUT_HANDLE);
    }

    static class JObjectExtensions
    {
        public static JObject MergeWith(this JObject target, JObject source)
        {
            target.Merge(source, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Union
            });
            return target;
        }
    }
}
