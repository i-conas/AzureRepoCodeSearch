# Azure DevOps Repository Search Tool

This .NET Core console application searches for arbitrary text across all repositories in your Azure DevOps organization using the Azure DevOps REST API.

## Features

- ✅ Uses **System.Text.Json** (not Newtonsoft.Json)
- 🔍 Searches across all projects and repositories in your organization
- 📁 Shows which files contain the search text
- 📍 Displays line numbers and context for matches
- 🛡️ Uses Personal Access Token (PAT) authentication
- 📊 Provides detailed console output with search results

## Setup

1. **Update Configuration**: Edit the constants in `Program.cs` or use `appsettings.json`:
   ```csharp
   private const string Organization = "your-organization"; // Your Azure DevOps organization
   private const string PersonalAccessToken = "your-pat-token"; // Your PAT
   private const string SearchText = "TODO"; // Text to search for
   ```

2. **Personal Access Token**: 
   - Go to Azure DevOps → User Settings → Personal Access Tokens
   - Create a new token with **Code (read)** permissions
   - Copy the token and replace `your-pat-token` in the code

3. **Organization Name**:
   - Replace `your-organization` with your Azure DevOps organization name
   - This is the name that appears in your Azure DevOps URL: `https://dev.azure.com/YOUR_ORGANIZATION`

## Usage

1. **Build the application**:
   ```bash
   dotnet build
   ```

2. **Run the application**:
   ```bash
   dotnet run
   ```

## How It Works

1. **Authentication**: Uses Basic authentication with your Personal Access Token
2. **Project Discovery**: Retrieves all projects in your organization
3. **Repository Discovery**: Gets all repositories within each project
4. **Search Methods**: 
   - Primary: Uses Azure DevOps Code Search API (if available)
   - Fallback: Downloads and searches individual files
5. **Results**: Displays matching files with line numbers and context

## Sample Output

```
Azure DevOps Repository Search Tool
===================================
Searching for: 'TODO'
Organization: mycompany

Found 5 projects

Searching in project: WebApplication
  Found 3 repositories
    ✓ Repository: frontend-app (12 matches)
      - File: /src/components/Header.tsx
        Matches: 2
        Line 15: // TODO: Add user authentication
        Line 42: // TODO: Implement dark mode toggle

    ✓ Repository: backend-api (8 matches)
      - File: /Controllers/UserController.cs
        Matches: 3
        Line 25: // TODO: Add input validation
        Line 67: // TODO: Implement caching
        Line 89: // TODO: Add error handling
```

## Dependencies

- **System.Text.Json**: For JSON serialization/deserialization
- **Microsoft.Extensions.Http**: For HTTP client factory
- **Microsoft.Extensions.Logging**: For logging capabilities
- **Microsoft.Extensions.Configuration**: For configuration management

## API Permissions Required

Your Personal Access Token needs the following scopes:
- **Code (read)**: To access repository content and search
- **Project and team (read)**: To list projects and repositories

## Troubleshooting

1. **401 Unauthorized**: Check your Personal Access Token and ensure it has correct permissions
2. **403 Forbidden**: Your token might not have access to specific projects
3. **Code Search Not Available**: The application will fallback to file-by-file search
4. **Rate Limiting**: The application includes basic error handling for API rate limits

## Extending the Application

You can easily extend this application to:
- Search for multiple terms
- Filter by file types
- Export results to CSV/JSON
- Add regex pattern matching
- Include/exclude specific projects or repositories
