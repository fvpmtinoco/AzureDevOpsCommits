using System.Text.Json.Serialization;

namespace AzureDevOpsCommits.Models
{
    public class DevOpsRepository
    {
        [JsonPropertyName("id")]
        public string Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; }

        public List<PullRequest> PullRequests { get; set; }
    }

    public class Commit
    {
        public string commitId { get; set; }
        public Author author { get; set; }
        public int commitedLines { get; set; }
        public string AuthorName { get; set; }
    }

    public class Author
    {
        public string name { get; set; }
        public string email { get; set; }
    }

    public class ChangeCounts
    {
        public int Add { get; set; }
        public int Edit { get; set; }
    }

    public class Change
    {
        public Item item { get; set; }
        public string changeType { get; set; }
        //When changeType is "edit, renamed", this property is present
        public string SourceServerItem { get; set; }
    }

    public class Item
    {
        public string url { get; set; }
        public bool isFolder { get; set; } = false;
        public string path { get; set; }
    }

    public class DevOpsRepositoriesResponse
    {
        [JsonPropertyName("value")]
        public List<DevOpsRepository> Repos { get; set; }
    }

    public class CommitsResponse
    {
        public int count { get; set; }
        public List<Commit> value { get; set; }
    }

    public class CommitDetailsResponse
    {
        public ChangeCounts changeCounts { get; set; }
        public List<Change> changes { get; set; }
    }

    public class Reviewer
    {
        public string UniqueName { get; set; }
        public string DisplayName { get; set; }
        public int Vote { get; set; } // The review decision/status
        // Other properties as needed
    }

    public class ReviewersResponse
    {
        public List<Reviewer> Value { get; set; }
    }

    public class PullRequest
    {
        public int PullRequestId { get; set; }
        public string Status { get; set; }
        [JsonPropertyName("closedDate")]
        public DateTime CompletionDate { get; set; }
        [JsonPropertyName("lastMergeTargetCommit")]
        public MergeTargetCommit MergeTargetCommit { get; set; }

        // Nested class to match the JSON structure
        public CreatedBy CreatedBy { get; set; }

        // Additional property to directly expose Author
        public string Author => CreatedBy?.DisplayName;

        // Additional property to directly expose MergeTargetCommitId
        public string MergeTargetCommitId => MergeTargetCommit?.Id;

        public int CommitedLines { get; set; }
    }

    public class PullRequestsResponse
    {
        public List<PullRequest> value { get; set; }
    }

    public class MergeTargetCommit
    {
        [JsonPropertyName("commitId")]
        public string Id { get; set; }
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    public class CreatedBy
    {
        public string DisplayName { get; set; }
    }
}
