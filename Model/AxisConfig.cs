using Newtonsoft.Json;

namespace NINA.Plugin.OnStepXTools.Model {

    public class AxisConfig {
        [JsonProperty("stepsPerWormRotation")] public long StepsPerWormRotation { get; set; }
        [JsonProperty("stepsPerDegree")]       public double StepsPerDegree { get; set; }
        [JsonProperty("reverseDirection")]     public bool ReverseDirection { get; set; }
        [JsonProperty("minPosition")]          public double MinPositionDeg { get; set; }
        [JsonProperty("maxPosition")]          public double MaxPositionDeg { get; set; }
    }

    public class PidConfig {
        [JsonProperty("p")] public double P { get; set; }
        [JsonProperty("i")] public double I { get; set; }
        [JsonProperty("d")] public double D { get; set; }
    }
}
