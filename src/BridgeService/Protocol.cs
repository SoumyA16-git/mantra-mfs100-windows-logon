using System;
using Newtonsoft.Json;

namespace MFS100BridgeSvc
{
    public static class Protocol
    {
        public const string PipeName = "MFS100Bridge";

        public class CommandRequest
        {
            [JsonProperty("cmd")]
            public string Cmd { get; set; } = string.Empty;

            [JsonProperty("timeoutMs")]
            public int TimeoutMs { get; set; } = 10000;

            [JsonProperty("probe")]
            public string? Probe { get; set; }

            [JsonProperty("gallery")]
            public string? Gallery { get; set; }
        }

        public class BaseResponse
        {
            [JsonProperty("ok")]
            public bool Ok { get; set; }

            [JsonProperty("errorCode", NullValueHandling = NullValueHandling.Ignore)]
            public int? ErrorCode { get; set; }

            [JsonProperty("errorMsg", NullValueHandling = NullValueHandling.Ignore)]
            public string? ErrorMsg { get; set; }
        }

        public class PingResponse : BaseResponse
        {
            [JsonProperty("servicePid")]
            public int ServicePid { get; set; }

            [JsonProperty("workerPid")]
            public int WorkerPid { get; set; }
        }

        public class DeviceInfoResponse : BaseResponse
        {
            [JsonProperty("make", NullValueHandling = NullValueHandling.Ignore)]
            public string? Make { get; set; }

            [JsonProperty("model", NullValueHandling = NullValueHandling.Ignore)]
            public string? Model { get; set; }

            [JsonProperty("serial", NullValueHandling = NullValueHandling.Ignore)]
            public string? Serial { get; set; }

            [JsonProperty("width", NullValueHandling = NullValueHandling.Ignore)]
            public int? Width { get; set; }

            [JsonProperty("height", NullValueHandling = NullValueHandling.Ignore)]
            public int? Height { get; set; }
        }

        public class CaptureResponse : BaseResponse
        {
            [JsonProperty("isoTemplate", NullValueHandling = NullValueHandling.Ignore)]
            public string? IsoTemplate { get; set; }

            [JsonProperty("ansiTemplate", NullValueHandling = NullValueHandling.Ignore)]
            public string? AnsiTemplate { get; set; }

            [JsonProperty("quality", NullValueHandling = NullValueHandling.Ignore)]
            public int? Quality { get; set; }

            [JsonProperty("nfiq", NullValueHandling = NullValueHandling.Ignore)]
            public int? Nfiq { get; set; }
        }

        public class MatchResponse : BaseResponse
        {
            [JsonProperty("score", NullValueHandling = NullValueHandling.Ignore)]
            public int? Score { get; set; }
        }

        public static string CreateErrorJson(int errorCode, string errorMsg)
        {
            return JsonConvert.SerializeObject(new BaseResponse
            {
                Ok = false,
                ErrorCode = errorCode,
                ErrorMsg = errorMsg
            }, Formatting.None);
        }
    }
}
