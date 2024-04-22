using AzureDevOpsCommits.Models;
using AzureDevOpsCommits.Settings;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

public static class Program
{
    private static DevOpsCredentials? devOpsSettings;
    private static FileExtensions? fileExtensions;
    private static Repositories? repositories;
    private static SerilogSettings? logging;

    private static readonly HttpClient client = new HttpClient();
    private static string baseUrl = string.Empty;
    private static List<string> admissableExtensions = null;

    static async Task Main(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);
        ConfigureHostAndServices(builder);
        ConfigureLogger();

        baseUrl = devOpsSettings.BaseUrl;
        admissableExtensions = fileExtensions.AdmissibleExtensions;

        SetAuthorizationHeader(client);
        var repositoriesToScan = await RetrieveAvailableRepositories(repositories);
        _ = await ProcessRepositories(repositoriesToScan);
    }

    static void SetAuthorizationHeader(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{devOpsSettings.PAT}")));
    }

    static async Task<List<DevOpsRepository>> ProcessRepositories(List<DevOpsRepository> repositoriesToScan)
    {
        foreach (var repo in repositoriesToScan)
        {
            Log.Debug("Processing repository {@repoName}...", repo.Name);
            var pullRequests = await GetMergedPullRequestsAsync(repo.Id, "master");

            List<Commit> commits = new();
            foreach (var pr in pullRequests)
            {
                commits.Add(new Commit
                {
                    AuthorName = pr.Author,
                    commitId = pr.MergeTargetCommitId,
                });
            }

            Log.Debug("Found {@count} completed pull requests for repository {@repoName} since {@startDate}", commits.Count, repo.Name, repositories.StartDate);            
            repo.PullRequests = await ProcessCommitDiffs(repo.Id, repo.Name, pullRequests);
        }
        return repositoriesToScan;
    }

    static async Task<List<DevOpsRepository>> RetrieveAvailableRepositories(Repositories repositories)
    {
        var responseString = await client.GetStringAsync(baseUrl + "git/repositories?api-version=6.0");
        var availableRepos = JsonSerializer.Deserialize<DevOpsRepositoriesResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var respositoriesToScan = availableRepos.Repos.FindAll(r => repositories.Names.Contains(r.Name));

        return respositoriesToScan;
    }

    public static async Task<List<PullRequest>> GetMergedPullRequestsAsync(string repositoryId, string targetBranch, int top = 100)
    {
        List<PullRequest> mergedPullRequests = new List<PullRequest>();
        int skip = 0;
        bool moreAvailable = true;

        while (moreAvailable)
        {
            string url = $"{baseUrl}git/repositories/{repositoryId}/pullrequests?searchCriteria.targetRefName=refs/heads/{targetBranch}&searchCriteria.status=completed&$top={top}&$skip={skip}&api-version=7.0";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync();
            var responseObject = JsonSerializer.Deserialize<PullRequestsResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (responseObject.value.Any())
            {
                mergedPullRequests.AddRange(responseObject.value.Where(pr => pr.CompletionDate >= repositories.StartDate));
                skip += top;
            }
            else
                moreAvailable = false;
        }

        return mergedPullRequests.OrderByDescending(pr => pr.CompletionDate).ToList();
    }

    static async Task<List<PullRequest>> ProcessCommitDiffs(string repositoryId, string repositoryName, List<PullRequest> pullRequests)
    {
        // From first to last commit
        pullRequests.Reverse();

        // Start from the second commit so that there is a "previous" commit to compare with.
        for (int i = 1; i < pullRequests.Count; i++)
        {
            string currentPullRequestId = pullRequests[i].MergeTargetCommitId;
            string previousPullRequestId = pullRequests[i - 1].MergeTargetCommitId;

            // Fetch and process the diff between currentCommitId and previousCommitId
            pullRequests[i - 1].CommitedLines = await GetCommitDiffAsync(repositoryId, previousPullRequestId, currentPullRequestId);

            using (LogContext.PushProperty("RepositoryName", repositoryName))
            using (LogContext.PushProperty("PRDate", pullRequests[i - 1].CompletionDate))
            using (LogContext.PushProperty("PRAuthor", pullRequests[i - 1].Author))
            using (LogContext.PushProperty("PRCommitedLines", pullRequests[i - 1].CommitedLines))
                Log.Information("Processing {0}/{1}: {@prName} - Commited lines {@commitedLines}", i, pullRequests.Count, pullRequests[i - 1].PullRequestId, pullRequests[i - 1].CommitedLines);
        }

        return pullRequests;
    }

    static async Task<List<Change>> GetMergedPullRequestChangesAsync(string repositoryId, string baseCommitId, string targetCommitId, int top = 100)
    {
        List<Change> mergedChanges = new();
        bool moreAvailable = true;
        int skip = 0;

        while (moreAvailable)
        {
            string url = $"{baseUrl}git/repositories/{repositoryId}/diffs/commits?$top={top}&$skip={skip}&baseVersion={baseCommitId}&targetVersion={targetCommitId}&baseVersionType=commit&targetVersionType=commit&api-version=7.0";
            var responseString = await client.GetStringAsync(url);
            var responseObject = JsonSerializer.Deserialize<CommitDetailsResponse>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (responseObject.changes.Any())
            {
                mergedChanges.AddRange(responseObject.changes);
                skip += top;
            }
            else
                moreAvailable = false;
        }

        return mergedChanges;
    }

    static async Task<int> GetCommitDiffAsync(string repositoryId, string baseCommitId, string targetCommitId)
    {
        int changedLines = 0;

        var changes = await GetMergedPullRequestChangesAsync(repositoryId, baseCommitId, targetCommitId);

        if (!changes.Any())
            return changedLines;

        var editedFiles = changes.Where(x => !x.item.isFolder && !(new string[] { "designer.cs", "reference.cs" }.Contains(Path.GetFileName(x.item.path).ToLower())) && admissableExtensions.Contains(Path.GetExtension(x.item.path))).ToList();

        var diffBuilder = new InlineDiffBuilder(new Differ());

        string ReplaceUrlPath(string originalUrl, string originalPath, string newPath)
        {
            // URL Encode the newPath
            string encodedNewPath = Uri.EscapeDataString(newPath.Substring(1));

            // Replace the original path in the URL
            string originalEncodedPath = Uri.EscapeDataString(originalPath.Substring(1));

            // Replace the path
            string updatedUrl = originalUrl.Replace(originalEncodedPath, encodedNewPath);

            return updatedUrl;
        }

        int DiffFiles(string? baseCommit, string? targetCommit)
        {
            int diffLines = 0;
            var diff = diffBuilder.BuildDiffModel(baseCommit, targetCommit);

            foreach (var line in diff.Lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Inserted:
                    case ChangeType.Modified:
                        diffLines++;
                        break;
                    case ChangeType.Deleted:
                        diffLines--;
                        break;
                }
            }

            return diffLines;
        }

        foreach (var file in editedFiles)
        {
            try
            {
                string? targetCommit;
                string? baseCommit;
                int? lineCount;
                switch (file.changeType)
                {
                    case "delete":
                        baseCommit = await client.GetStringAsync(file.item.url.Replace($"version={targetCommitId}", $"version={baseCommitId}"));
                        lineCount = baseCommit.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        changedLines -= lineCount.GetValueOrDefault(0);
                        break;
                    case "add":
                        targetCommit = await client.GetStringAsync(file.item.url);
                        lineCount = targetCommit.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                        changedLines += lineCount.GetValueOrDefault(0);
                        break;
                    case "edit":
                        targetCommit = await client.GetStringAsync(file.item.url);
                        baseCommit = await client.GetStringAsync(file.item.url.Replace($"version={targetCommitId}", $"version={baseCommitId}"));
                        changedLines += DiffFiles(baseCommit, targetCommit);
                        break;
                    case "edit, rename":
                        if (!string.IsNullOrEmpty(file.SourceServerItem))
                        {
                            targetCommit = await client.GetStringAsync(file.item.url);
                            var replacedUrl = ReplaceUrlPath(file.item.url, file.item.path, file.SourceServerItem).Replace($"version={targetCommitId}", $"version={baseCommitId}");
                            baseCommit = await client.GetStringAsync(replacedUrl);

                            changedLines += DiffFiles(baseCommit, targetCommit);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error in GetCommitDiffAsync for repositoy name {@repoId} from commit {@baseCommitId} to {@targetCommitId}: {error}", repositoryId, baseCommitId, targetCommitId, ex.Message);
            }
        }

        return changedLines;
    }

    static void ConfigureLogger()
    {
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Information);

        foreach (var sink in logging.WriteTo)
        {
            if (sink.Name == "Console")
            {
                loggerConfiguration.WriteTo.Console();
            }
            else if (sink.Name == "LokiHttp" && sink.Args.TryGetValue("serverUrl", out var serverUrl))
            {
                var applicationLabel = new LokiLabel()
                {
                    Key = "Application",
                    Value = Assembly.GetExecutingAssembly().GetName().Name
                };

                loggerConfiguration.WriteTo.GrafanaLoki(uri: serverUrl, labels: new List<LokiLabel>() { applicationLabel });
            }
        }

        Log.Logger = loggerConfiguration.CreateLogger();
    }

    static void ConfigureHostAndServices(IHostBuilder builder)
    {
        var host = builder
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.development.json", optional: true);
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.Configure<DevOpsCredentials>(hostContext.Configuration.GetSection("DevOpsCredentials"));
                services.Configure<FileExtensions>(hostContext.Configuration.GetSection("FileExtensions"));
                services.Configure<Repositories>(hostContext.Configuration.GetSection("Repositories"));
                services.Configure<SerilogSettings>(hostContext.Configuration.GetSection("Serilog"));
            })
            .Build();

        devOpsSettings = host.Services.GetRequiredService<IOptions<DevOpsCredentials>>().Value;
        fileExtensions = host.Services.GetRequiredService<IOptions<FileExtensions>>().Value;
        repositories = host.Services.GetRequiredService<IOptions<Repositories>>().Value;
        logging = host.Services.GetRequiredService<IOptions<SerilogSettings>>().Value;
    }
}