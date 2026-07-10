using Newtonsoft.Json;

namespace NINA.Plugin.OnStepXTools.Model {

    // 12-parameter OnStepX pointing model.
    // Indices a (hcp) and c (dcp) are in degrees; all others are in arcseconds.
    public class AlignmentModelCoefficients {
        [JsonProperty("ax1Cor")] public int Ax1Cor { get; set; }  // index 0
        [JsonProperty("ax2Cor")] public int Ax2Cor { get; set; }  // index 1
        [JsonProperty("altCor")] public int AltCor { get; set; }  // index 2
        [JsonProperty("azmCor")] public int AzmCor { get; set; }  // index 3
        [JsonProperty("doCor")]  public int DoCor  { get; set; }  // index 4
        [JsonProperty("pdCor")]  public int PdCor  { get; set; }  // index 5
        [JsonProperty("dfCor")]  public int DfCor  { get; set; }  // index 6 or 7
        [JsonProperty("tfCor")]  public int TfCor  { get; set; }  // index 8
        [JsonProperty("hcp")]    public int Hcp    { get; set; }  // index a - degrees
        [JsonProperty("hca")]    public int Hca    { get; set; }  // index b
        [JsonProperty("dcp")]    public int Dcp    { get; set; }  // index c - degrees
        [JsonProperty("dca")]    public int Dca    { get; set; }  // index d
        [JsonProperty("stars")]  public int Stars  { get; set; }
    }
}
