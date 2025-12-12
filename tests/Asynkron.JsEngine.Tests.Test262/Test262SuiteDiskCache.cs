using System.Reflection;
using Test262Harness;
using Zio;

namespace Asynkron.JsEngine.Tests.Test262;

internal static class Test262SuiteDiskCache
{
    private const string EnabledEnvVar = "JSENGINE_TEST262_DISK_CACHE";
    private const string CacheDirEnvVar = "JSENGINE_TEST262_DISK_CACHE_DIR";
    private const string ExtractMarkerFileName = ".jsengine-test262-extract.complete";
    private const string ExtractLockFileName = ".jsengine-test262-extract.lock";

    internal static bool Enabled
    {
        get
        {
            var setting = Environment.GetEnvironmentVariable(EnabledEnvVar);
            if (string.IsNullOrWhiteSpace(setting))
            {
                return true;
            }

            return !string.Equals(setting, "0", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(setting, "false", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(setting, "off", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static async Task<Test262Stream> LoadAsync()
    {
        if (!Enabled)
        {
            return await Test262StreamExtensions.FromGitHub(State.GitHubSha);
        }

        var suiteDir = GetExtractedSuiteDirectory();
        if (IsExtractedSuiteReady(suiteDir))
        {
            return await TryLoadFromDirectory(suiteDir);
        }

        try
        {
            Directory.CreateDirectory(suiteDir);
        }
        catch
        {
            return await Test262StreamExtensions.FromGitHub(State.GitHubSha);
        }

        await using var extractLock = TryAcquireLock(Path.Combine(suiteDir, ExtractLockFileName));
        if (extractLock is null)
        {
            if (IsExtractedSuiteReady(suiteDir))
            {
                return await TryLoadFromDirectory(suiteDir);
            }

            return await Test262StreamExtensions.FromGitHub(State.GitHubSha);
        }

        if (IsExtractedSuiteReady(suiteDir))
        {
            return await TryLoadFromDirectory(suiteDir);
        }

        Test262Stream? githubStream = null;
        try
        {
            githubStream = await Test262StreamExtensions.FromGitHub(State.GitHubSha);

            await ExtractToDirectory(githubStream, suiteDir);
            await WriteExtractionMarker(suiteDir);

            try
            {
                var directoryStream = Test262Stream.FromDirectory(suiteDir, _ => { });
                return directoryStream;
            }
            catch
            {
                return githubStream;
            }
        }
        catch
        {
            return githubStream is null
                ? await Test262StreamExtensions.FromGitHub(State.GitHubSha)
                : githubStream;
        }

        // Unreachable: all paths return above.
    }

    /// <summary>
    /// Gets the extracted suite directory path.
    /// </summary>
    internal static string GetCacheDirectory() => GetExtractedSuiteDirectory();

    private static string GetExtractedSuiteDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable(CacheDirEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.Combine(overridePath, State.GitHubSha);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.GetTempPath();
        }

        return Path.Combine(home, ".asynkron", "jsengine", "test262", State.GitHubSha);
    }

    private static bool IsExtractedSuiteReady(string suiteDir)
        => File.Exists(Path.Combine(suiteDir, ExtractMarkerFileName));

    private static async Task<Test262Stream> TryLoadFromDirectory(string suiteDir)
    {
        try
        {
            return Test262Stream.FromDirectory(suiteDir, _ => { });
        }
        catch
        {
            return await Test262StreamExtensions.FromGitHub(State.GitHubSha);
        }
    }

    private static async Task WriteExtractionMarker(string suiteDir)
    {
        var markerPath = Path.Combine(suiteDir, ExtractMarkerFileName);
        var content = $"sha={State.GitHubSha}{Environment.NewLine}extracted={DateTimeOffset.UtcNow:O}{Environment.NewLine}";
        await File.WriteAllTextAsync(markerPath, content);
    }

    private static FileStream? TryAcquireLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch
        {
            return null;
        }
    }

    private static async Task ExtractToDirectory(Test262Stream stream, string suiteDir)
    {
        var fileSystem = TryGetFileSystem(stream) ?? throw new NotSupportedException(
            "Test262Stream.Options.FileSystem is not available; cannot extract suite to disk.");

        foreach (var file in fileSystem.EnumerateFiles(UPath.Root, "*", SearchOption.AllDirectories))
        {
            var relative = file.FullName;
            if (relative.StartsWith("/", StringComparison.Ordinal))
            {
                relative = relative[1..];
            }

            var destinationPath = Path.Combine(suiteDir, relative.Replace('/', Path.DirectorySeparatorChar));
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await using var source = fileSystem.OpenFile(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read,
                bufferSize: 128 * 1024,
                useAsync: true);
            await source.CopyToAsync(destination);
        }
    }

    private static IFileSystem? TryGetFileSystem(Test262Stream stream)
    {
        var options = stream.Options;
        var fsProp = options.GetType().GetProperty("FileSystem",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (fsProp?.GetValue(options) is IFileSystem fileSystem)
        {
            return fileSystem;
        }

        return null;
    }
}
