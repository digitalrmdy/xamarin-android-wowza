string nugetSource = "https://api.nuget.org/v3/index.json";

// Environment variables
var target = Argument("target", EnvironmentVariable("BUILD_TARGET") ?? "Default");
var buildNumber = EnvironmentVariable("BITRISE_BUILD_NUMBER") ?? "0";

var nugetApiKey = EnvironmentVariable("NUGET_API_KEY");
var isStableVersion = !string.IsNullOrEmpty(EnvironmentVariable("NUGET_STABLE"));

var projectName = "Rmdy.Android.Wowza"; 
var solutionPath = MakeAbsolute(File(EnvironmentVariable("SOLUTION_FILE") ?? $"./src/{projectName}.sln"));
var nuspecPath = MakeAbsolute(File(EnvironmentVariable("NUSPEC_FILE") ?? "./src/package.nuspec"));

string versionSuffix = null;

// Targets

Setup(setupContext =>
{
    if(buildNumber != null && !isStableVersion)
    {
        versionSuffix = "build." + buildNumber;
    }
});

Task("Build")
    .Does(()=>
    {
        var buildSettings = new MSBuildSettings { Configuration = "Release" };
        MSBuild(solutionPath, buildSettings);
    });
    
Task("Pack")
    .IsDependentOn("Build")
    .Does(()=>
    {
        Information($"NuGet version suffix: {versionSuffix}");
        
        var packSettings = new NuGetPackSettings();
        if(isStableVersion)
        {
            packSettings.Suffix = versionSuffix;
        }
        
        NuGetPack(nuspecPath, packSettings);
    });
    
Task("Publish")
    .IsDependentOn("Pack")
    .Does(()=>
    {
        var pkgPath = GetFiles($"./*.nupkg").First();
        
        if(string.IsNullOrEmpty(nugetApiKey))
        {
            Error("NuGet API key not set");
            return;
        }
        
        var pushSettings = new NuGetPushSettings
        {
            ApiKey = nugetApiKey,
            Source = nugetSource
        };
        
        NuGetPush(pkgPath, pushSettings);
    });
    
Task("NoPublish")
    .IsDependentOn("Pack");
    
Task("Default")
    .IsDependentOn("Publish");
    
RunTarget(target);