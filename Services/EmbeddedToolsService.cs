using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Platform;

namespace Mermaider.Services;

public static class EmbeddedToolsService
{
    private static readonly object SyncRoot = new();
    private static string? _cachedToolsDir;

    public static string? EnsureExtractedTools()
    {
        if (!string.IsNullOrEmpty(_cachedToolsDir) && Directory.Exists(_cachedToolsDir))
        {
            return _cachedToolsDir;
        }

        lock (SyncRoot)
        {
            if (!string.IsNullOrEmpty(_cachedToolsDir) && Directory.Exists(_cachedToolsDir))
            {
                return _cachedToolsDir;
            }

            var toolsRootUri = new Uri("avares://Mermaider/tools");
            var buildId = Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId.ToString("N");
            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Mermaider",
                "embedded-tools");
            var targetRoot = Path.Combine(cacheRoot, buildId);
            var markerPath = Path.Combine(targetRoot, ".extract-complete");

            if (File.Exists(markerPath))
            {
                _cachedToolsDir = targetRoot;
                return _cachedToolsDir;
            }

            Directory.CreateDirectory(targetRoot);

            var assetUris = AssetLoader.GetAssets(toolsRootUri, null)
                .Where(uri => !uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
                .ToArray();

            if (assetUris.Length == 0)
            {
                return null;
            }

            foreach (var assetUri in assetUris)
            {
                var relativePath = GetRelativeToolsPath(assetUri);
                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }

                var destinationPath = Path.Combine(targetRoot, relativePath);
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                using var source = AssetLoader.Open(assetUri);
                using var destination = File.Create(destinationPath);
                source.CopyTo(destination);
            }

            File.WriteAllText(markerPath, buildId);
            _cachedToolsDir = targetRoot;
            return _cachedToolsDir;
        }
    }

    private static string GetRelativeToolsPath(Uri assetUri)
    {
        const string toolsPrefix = "/tools/";
        var absolutePath = Uri.UnescapeDataString(assetUri.AbsolutePath);
        var index = absolutePath.IndexOf(toolsPrefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        return absolutePath[(index + toolsPrefix.Length)..]
            .Replace('/', Path.DirectorySeparatorChar);
    }
}
