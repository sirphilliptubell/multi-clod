using System.Reflection;

namespace MultiClod.App.Updates;

/// <summary>
/// Where this build checks for Velopack updates - baked into the exe at publish time as
/// AssemblyMetadata (see MultiClodUpdateFeedPath in the csproj), sourced from the
/// MULTICLOD_DEPLOY_PATH environment variable on the publishing machine and never committed to
/// source control. An optional MULTICLOD_UPDATE_FEED_PATH environment variable on the *running*
/// machine overrides the baked-in value, giving a zero-infrastructure escape hatch if the share
/// ever needs to move without republishing every existing install.
/// </summary>
public static class UpdateFeedLocation {
	private const string BakedInMetadataKey = "MultiClodUpdateFeedPath";
	private const string RuntimeOverrideEnvVar = "MULTICLOD_UPDATE_FEED_PATH";

	public static string? Path { get; } = Resolve(
		Environment.GetEnvironmentVariable(RuntimeOverrideEnvVar),
		Assembly.GetExecutingAssembly()
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(a => a.Key == BakedInMetadataKey)?.Value);

	/// <summary>
	/// Pure decision logic, kept separate from the real env/assembly reads above so tests can
	/// exercise "override wins" / "falls back to baked-in" / "neither set -&gt; null" directly,
	/// instead of having to mutate real process environment variables (flaky under parallel test
	/// execution).
	/// </summary>
	public static string? Resolve(string? envOverride, string? bakedInValue) {
		if (!string.IsNullOrEmpty(envOverride)) {
			return envOverride;
		}

		return string.IsNullOrEmpty(bakedInValue) ? null : bakedInValue;
	}
}
