using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.ModelManagement {

    public class ModelBuildSessionStore {
        private static readonly string SessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NINA", "Plugins", "OnStepX", "ModelBuilds");

        public ModelBuildSessionStore() {
            Directory.CreateDirectory(SessionDir);
        }

        public async Task SaveAsync(ModelBuildSession session) {
            var path = SessionPath(session.SessionId);
            var tmp  = path + ".tmp";
            var json = JsonConvert.SerializeObject(session, Formatting.Indented);
            await File.WriteAllTextAsync(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }

        public async Task<ModelBuildSession?> LoadAsync(string sessionId) {
            var path = SessionPath(sessionId);
            if (!File.Exists(path)) return null;
            try {
                var json = await File.ReadAllTextAsync(path);
                return JsonConvert.DeserializeObject<ModelBuildSession>(json);
            } catch (Exception ex) {
                Logger.Error($"Failed to load session {sessionId}: {ex.Message}");
                return null;
            }
        }

        public string[] ListSessionIds() {
            if (!Directory.Exists(SessionDir)) return Array.Empty<string>();
            var files = Directory.GetFiles(SessionDir, "*.json");
            var ids = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                ids[i] = Path.GetFileNameWithoutExtension(files[i]);
            return ids;
        }

        private static string SessionPath(string sessionId) =>
            Path.Combine(SessionDir, sessionId + ".json");
    }
}
