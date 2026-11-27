using MyFashion.Core.Profile;
using MyFashion.Core.Recommendations;

namespace MyFashion.Core.Application;

/// <summary>An account in the list on the home page.</summary>
public sealed record AccountOverview(string Username, int Runs, DateTimeOffset? LastRunAt, int AnalyzedImages);

/// <summary>A run as shown in the UI.</summary>
public sealed record RunInfo(
    int Id,
    string Username,
    RunSource Source,
    RunStatus Status,
    string Model,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error,
    RunCounts Counts);

/// <summary>Read-only queries for the UI.</summary>
public interface IAnalysisReader
{
    Task<IReadOnlyList<AccountOverview>> GetAccountsAsync(CancellationToken cancellationToken);

    /// <summary>Every analyzed image with an outfit for the account, across runs, one per distinct image.</summary>
    Task<IReadOnlyList<AnalyzedImage>> GetImagesAsync(string username, CancellationToken cancellationToken);

    Task<IReadOnlyList<RunInfo>> GetRunsAsync(string username, CancellationToken cancellationToken);
}

/// <summary>Stores the user's own preferences (one set per installation).</summary>
public interface IPreferencesStore
{
    Task<UserPreferences> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken);
}
