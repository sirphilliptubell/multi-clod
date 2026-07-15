using System.Reflection;

namespace MultiClod.App.Updates;

/// <summary>
/// Where this build checks for Velopack updates (a GitHub repo URL for <see
/// cref="Velopack.Sources.GithubSource"/>) - baked into the exe at publish time as AssemblyMetadata
/// (see MultiClodUpdateFeedUrl in the csproj). Empty/missing for Debug builds - see the csproj's
/// Configuration-gated default - so plain `dotnet build`/F5 debugging never checks GitHub for
/// updates.
/// </summary>
public static class UpdateFeedLocation {
	private const string BakedInMetadataKey = "MultiClodUpdateFeedUrl";

	public static string? Path { get; } = Resolve(
		Assembly.GetExecutingAssembly()
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(a => a.Key == BakedInMetadataKey)?.Value);

	/// <summary>
	/// Pure decision logic, kept separate from the real assembly read above so tests can exercise
	/// "baked-in set" / "baked-in null" / "baked-in empty" directly.
	/// </summary>
	public static string? Resolve(string? bakedInValue) =>
		string.IsNullOrEmpty(bakedInValue) ? null : bakedInValue;
}
