using System.Collections.ObjectModel;

namespace Em.Api.Core;

/// <summary>A label and an escaped URL for a directory ancestor.</summary>
/// <param name="Label">Display text, encoded by the renderer.</param>
/// <param name="Href">URL with escaped path segments.</param>
public sealed record CdnBreadcrumb(string Label, string Href);

/// <summary>A public directory entry; renderers must encode its name as text.</summary>
/// <param name="Name">Original name.</param>
/// <param name="Href">Escaped relative URL.</param>
/// <param name="IsDirectory">Whether the entry is a folder.</param>
/// <param name="Length">File length, absent for folders.</param>
/// <param name="Modified">Last modification time.</param>
public sealed record CdnPageEntry(string Name, string Href, bool IsDirectory, long? Length, DateTimeOffset Modified);

/// <summary>Read-only public directory data. Limits and counts are informational.</summary>
public sealed record CdnDirectoryPage {
   /// <summary>Full public request path.</summary>
   public required string RequestPath { get; init; }
   /// <summary>Ancestors including the current folder.</summary>
   public required IReadOnlyList<CdnBreadcrumb> Breadcrumbs { get; init; }
   /// <summary>Whether navigation to the parent is available.</summary>
   public bool HasParent { get; init; }
   /// <summary>Folders sorted by name.</summary>
   public required IReadOnlyList<CdnPageEntry> Folders { get; init; }
   /// <summary>Files sorted by name.</summary>
   public required IReadOnlyList<CdnPageEntry> Files { get; init; }
   /// <summary>Host application name.</summary>
   public required string ApplicationName { get; init; }
   /// <summary>Host informational version formatted for display.</summary>
   public required string HostVersion { get; init; }
   /// <summary>Maximum upload size in bytes.</summary>
   public long MaxFileSize { get; init; }
   /// <summary>Number of visible folders.</summary>
   public int FolderCount => Folders.Count;
   /// <summary>Number of visible files.</summary>
   public int FileCount => Files.Count;
   internal IReadOnlyDictionary<string, string> AssetUrls { get; init; } = ReadOnlyDictionary<string, string>.Empty;
   /// <summary>Returns an escaped, versioned skin asset URL, or null if unavailable.</summary>
   public string? Asset(string path) => AssetUrls.GetValueOrDefault(path.Replace('\\', '/'));
}

/// <summary>A CDN not-found page without disclosure of the failure reason.</summary>
/// <param name="RequestPath">Full public request path.</param>
/// <param name="ApplicationName">Host application name.</param>
/// <param name="HostVersion">Host version for display.</param>
public sealed record CdnNotFoundPage(string RequestPath, string ApplicationName, string HostVersion) {
   internal IReadOnlyDictionary<string, string> AssetUrls { get; init; } = ReadOnlyDictionary<string, string>.Empty;
   /// <summary>Returns an escaped, versioned asset URL, or null when CDN assets are unavailable.</summary>
   public string? Asset(string path) => AssetUrls.GetValueOrDefault(path.Replace('\\', '/'));
}

/// <summary>Host identity and the currently available download link.</summary>
/// <param name="ApplicationName">Host application name.</param>
/// <param name="HostVersion">Host version for display.</param>
/// <param name="CdnUrl">Download URL, or null when unavailable.</param>
public sealed record HomePage(string ApplicationName, string HostVersion, string? CdnUrl);

/// <summary>Unknown site path and host identity.</summary>
/// <param name="RequestPath">Full public request path.</param>
/// <param name="ApplicationName">Host application name.</param>
/// <param name="HostVersion">Host version for display.</param>
public sealed record SiteNotFoundPage(string RequestPath, string ApplicationName, string HostVersion);
