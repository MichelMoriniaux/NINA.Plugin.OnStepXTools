using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.OnStepXTools.Equipment;
using NINA.Plugin.OnStepXTools.Model;
using NINA.Plugin.OnStepXTools.ModelManagement;
using Xunit;

namespace NINA.Plugin.OnStepXTools.Tests {

    public sealed class OnStepXMountProtocolTests {
        [Fact]
        public async Task CoefficientWrite_Gem_UsesIntegerMap_ActivatesAndVerifies() {
            var simulator = new OnStepXSimulator { MountType = MountType.GEM };
            var mount = new OnStepXMount(simulator);

            await mount.WriteCoefficientsAsync(new AlignmentModelCoefficients {
                Ax1Cor = 1,
                Ax2Cor = -3,
                AltCor = 30,
                AzmCor = -41,
                DoCor = 5,
                PdCor = 6,
                DfCor = 78,
                TfCor = -8,
                Hcp = 12,
                Hca = 12,
                Dcp = -14,
                Dca = 14
            });

            Assert.Contains(":SX07,78#", simulator.Commands);
            Assert.DoesNotContain(simulator.Commands, c => c.StartsWith(":SX09,", StringComparison.Ordinal) && c != ":SX09,2#");
            Assert.Contains(":SX09,2#", simulator.Commands);
            Assert.True(simulator.ModelIsReady);
            Assert.Empty(simulator.Faults);

            var readback = await mount.GetCoefficientsAsync();
            Assert.Equal(78, readback!.DfCor);
            Assert.Equal(-8, readback.TfCor);
            Assert.Equal(12, readback.Hcp);
            Assert.Equal(12, readback.Hca);
            Assert.Equal(-14, readback.Dcp);
            Assert.Equal(14, readback.Dca);
        }

        [Theory]
        [InlineData(MountType.Fork)]
        [InlineData(MountType.AltAz)]
        [InlineData(MountType.Fork_TA)]
        [InlineData(MountType.AltAz_Unlimited)]
        public async Task CoefficientWrite_ForkAndAltAzFamilies_UseRegisterSixForDfCor(MountType mountType) {
            var simulator = new OnStepXSimulator { MountType = mountType };
            var mount = new OnStepXMount(simulator);

            await mount.WriteCoefficientsAsync(new AlignmentModelCoefficients { DfCor = 42 });

            Assert.Contains(":SX06,42#", simulator.Commands);
            Assert.DoesNotContain(":SX07,42#", simulator.Commands);
            Assert.Empty(simulator.Faults);
        }

        [Fact]
        public async Task DangerousSettingsCommandsUseVerifiedFirmwareSpelling() {
            var simulator = new OnStepXSimulator();
            var mount = new OnStepXMount(simulator);

            await mount.SetHomePositionAsync();
            await mount.SetPauseAtHomeAsync(true);
            await mount.SetGotoBuzzerAsync(true);
            await mount.SetAutoMeridianFlipAsync(true);
            await mount.SetPreferredPierSideAsync(PreferredPierSide.Best);
            await mount.TriggerMeridianFlipAsync();
            await mount.SetLocationAsync(longitudeDeg: -122.25, latitudeDeg: 37.5, elevationM: 15.2);
            await mount.SetGuideRateAsync(3);
            await mount.SetSlewSpeedAsync(SlewSpeed.Fast);
            await mount.SetBacklashAsync(12, 13);
            await mount.SetLimitsAsync(-5, 80, 7.5, -2.5);

            Assert.Contains(":hF#", simulator.Commands);
            Assert.Contains(":SX98,1#", simulator.Commands);
            Assert.Contains(":SX97,1#", simulator.Commands);
            Assert.Contains(":SX95,1#", simulator.Commands);
            Assert.Contains(":SX96,B#", simulator.Commands);
            Assert.Contains(":MN#", simulator.Commands);
            Assert.Contains(":Sv15.2#", simulator.Commands);
            Assert.Contains(":R3#", simulator.Commands);
            Assert.Contains(":SX93,2#", simulator.Commands);
            Assert.Contains(":$BR12#", simulator.Commands);
            Assert.Contains(":$BD13#", simulator.Commands);
            Assert.Contains(":SXE9,30#", simulator.Commands);
            Assert.Contains(":SXEA,-10#", simulator.Commands);

            Assert.DoesNotContain(":FH#", simulator.Commands);
            Assert.DoesNotContain(simulator.Commands, c => c.StartsWith(":SXE7", StringComparison.Ordinal));
            Assert.DoesNotContain(":Mf#", simulator.Commands);
            Assert.False(simulator.FocuserHomed);
            Assert.Equal(25600, simulator.PecWormSteps);
            Assert.Empty(simulator.Faults);
        }

        [Fact]
        public async Task NewSettingsCommands_SendExpectedProtocolStrings() {
            var simulator = new OnStepXSimulator();
            var mount = new OnStepXMount(simulator);

            await mount.RevertAxisAsync(1);
            await mount.ClearEepromAsync();
            await mount.ContinueGotoAfterPauseAsync();
            await mount.SetEncoderOriginAsync();
            await mount.SetCompensatedTrackingAxisAsync(CompensatedTrackingAxis.Dual);
            await mount.SetRuntimeAxisConfigAsync(true);

            Assert.Contains(":SXA1,R#", simulator.Commands);
            Assert.Contains(":ENVRESET#", simulator.Commands);
            Assert.Contains(":SX99,1#", simulator.Commands);
            Assert.Contains(":SEO#", simulator.Commands);
            Assert.Contains(":T2#", simulator.Commands);
            Assert.Contains(":SXAC,0#", simulator.Commands);
            Assert.Empty(simulator.Faults);
        }

