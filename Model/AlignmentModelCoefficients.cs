using Newtonsoft.Json;

namespace NINA.Plugin.OnStepXTools.Model {

    // 12-parameter OnStepX pointing model.
    // Indices 8 (hcp) and a (dcp) are in degrees; all others are in arcseconds.
    public class AlignmentModelCoefficients {
        [JsonProperty("ax1Cor")] public double Ax1Cor { get; set; }  // index 0
        [JsonProperty("ax2Cor")] public double Ax2Cor { get; set; }  // index 1
        [JsonProperty("altCor")] public double AltCor { get; set; }  // index 2
        [JsonProperty("azmCor")] public double AzmCor { get; set; }  // index 3
        [JsonProperty("doCor")]  public double DoCor  { get; set; }  // index 4
        [JsonProperty("pdCor")]  public double PdCor  { get; set; }  // index 5 (written as 0; absorbed into Ax2Cor)
        [JsonProperty("dfCor")]  public double DfCor  { get; set; }  // index 6
        [JsonProperty("tfCor")]  public double TfCor  { get; set; }  // index 7
        [JsonProperty("hcp")]    public double Hcp    { get; set; }  // index 8 - degrees
        [JsonProperty("hca")]    public double Hca    { get; set; }  // index 9
        [JsonProperty("dcp")]    public double Dcp    { get; set; }  // index a - degrees
        [JsonProperty("dca")]    public double Dca    { get; set; }  // index b
        [JsonProperty("stars")]  public int    Stars  { get; set; }
    }
}
