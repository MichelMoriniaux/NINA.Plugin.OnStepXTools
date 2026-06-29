using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NINA.Plugin.OnStepXTools.Model {

    public class SavedModelPoint {
        [JsonProperty("index")]     public int Index { get; set; }
        [JsonProperty("altDeg")]    public double AltitudeDeg { get; set; }
        [JsonProperty("azDeg")]     public double AzimuthDeg { get; set; }
        [JsonProperty("mountHA")]   public double MountHAHours { get; set; }
        [JsonProperty("mountDec")]  public double MountDecDeg { get; set; }
        [JsonProperty("solvedRA")]  public double SolvedRAHours { get; set; }
        [JsonProperty("solvedDec")] public double SolvedDecDeg { get; set; }
        [JsonProperty("pierSide")]  public int PierSide { get; set; }
        [JsonProperty("errRA")]     public double PointingErrorRAArcsec { get; set; }
        [JsonProperty("errDec")]    public double PointingErrorDecArcsec { get; set; }

        public void RestoreTo(AlignmentPoint point) {
            point.AltitudeDeg             = AltitudeDeg;
            point.AzimuthDeg              = AzimuthDeg;
            point.MountHAHours            = MountHAHours;
            point.MountDecDeg             = MountDecDeg;
            point.SolvedRAHours           = SolvedRAHours;
            point.SolvedDecDeg            = SolvedDecDeg;
            point.PierSide                = PierSide;
            point.PointingErrorRAArcsec   = PointingErrorRAArcsec;
            point.PointingErrorDecArcsec  = PointingErrorDecArcsec;
            point.State                   = AlignmentPointState.Added;
        }
    }

    public class ModelBuildSession {
        [JsonProperty("sessionId")]  public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        [JsonProperty("startedUtc")] public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        [JsonProperty("mode")]       public BuildMode Mode { get; set; }
        [JsonProperty("points")]     public List<SavedModelPoint> Points { get; set; } = new();
        [JsonProperty("coefficients")] public AlignmentModelCoefficients? Coefficients { get; set; }
    }
}
