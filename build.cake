#addin "Cake.ExtendedNuGet"
#addin "nuget:?package=NuGet.Core"
#addin "Cake.Slack"
#addin nuget:?package=Cake.Git
#addin "Cake.FileHelpers"

var projectName = "x3factr.Platform.Droid.Wowza";
var company = "3factr";

var target = Argument("target", EnvironmentVariable("BUILD_TARGET") ?? "Default");

var buildConfig = Argument("config", EnvironmentVariable("BUILD_CONFIG") ?? "Debug");
var buildIdStr = Argument("buildId", EnvironmentVariable("BITRISE_BUILD_NUMBER") ?? "1");
var buildId = Int32.Parse(buildIdStr);
var minimalVerbosity = string.IsNullOrEmpty(Argument("verbose", EnvironmentVariable("BUILD_VERBOSE") ?? string.Empty));
var solutionPath = MakeAbsolute(File((EnvironmentVariable("SOLUTION_FILE") ?? $"./src/{projectName}.sln")));

var customFeed = Argument("customFeed", EnvironmentVariable("CUSTOM_FEED"));
var nugetApiKey = Argument("nugetApiKey", EnvironmentVariable("NUGET_API_KEY"));
var nugetPublishingFeed = Argument("nugetPublishingFeed", EnvironmentVariable("NUGET_PUBLISHING_FEED"));

var slackHook = Argument("slackToken", EnvironmentVariable("SLACK_HOOK"));
var slackChannel = Argument("slackChannel", EnvironmentVariable("SLACK_CHANNEL"));
var slackMessageSettings = new SlackChatMessageSettings{ IncomingWebHookUrl = slackHook };

var repositoryDirectoryPath = DirectoryPath.FromString(EnvironmentVariable("BITRISE_SOURCE_DIR") ?? ".");
var gitBranchInfo = GitBranchCurrent(repositoryDirectoryPath);
var gitBranch = EnvironmentVariable("BITRISE_GIT_BRANCH") ?? gitBranchInfo.FriendlyName;
var gitCommitInfo = gitBranchInfo.Tip;
var gitCommitSha = gitCommitInfo.Sha;
var gitCommitMessage = gitCommitInfo.Message;
var gitcommitAuthor = gitCommitInfo.Author.Name;

var betaSuffix = gitBranch != "master" ? "-beta" : string.Empty;

Action<string> LogToSlack = logText => 
{
    if(!string.IsNullOrEmpty(slackHook) && !string.IsNullOrEmpty(slackChannel))
    {
        Slack.Chat.PostMessage(messageSettings: slackMessageSettings, channel: slackChannel, text: logText);
    }
};

Action<string, System.Exception> LogExceptionToSlack = (logText, exc) => 
{
    if(!string.IsNullOrEmpty(slackHook) && !string.IsNullOrEmpty(slackChannel))
    {
        Slack.Chat.PostMessage(messageSettings: slackMessageSettings, channel: slackChannel, text: ":no_entry_sign: " + logText, messageAttachments: new SlackChatMessageAttachment[]
        {
            new SlackChatMessageAttachment
            {
                Color = "#FF0000",
                Text = $"{exc.GetType().Name} occured",
                Fields = new SlackChatMessageAttachmentField[]
                {
                    new SlackChatMessageAttachmentField
                    {
                        Title = "Message",
                        Value = exc.Message
                    }
                }
            }
        });
    }
};

Task("CleanBuild")
    .Does(() => 
    {
        CleanDirectories("./**/bin");
        CleanDirectories("./**/obj");
        DeleteFiles("./*.nupkg");
        Information("Cleaned build and package directories");
    });

Task("RestorePackages")
    .Does(() => 
    {
        try
        {
            var sources = new List<string>
            {
                "https://api.nuget.org/v3/index.json"
            };
            if(!string.IsNullOrEmpty(customFeed))
            {
                sources.Add(customFeed);
            }
            
            Information("Restoring NuGet packages for {0}", solutionPath);
            NuGetRestore(solutionPath, new NuGetRestoreSettings
            {
                Source = sources
            });
        }
        catch (System.Exception exc)
        {
            LogExceptionToSlack($"Build {buildId} failed during NuGet package restore. Rebuild will possibly fix this issue.", exc);
            throw;
        }
    });

Task("Clean")
    .IsDependentOn("CleanBuild")
    .IsDependentOn("RestorePackages")
    .Does(() => 
    {
        //NOP
    });

