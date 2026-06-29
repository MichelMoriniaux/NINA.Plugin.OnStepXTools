using System.Reflection;
using System.Runtime.InteropServices;

// NEVER change this Guid between plugin versions - NINA uses it as the stable plugin identifier
[assembly: Guid("3a8f2c10-4b7e-4d6a-9e3f-0c1d5e8f2a4b")]
[assembly: AssemblyTitle("OnStepX Tools")]
[assembly: AssemblyDescription("Mount configuration and automated pointing model generation for OnStepX controllers")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("NINA.Plugin.OnStepXTools")]
[assembly: AssemblyCopyright("Copyright © 2024")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]

// NINA metadata - MinimumApplicationVersion is a load-time gate; must match the Plugins subfolder name
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.2017")]
[assembly: AssemblyMetadata("License", "MIT")]
[assembly: AssemblyMetadata("LicenseURL", "https://opensource.org/licenses/MIT")]
[assembly: AssemblyMetadata("Repository", "https://github.com/MichelMoriniaux/NINA.Plugin.OnStepXTools")]
[assembly: AssemblyMetadata("Tags", "OnStepX,Alignment,PointingModel,Telescope")]
[assembly: AssemblyMetadata("Homepage", "")]
[assembly: AssemblyMetadata("ChangelogURL", "")]
[assembly: AssemblyMetadata("FeaturedImageURL", "")]
[assembly: AssemblyMetadata("ScreenshotURL", "")]
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
[assembly: AssemblyMetadata("LongDescription", @"Provides mount configuration, real-time status monitoring, and automated all-sky pointing model generation for OnStepX telescope controllers. Includes plate-solve-based star alignment and full-sky pointing model sequencer items.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
