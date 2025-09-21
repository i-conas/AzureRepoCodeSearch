using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AzureRepoSearchApp;

class Program
{
    private static string Organization = string.Empty;
    private static string PersonalAccessToken = string.Empty;
    private static string SearchText = string.Empty;

    private static readonly HttpClient httpClient = new();
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static async Task Main(string[] args)
    {
        // Load configuration from appsettings.json
        var configuration = LoadConfiguration();
        LoadConfigurationValues(configuration);

        Console.WriteLine("Azure DevOps Repository Search Tool");
        Console.WriteLine("===================================");
        Console.WriteLine($"Searching for: '{SearchText}'");
        Console.WriteLine($"Organization: {Organization}");
        Console.WriteLine();

        try
        {
            // Setup HTTP client
            SetupHttpClient();

            // Get all projects
            var projects = await GetProjectsAsync();
            Console.WriteLine($"Found {projects.Count} projects");
            Console.WriteLine();

            projects = projects.Where(p => p.Name.Equals("Online-Campus", StringComparison.OrdinalIgnoreCase)).ToList();

            // Search in each project
            foreach (var project in projects)
            {
                Console.WriteLine($"Searching in project: {project.Name}");
                await SearchInProjectAsync(project);
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        Console.WriteLine("Search completed. Press any key to exit...");
        Console.ReadLine();
    }

    private static IConfiguration LoadConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        return builder.Build();
    }

    private static void LoadConfigurationValues(IConfiguration configuration)
    {
        Organization = configuration["AzureDevOps:Organization"] ?? throw new InvalidOperationException("Organization not found in configuration");
        PersonalAccessToken = configuration["AzureDevOps:PersonalAccessToken"] ?? throw new InvalidOperationException("PersonalAccessToken not found in configuration");
        SearchText = configuration["AzureDevOps:SearchText"] ?? throw new InvalidOperationException("SearchText not found in configuration");

        // Validate required configuration values
        if (string.IsNullOrWhiteSpace(Organization))
            throw new InvalidOperationException("Organization cannot be empty");

        if (string.IsNullOrWhiteSpace(PersonalAccessToken))
            throw new InvalidOperationException("PersonalAccessToken cannot be empty");

        if (string.IsNullOrWhiteSpace(SearchText))
            throw new InvalidOperationException("SearchText cannot be empty");

        Console.WriteLine("Configuration loaded successfully from appsettings.json");
    }

    private static void SetupHttpClient()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{PersonalAccessToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static async Task<List<Project>> GetProjectsAsync()
    {
        var url = $"https://dev.azure.com/{Organization}/_apis/projects?api-version=7.1";

        try
        {
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ProjectsResponse>(jsonString, jsonOptions);

            return result?.Value ?? new List<Project>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting projects: {ex.Message}");
            return new List<Project>();
        }
    }

    private static async Task SearchInProjectAsync(Project project)
    {
        try
        {
            // Get repositories for this project
            var repositories = await GetRepositoriesAsync(project.Id);

            if (repositories.Count == 0)
            {
                Console.WriteLine("  No repositories found");
                return;
            }

            Console.WriteLine($"  Found {repositories.Count} repositories");

            repositories = repositories.Where(r => r.Name.Equals("OnlineCampus.BackendServices", StringComparison.OrdinalIgnoreCase)).ToList();

            // Search in each repository
            foreach (var repo in repositories)
            {
                await SearchInRepositoryAsync(project, repo);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error searching in project {project.Name}: {ex.Message}");
        }
    }

    private static async Task<List<Repository>> GetRepositoriesAsync(string projectId)
    {
        var url = $"https://dev.azure.com/{Organization}/{projectId}/_apis/git/repositories?api-version=7.1";

        try
        {
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<RepositoriesResponse>(jsonString, jsonOptions);

            return result?.Value ?? new List<Repository>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Error getting repositories: {ex.Message}");
            return new List<Repository>();
        }
    }

    private static async Task SearchInRepositoryAsync(Project project, Repository repository)
    {
        // Try multiple search endpoints as different organizations might use different URLs
        var searchUrls = new[]
        {
            $"https://almsearch.dev.azure.com/{Organization}/_apis/search/codesearchresults?api-version=7.1-preview.1",
            //$"https://dev.azure.com/{Organization}/_apis/search/codesearchresults?api-version=7.1-preview.1"
        };

        foreach (var searchUrl in searchUrls)
        {
            try
            {
                Console.WriteLine($"    Trying search endpoint: {searchUrl.Split('/')[2]}");

                if (await TrySearchWithUrl(searchUrl, project, repository))
                {
                    return; // Success, no need to try other URLs
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Search failed with {searchUrl.Split('/')[2]}: {ex.Message}");
            }
        }

        // If all search APIs fail, fall back to file-by-file search
        Console.WriteLine($"    All search APIs failed for {repository.Name}, trying file-by-file search...");
        await SearchInRepositoryFilesAsync(project, repository);
    }

    private static async Task<bool> TrySearchWithUrl(string searchUrl, Project project, Repository repository)
    {
        try
        {
            // Create the search request with exact property names expected by Azure DevOps API
            var searchRequest = new Dictionary<string, object>
            {
                { "searchText", SearchText },
                { "$skip", 0 },
                { "$top", 100 },
                { "filters", new Dictionary<string, List<string>>
                    {
                        { "Project", new List<string> { project.Name } },
                        { "Repository", new List<string> { repository.Name } }
                    }
                },
                { "includeFacets", true }
            };

            var jsonContent = JsonSerializer.Serialize(searchRequest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = null, // Don't transform property names
                WriteIndented = false
            });
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            Console.WriteLine($"    Executing URL: {searchUrl}");
            Console.WriteLine($"    JSON Payload: {jsonContent}");

            var response = await httpClient.PostAsync(searchUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();

                // Debug: Print the actual JSON response to understand its structure
                Console.WriteLine($"    Raw JSON Response: {jsonString}");

                try
                {
                    var searchResult = JsonSerializer.Deserialize<CodeSearchResponse>(jsonString, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });

                    if (searchResult?.Results != null && searchResult.Results.Count > 0)
                    {
                        Console.WriteLine($"    ✓ Repository: {repository.Name} ({searchResult.Results.Count} matches)");

                        foreach (var result in searchResult.Results)
                        {
                            Console.WriteLine($"      - File: {result.Path}");
                            Console.WriteLine($"        Content Matches: {result.Matches?.Content?.Count ?? 0}");

                            if (result.Matches?.Content != null && result.Matches.Content.Count > 0)
                            {
                                foreach (var match in result.Matches.Content.Take(3)) // Show first 3 matches
                                {
                                    Console.WriteLine($"        Char Offset {match.CharOffset}: Length {match.Length}, Line {match.Line}, Type: {match.Type}");
                                }
                                if (result.Matches.Content.Count > 3)
                                {
                                    Console.WriteLine($"        ... and {result.Matches.Content.Count - 3} more matches");
                                }
                            }
                            Console.WriteLine();
                        }
                        return true; // Success
                    }
                    else
                    {
                        Console.WriteLine($"    No matches found in {repository.Name}");
                        return true; // API worked, just no results
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"    JSON Deserialization Error: {ex.Message}");
                    Console.WriteLine($"    Trying alternative JSON parsing...");

                    // Try to parse with JsonDocument for more flexible handling
                    try
                    {
                        return ParseSearchResponseAlternative(jsonString, repository.Name);
                    }
                    catch (Exception altEx)
                    {
                        Console.WriteLine($"    Alternative parsing also failed: {altEx.Message}");
                        Console.WriteLine($"    Raw response: {jsonString.Substring(0, Math.Min(500, jsonString.Length))}...");
                        return false;
                    }
                }
            }
            else
            {
                // Log the error details
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"    Search API failed:");
                Console.WriteLine($"    Status: {response.StatusCode}");
                Console.WriteLine($"    Error: {errorContent}");
                return false; // Failed
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Exception during search: {ex.Message}");
            return false; // Failed
        }
    }

    private static bool ParseSearchResponseAlternative(string jsonString, string repositoryName)
    {
        using var document = JsonDocument.Parse(jsonString);
        var root = document.RootElement;

        if (root.TryGetProperty("results", out var resultsElement) && resultsElement.GetArrayLength() > 0)
        {
            Console.WriteLine($"    ✓ Repository: {repositoryName} ({resultsElement.GetArrayLength()} files with matches)");

            foreach (var result in resultsElement.EnumerateArray())
            {
                var path = result.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : "Unknown";
                Console.WriteLine($"      - File: {path}");

                if (result.TryGetProperty("matches", out var matchesElement))
                {
                    if (matchesElement.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.Array)
                    {
                        var matchCount = contentElement.GetArrayLength();
                        Console.WriteLine($"        Content Matches: {matchCount}");

                        var displayed = 0;
                        foreach (var match in contentElement.EnumerateArray())
                        {
                            if (displayed >= 3) break;

                            var charOffset = match.TryGetProperty("charOffset", out var offsetElement) ? offsetElement.GetInt32() : 0;
                            var length = match.TryGetProperty("length", out var lengthElement) ? lengthElement.GetInt32() : 0;
                            var line = match.TryGetProperty("line", out var lineElement) ? lineElement.GetInt32() : 0;
                            var type = match.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "";

                            Console.WriteLine($"        Char Offset {charOffset}: Length {length}, Line {line}, Type: {type}");
                            displayed++;
                        }

                        if (matchCount > 3)
                        {
                            Console.WriteLine($"        ... and {matchCount - 3} more matches");
                        }
                    }
                    else
                    {
                        // Handle case where matches structure is different
                        Console.WriteLine($"        Matches structure: {matchesElement.ValueKind}");
                        Console.WriteLine($"        Raw matches: {matchesElement.GetRawText().Substring(0, Math.Min(200, matchesElement.GetRawText().Length))}...");
                    }
                }
                Console.WriteLine();
            }
            return true;
        }
        else
        {
            Console.WriteLine($"    No matches found in {repositoryName}");
            return true;
        }
    }

    private static async Task SearchInRepositoryFilesAsync(Project project, Repository repository)
    {
        try
        {
            // Get all items in the repository
            var itemsUrl = $"https://dev.azure.com/{Organization}/{project.Id}/_apis/git/repositories/{repository.Id}/items?recursionLevel=Full&api-version=7.1";

            var response = await httpClient.GetAsync(itemsUrl);
            if (!response.IsSuccessStatusCode) return;

            var jsonString = await response.Content.ReadAsStringAsync();
            var itemsResult = JsonSerializer.Deserialize<ItemsResponse>(jsonString, jsonOptions);

            if (itemsResult?.Value == null) return;

            var files = itemsResult.Value.Where(item => !item.IsFolder && IsTextFile(item.Path)).ToList();
            var matchingFiles = new List<string>();

            foreach (var file in files)
            {
                if (await SearchInFileAsync(project.Id, repository.Id, file.Path))
                {
                    matchingFiles.Add(file.Path);
                }
            }

            if (matchingFiles.Count > 0)
            {
                Console.WriteLine($"    ✓ Repository: {repository.Name} ({matchingFiles.Count} files contain '{SearchText}')");
                foreach (var file in matchingFiles)
                {
                    Console.WriteLine($"      - {file}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Error searching repository files: {ex.Message}");
        }
    }

    private static async Task<bool> SearchInFileAsync(string projectId, string repositoryId, string filePath)
    {
        try
        {
            var fileUrl = $"https://dev.azure.com/{Organization}/{projectId}/_apis/git/repositories/{repositoryId}/items?path={Uri.EscapeDataString(filePath)}&api-version=7.1";

            var response = await httpClient.GetAsync(fileUrl);
            if (!response.IsSuccessStatusCode) return false;

            var content = await response.Content.ReadAsStringAsync();
            return content.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTextFile(string path)
    {
        var textExtensions = new[] { ".cs", ".js", ".ts", ".html", ".css", ".json", ".xml", ".txt", ".md", ".yml", ".yaml", ".sql", ".py", ".java", ".cpp", ".h", ".c" };
        return textExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }
}

// Data models using System.Text.Json attributes
public class ProjectsResponse
{
    public List<Project> Value { get; set; } = new();
}

public class Project
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class RepositoriesResponse
{
    public List<Repository> Value { get; set; } = new();
}

public class Repository
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public Project Project { get; set; } = new();
}

public class CodeSearchRequest
{
    public string SearchText { get; set; } = string.Empty;
    public bool IncludeFacets { get; set; }
    public int Top { get; set; }
    public Dictionary<string, List<string>> Filters { get; set; } = new();
}

public class CodeSearchResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("results")]
    public List<CodeSearchResult> Results { get; set; } = new();
    
    [JsonPropertyName("infoCode")]
    public int InfoCode { get; set; }
    
    [JsonPropertyName("facets")]
    public JsonElement? Facets { get; set; }
}

public class CodeSearchResult
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("matches")]
    public MatchesContainer Matches { get; set; } = new();

    [JsonPropertyName("collection")]
    public Collection Collection { get; set; } = new();

    [JsonPropertyName("project")]
    public Project Project { get; set; } = new();

    [JsonPropertyName("repository")]
    public Repository Repository { get; set; } = new();
    
    [JsonPropertyName("versions")]
    public List<Version> Versions { get; set; } = new();
    
    [JsonPropertyName("contentId")]
    public string ContentId { get; set; } = string.Empty;
}

public class MatchesContainer
{
    [JsonPropertyName("content")]
    public List<ContentMatch> Content { get; set; } = new();
    
    [JsonPropertyName("fileName")]
    public List<object> FileName { get; set; } = new();
}

public class ContentMatch
{
    [JsonPropertyName("charOffset")]
    public int CharOffset { get; set; }

    [JsonPropertyName("length")]
    public int Length { get; set; }

    [JsonPropertyName("line")]
    public int Line { get; set; }
    
    [JsonPropertyName("column")]
    public int Column { get; set; }
    
    [JsonPropertyName("codeSnippet")]
    public string? CodeSnippet { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public class Collection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class Version
{
    [JsonPropertyName("branchName")]
    public string BranchName { get; set; } = string.Empty;
    
    [JsonPropertyName("changeId")]
    public string ChangeId { get; set; } = string.Empty;
}

public class ItemsResponse
{
    public List<GitItem> Value { get; set; } = new();
}

public class GitItem
{
    public string Path { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public string Url { get; set; } = string.Empty;
}
