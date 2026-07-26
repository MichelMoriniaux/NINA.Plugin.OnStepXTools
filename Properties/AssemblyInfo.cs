using System.Reflection;
using System.Runtime.InteropServices;

// NEVER change this Guid between plugin versions - NINA uses it as the stable plugin identifier
[assembly: Guid("3a8f2c10-4b7e-4d6a-9e3f-0c1d5e8f2a4b")]
[assembly: AssemblyTitle("OnStepX Tools")]
[assembly: AssemblyDescription("Mount configuration and automated pointing model generation for OnStepX controllers")]
[assembly: AssemblyCompany("Michel Moriniaux")]
[assembly: AssemblyProduct("NINA.Plugin.OnStepXTools")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("1.0.1.7")]
[assembly: AssemblyFileVersion("1.0.1.7")]
[assembly: ComVisible(false)]

// NINA metadata - MinimumApplicationVersion is a load-time gate; must match the Plugins subfolder name
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.2017")]
[assembly: AssemblyMetadata("License", "MIT")]
[assembly: AssemblyMetadata("LicenseURL", "https://opensource.org/licenses/MIT")]
[assembly: AssemblyMetadata("Repository", "https://github.com/MichelMoriniaux/NINA.Plugin.OnStepXTools")]
[assembly: AssemblyMetadata("Tags", "OnStepX,Alignment,PointingModel,Telescope,Mount")]
[assembly: AssemblyMetadata("Homepage", "")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/MichelMoriniaux/NINA.Plugin.OnStepXTools/commits/main")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://github.com/MichelMoriniaux/NINA.Plugin.OnStepXTools/releases/download/resources/OnStepX.jpg")]
[assembly: AssemblyMetadata("ScreenshotURL", "https://github.com/MichelMoriniaux/NINA.Plugin.OnStepXTools/releases/download/resources/OnStepXToolsScreenshot.JPG")]
[assembly: AssemblyMetadata("AltScreenshotURL", "https://github.com/MichelMoriniaux/NINA.Plugin.OnStepXTools/releases/download/resources/OnStepXToolsAltScreenshot.JPG")]
[assembly: AssemblyMetadata("LongDescription", @"This plugin provides configuration panels for OnStepX mounts, as well as pointing model generation.

* NOTE: Use at your own risk this is unproven software, the model is not fully tested at this time *

# Features #

* Build full sky models using Golden Spiral in an Imaging tab dock
* Sidereal Path model point generation that creates points along the sidereal path of a DSO
* Grid model point generation that creates points along the AltAz grid
* Random point generation that creates random points in the sky
* Points sequence is optimized to reduce movements and meridian flips
* Advanced Sequencer items to build, load, and save models as part of a sequence
* NINA horizons supported. Load the horizon file in NINA Options -> General
* View and save loaded alignment models
* Load and Delete existing alignment models
* Adjust mount Options as in the SWS
* Adjust motor and PID parameters

# Support #

Usually active in the JTW discord as 'Crockett AndTubbs'
")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