Task("SetMetadata")
  .Does(() => 
{
  try
  {
    var nuspecVersion = XmlPeek("./src/package.nuspec", "/package/metadata/version/text()");
    var fileVersion = nuspecVersion.Replace("$versionSuffix$", buildIdStr);
    var version = nuspecVersion.Replace("$versionSuffix$",$"{buildId}{betaSuffix}");   

    var files = System.IO.Directory.GetFiles(System.IO.Directory.GetCurrentDirectory(), "AssemblyInfo.cs", SearchOption.AllDirectories); 
    foreach(var file in files)
    {
      var assemblyInfo = ParseAssemblyInfo(File(file));
      CreateAssemblyInfo(file, new AssemblyInfoSettings
      {
        Title = assemblyInfo.Title,
        Product = assemblyInfo.Title,
        Company = company,
        Version = fileVersion,
        FileVersion = fileVersion,
        InformationalVersion = version,
        Copyright = string.Format("Copyright (c) {0}, {1}", DateTime.Now.Year, company)
      });
    }
  }
  catch(System.Exception exc)
  {
    LogExceptionToSlack($"Build {buildId} failed during metadata update.", exc);
    throw;
  }
});

Task("BuildSolution")
    .IsDependentOn("SetMetadata")
    .IsDependentOn("RestorePackages")
    .Does(() => 
    {
        try
        {
            Information("Building solution");
            MSBuild(solutionPath, conf =>
            { conf
                .SetConfiguration(buildConfig);

                if(minimalVerbosity)
                {
                    conf.SetVerbosity(Verbosity.Minimal);
                }
            });

            DeleteFiles("./*.nupkg");
        }
        catch (System.Exception exc)
        {
            LogExceptionToSlack($"Build {buildId} failed during build.", exc);
            throw;
        }
    });

Task("CreateNuGetPackage")
    .IsDependentOn("BuildSolution")
    .Does(() => 
    {
        try
        {
            var properties = new Dictionary<string,string>();
            properties.Add("VersionSuffix", $"{buildId}{betaSuffix}");
            properties.Add("Configuration", buildConfig);

            var nuGetPackSettings = new NuGetPackSettings { Properties = properties } ;
            NuGetPack("./src/package.nuspec", nuGetPackSettings);
        }
        catch (System.Exception exc)
        {
            LogExceptionToSlack($"Build {buildId} failed during NuGet packaging.", exc);
            throw;
        }
    });

Task("PushNuGetPackage")
    .IsDependentOn("CreateNuGetPackage")
    .Does(() => 
    {
        if(string.IsNullOrEmpty(nugetPublishingFeed) || string.IsNullOrEmpty(nugetApiKey))
        {
          Warning("Not pushing NuGet package because NuGet info is missing.");
          return;
        }
        
        try
        {
            var package = GetFiles("*.nupkg").First();

            NuGetPush(package, new NuGetPushSettings {
              Source = nugetPublishingFeed,
              ApiKey = nugetApiKey
            });
        }
        catch (System.Exception exc)
        {
            LogExceptionToSlack($"Build {buildId} failed during NuGet publish.", exc);
            throw;
        }
    });

Task("Default")
  .IsDependentOn("PushNuGetPackage")
  .Does(() =>
{
        Information($"Finished building and publishing {buildConfig} package");
        LogToSlack($"Finished building and publishing {projectName} {buildId} in config {buildConfig}");
        if(!string.IsNullOrEmpty(slackHook) && !string.IsNullOrEmpty(slackChannel))
        {
            Slack.Chat.PostMessage(messageSettings: slackMessageSettings, channel: slackChannel, text: ":white_check_mark: Build succesful", messageAttachments: new SlackChatMessageAttachment[]
            {
                new SlackChatMessageAttachment
                {
                    Color = "#00FF00",
                    Text = $"Build {buildId}",
                    Fields = new SlackChatMessageAttachmentField[]
                    {
                        new SlackChatMessageAttachmentField
                        {
                            Title = "Branch",
                            Value = gitBranch
                        },
                        new SlackChatMessageAttachmentField
                        {
                            Title = "Author",
                            Value = gitcommitAuthor
                        },
                        new SlackChatMessageAttachmentField
                        {
                            Title = "Commit",
                            Value = $"{gitCommitMessage} ({gitCommitSha})"
                        }
                    }
                }
            });
        }
});

Task("NoPublish")
  .IsDependentOn("CreateNuGetPackage")
  .Does(() =>
{
        Information($"Finished building {buildConfig} package");
        LogToSlack($"Finished building {projectName} {buildId} in config {buildConfig}");
        if(!string.IsNullOrEmpty(slackHook) && !string.IsNullOrEmpty(slackChannel))
        {
            Slack.Chat.PostMessage(messageSettings: slackMessageSettings, channel: slackChannel, text: ":white_check_mark: Build succesful", messageAttachments: new SlackChatMessageAttachment[]
            {
                new SlackChatMessageAttachment
                {
                    Color = "#00FF00",
                    Text = $"Build {buildId}",
                    Fields = new SlackChatMessageAttachmentField[]
                    {
                        new SlackChatMessageAttachmentField
                        {
                            Title = "Branch",
                            Value = gitBranch
                        },
                        new SlackChatMessageAttachmentField
                        {
                            Title = "Author",
                            Value = gitcommitAuthor
                        },
                        new SlackChatMessageAttachmentField
                        {
                            Title = "Commit",
                            Value = $"{gitCommitMessage} ({gitCommitSha})"
                        }
                    }
                }
            });
        }
});

RunTarget(target);