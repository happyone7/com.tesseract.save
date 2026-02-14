using System.Collections.Concurrent;

namespace Tesseract.Save
{
    /// <summary>
    /// Provides per-file locking for thread-safe concurrent file access.
    /// </summary>
    public static class SafeFileManager
    {
        private static readonly ConcurrentDictionary<string, object> _fileLocks = new ConcurrentDictionary<string, object>();

        public static object GetLock(string filePath)
        {
            return _fileLocks.GetOrAdd(filePath, _ => new object());
        }
    }
}
