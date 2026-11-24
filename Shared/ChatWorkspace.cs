using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Chats are grouped by the working directory (workspace) they were made in:
/// chats\&lt;folder name&gt;-&lt;hash&gt;\ beside the EXEs. The hash of the full path
/// keeps two workspaces with the same folder name apart. Shared so the CLI
/// (which saves) and the GUI (which lists) agree on the folder.
/// </summary>
public static class ChatWorkspace
{
    public static string ChatsRoot
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chats"); }
    }

    /// <summary>The folder holding <paramref name="workspace"/>'s chats.</summary>
    public static string DirectoryFor(string workspace)
    {
        return Path.Combine(ChatsRoot, FolderName(workspace));
    }

    /// <summary>e.g. C:\Projects\MyApp → MyApp-3fa2c1d0.</summary>
    public static string FolderName(string workspace)
    {
        string full = Normalize(workspace);
        string name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(name))
            name = full.Substring(0, 1);    // A drive root: its letter.
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        // Windows paths are case-insensitive: C:\Work and c:\work are one workspace.
        byte[] hash;
        using (SHA1 sha = SHA1.Create())
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(full.ToUpperInvariant()));
        StringBuilder suffix = new StringBuilder(8);
        for (int i = 0; i < 4; i++)
            suffix.Append(hash[i].ToString("x2"));

        return name + "-" + suffix;
    }

    /// <summary>Full path without a trailing separator (a drive root keeps its own).</summary>
    public static string Normalize(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full);
        return full.Length > root.Length
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    public static bool SamePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b);
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }
}