        [Fact]
        public async Task ReloadSettingsAsync_PopulatesStateFromMount() {
            var simulator = new OnStepXSimulator { MountType = MountType.GEM };
            var mount = new OnStepXMount(simulator);

            await mount.ReloadSettingsAsync();

            Assert.True(mount.State.TrackingEnabled);
            Assert.Equal(5, mount.State.GuideRateIndex);
            Assert.Equal("X", mount.State.LastError);
            Assert.Equal(MountType.GEM, mount.State.MountType);
            Assert.Equal(PreferredPierSide.Best, mount.State.PreferredPierSide);
            Assert.Equal(12, mount.State.BacklashAxis1Arcsec);
            Assert.Equal(13, mount.State.BacklashAxis2Arcsec);
            Assert.Equal(-5, mount.State.LimitMinAltDeg);
            Assert.Equal(85, mount.State.LimitMaxAltDeg);
            Assert.Equal(7.5, mount.State.LimitEastPastMeridian);
            Assert.Equal(-2.5, mount.State.LimitWestPastMeridian);
            Assert.Equal("+37*30:00", mount.State.LatitudeDMS);
            Assert.Equal("-122*15:00", mount.State.LongitudeDMS);
            Assert.Equal("15.2", mount.State.Elevation);
            Assert.Empty(mount.State.Axis1Params);
            Assert.Empty(mount.State.Axis2Params);
            Assert.Empty(simulator.Faults);
        }

        [Fact]
        public async Task SaveAxisAsync_PushesEditedParamsThenReloadsThatAxis() {
            var simulator = new OnStepXSimulator();
            simulator.AxisParams[1] = new Dictionary<int, string> {
                [1] = "5,0,10,4,$1"
            };
            var mount = new OnStepXMount(simulator);

            await mount.ReloadSettingsAsync();
            Assert.Single(mount.State.Axis1Params);
            Assert.Equal("Steps/degree", mount.State.Axis1Params[0].Name);
            Assert.Equal("5", mount.State.Axis1Params[0].CurrentValue);

            mount.State.Axis1Params[0].EditValue = "7";
            await mount.SaveAxisAsync(1);

            Assert.Contains(":SXA1,1,7#", simulator.Commands);
            Assert.Empty(simulator.Faults);
        }

        [Theory]
        [InlineData(ServoCalibrationCommand.TrackNormally,     ":SX4E,T#")]
        [InlineData(ServoCalibrationCommand.TrackFixedRate,    ":SX4E,F#")]
        [InlineData(ServoCalibrationCommand.RecordCalibration, ":SX4E,R#")]
        [InlineData(ServoCalibrationCommand.StopRecording,     ":SX4E,W#")]
        [InlineData(ServoCalibrationCommand.ClearBuffer,       ":SX4E,!#")]
        [InlineData(ServoCalibrationCommand.LoadCalibration,   ":SX4E,L#")]
        [InlineData(ServoCalibrationCommand.SaveCalibration,   ":SX4E,S#")]
        [InlineData(ServoCalibrationCommand.LoadBackup,        ":SX4E,V#")]
        [InlineData(ServoCalibrationCommand.SaveBackup,        ":SX4E,B#")]
        [InlineData(ServoCalibrationCommand.HighPassFilter,    ":SX4E,H#")]
        [InlineData(ServoCalibrationCommand.LowPassFilter,     ":SX4E,A#")]
        public async Task ServoCalibrationAsync_SendsExpectedCommand(ServoCalibrationCommand cmd, string expectedCommand) {
            var simulator = new OnStepXSimulator();
            var mount = new OnStepXMount(simulator);

            await mount.ServoCalibrationAsync(cmd);

            Assert.Contains(expectedCommand, simulator.Commands);
            Assert.Empty(simulator.Faults);
        }

        [Fact]
        public async Task StarUploadOrchestrator_ResetsUploadsComputesOnceAndSavesAfterVerify() {
            var simulator = new OnStepXSimulator();
            var mount = new OnStepXMount(simulator);
            var points = new[] {
                Point(0, 1),
                Point(1, -1),
                Point(2, 1)
            };

            var coefficients = await AlignmentUploadOrchestrator.UploadAndComputeAsync(
                mount,
                points,
                saveToEeprom: true,
                delayAsync: (_, _) => Task.CompletedTask,
                CancellationToken.None);

            Assert.NotNull(coefficients);
            Assert.Equal(1, simulator.Commands.Count(c => c == ":SX09,0#"));
            Assert.Equal(1, simulator.Commands.Count(c => c == ":SX09,1#"));
            Assert.True(simulator.Commands.IndexOf(":SX09,0#") < simulator.Commands.IndexOf(":SX0A,01:00:00#"));
            var computeIndex = simulator.Commands.IndexOf(":SX09,1#");
            Assert.True(computeIndex > simulator.Commands.LastIndexOf(":SX0E,1#"));
            Assert.DoesNotContain(
                simulator.Commands.Skip(computeIndex + 1),
                c => c.StartsWith(":SX0", StringComparison.Ordinal) &&
                     c.Length > 4 &&
                     (c[4] is 'A' or 'B' or 'C' or 'D' or 'E'));
            Assert.True(simulator.Commands.IndexOf(":AW#") > simulator.Commands.IndexOf(":SX09,1#"));
            Assert.Equal(1, simulator.AwCount);
            Assert.Empty(simulator.Faults);
        }

        private static AlignmentPoint Point(int index, int pierSide) => new() {
            Index = index,
            ActualHAHours = index + 1,
            ActualDecDeg = 20 + index,
            MountHAHours = index + 1.1,
            MountDecDeg = 20.5 + index,
            PierSide = pierSide,
            State = AlignmentPointState.Added
        };
    }
}
