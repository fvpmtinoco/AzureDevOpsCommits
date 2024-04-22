using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureDevOpsCommits.Settings
{
    public record DevOpsCredentials
    {
        public string PAT { get; init; }
        public string BaseUrl { get; init; }
    }

    public record FileExtensions
    {
        public List<string> AdmissibleExtensions { get; init; }
    }

    public record Repositories
    {
        public List<string> Names { get; init; }
        public DateTime StartDate { get; init; } = new DateTime(2023, 1, 1);
    }

    public record SerilogSettings
    {
        public MinimumLevelSettings MinimumLevel { get; init; }
        public List<SinkSettings> WriteTo { get; init; }
    }

    public record MinimumLevelSettings
    {
        public string Default { get; init; }
        public Dictionary<string, string> Override { get; init; }
    }

    public record SinkSettings
    {
        public string Name { get; init; }
        public Dictionary<string, string> Args { get; init; }
    }
}
