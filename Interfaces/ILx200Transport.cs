namespace NINA.Plugin.OnStepXTools.Interfaces {

    public interface ILx200Transport {
        string? SendString(string command);
        bool SendBlind(string command);
        bool SendBool(string command);
    }
}
