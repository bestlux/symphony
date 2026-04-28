using System.Text.RegularExpressions;

namespace Symphony.Workspaces;

public static partial class PathSafety
{
    public static string SafeIdentifier(string? identifier)
    {
        var value = string.IsNullOrWhiteSpace(identifier) ? "issue" : identifier.Trim();
        return UnsafeIdentifierCharacters().Replace(value, "_");
    }

    public static string WorkspacePath(string root, string safeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new WorkspaceException("Workspace root is required.");
        }

        if (string.IsNullOrWhiteSpace(safeIdentifier))
        {
            throw new WorkspaceException("Workspace identifier is required.");
        }

        return Path.GetFullPath(Path.Combine(ExpandHome(root), safeIdentifier));
    }

    public static string RemoteWorkspacePath(string root, string safeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new WorkspaceException("Workspace root is required.");
        }

        if (string.IsNullOrWhiteSpace(safeIdentifier))
        {
            throw new WorkspaceException("Workspace identifier is required.");
        }

        return root.TrimEnd('/', '\\') + "/" + safeIdentifier;
    }

    public static void ValidateLocalWorkspacePath(string root, string workspace)
    {
        var fullRoot = Path.GetFullPath(ExpandHome(root)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullWorkspace = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(fullRoot, fullWorkspace, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceException($"Workspace path must not equal workspace root: {fullWorkspace}");
        }

        var rootPrefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullWorkspace.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceException($"Workspace path '{fullWorkspace}' is outside workspace root '{fullRoot}'.");
        }
    }

    public static void ValidateRemoteWorkspacePath(string workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            throw new WorkspaceException("Remote workspace path is empty.");
        }

        if (workspace.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new WorkspaceException("Remote workspace path contains invalid characters.");
        }
    }

    public static string ExpandHome(string path)
    {
        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    [GeneratedRegex("[^a-zA-Z0-9._-]")]
    private static partial Regex UnsafeIdentifierCharacters();
}

public sealed class WorkspaceException : Exception
{
    public WorkspaceException(string message)
        : base(message)
    {
    }

    public WorkspaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
