using NINA.Plugin.OnStepXTools.Model;
using Xunit;

namespace NINA.Plugin.OnStepXTools.Tests;

public class FirmwareVersionTests {
    [Theory]
    [InlineData(10, 28, 't', true)]   // exact match
    [InlineData(10, 28, 'u', true)]   // later patch letter, same major.minor
    [InlineData(10, 29, 'a', true)]   // later minor
    [InlineData(11, 0, '\0', true)]   // later major
    [InlineData(10, 28, 's', false)]  // earlier patch letter, same major.minor
    [InlineData(10, 27, 'z', false)]  // earlier minor, even with a "later" letter
    [InlineData(9, 30, 'z', false)]   // earlier major
    public void IsAtLeast_ComparesMajorMinorPatchCorrectly(int major, int minor, char patch, bool expected) {
        var version = new FirmwareVersion { Major = major, Minor = minor, Patch = patch };
        Assert.Equal(expected, version.IsAtLeast(10, 28, 't'));
    }

    [Fact]
    public void IsAtLeast_MissingPatchLetterAtSameMajorMinor_IsTreatedAsBeforeAnyLetteredPatch() {
        // OnStepX point releases are always lettered in practice - a bare "10.28" is treated
        // as earlier than "10.28t" rather than assumed to already include the fix.
        var version = new FirmwareVersion { Major = 10, Minor = 28, Patch = '\0' };
        Assert.False(version.IsAtLeast(10, 28, 't'));
    }
}
