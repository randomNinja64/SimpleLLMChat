using System;
using System.IO;
using System.Threading;

/// <summary>
/// Writes beside the target, flushes to disk, then swaps it into place, so the
/// file is always the old or the new version, never a partial one. Saved chats
/// are rewritten whole, and the GUI kills the CLI on New Chat, which can land
/// mid-save: a plain overwrite could lose the whole chat.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] contents)
    {
        Write(path, stream => stream.Write(contents, 0, contents.Length));
    }

    /// <summary>
    /// As <see cref="WriteAllBytes"/>, for content written straight to the stream, so a
    /// large file is not first built in memory. <paramref name="write"/> must not close it.
    /// </summary>
    public static void Write(string path, Action<Stream> write)
    {
        string target = Path.GetFullPath(path);
        string temp = target + ".tmp";

        using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            write(stream);
            stream.Flush(true);
        }

        // A reader (the GUI's sidebar, antivirus) can hold the target open for
        // a moment and block the swap; retry briefly before giving up.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(target))
                    File.Replace(temp, target, null);
                else
                    File.Move(temp, target);
                return;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException) || attempt >= 5)
                {
                    try { File.Delete(temp); } catch (Exception) { }
                    throw;
                }
                Thread.Sleep(50 * attempt);
            }
        }
    }
}
