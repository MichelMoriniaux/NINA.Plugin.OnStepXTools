namespace NINA.Plugin.OnStepXTools.Model {

    // Parsed OnStepX firmware version, e.g. "10.28t" -> Major=10, Minor=28, Patch='t'.
    // Patch is '\0' when the firmware string has no trailing patch letter.
    public sealed class FirmwareVersion {
        public int Major { get; init; }
        public int Minor { get; init; }
        public char Patch { get; init; }

        // True if this version is at or after major.minor[patch] - e.g. IsAtLeast(10, 28, 't')
        // is true for 10.28t, 10.28u, 10.29, 11.0; false for 10.28s, 10.27z. A missing patch
        // letter (Patch == '\0') compares as earlier than any lettered patch at the same
        // major.minor, since OnStepX point releases are always lettered in practice - safer to
        // assume a fix isn't present than to assume it is when the version string is ambiguous.
        public bool IsAtLeast(int major, int minor, char patch) {
            if (Major != major) return Major > major;
            if (Minor != minor) return Minor > minor;
            return char.ToLowerInvariant(Patch) >= char.ToLowerInvariant(patch);
        }
    }
}
