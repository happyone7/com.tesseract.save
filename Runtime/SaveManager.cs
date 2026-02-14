using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Tesseract.Save
{
    /// <summary>
    /// Production-grade save manager with async operations, rolling backups, and corruption recovery.
    /// 
    /// Usage:
    ///   var manager = new SaveManager<MyData>("save.json");
    ///   await manager.SaveAsync(myData);
    ///   var loaded = await manager.LoadAsync();
    /// </summary>
    public class SaveManager<T> where T : class, new()
    {
        private readonly string _savePath;
        private readonly string _backupPath;
        private readonly string _backupDir;
        private readonly ISaveSerializer _serializer;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        private readonly int _maxBackups;
        private readonly float _backupIntervalHours;

        private DateTime _lastBackupTime = DateTime.MinValue;

        /// <summary>
        /// Create a SaveManager for type T.
        /// </summary>
        /// <param name="fileName">Save file name (e.g. "save.json")</param>
        /// <param name="serializer">Custom serializer. Defaults to JsonUtilitySerializer.</param>
        /// <param name="maxBackups">Maximum number of rolling backups to keep.</param>
        /// <param name="backupIntervalHours">Minimum hours between automatic backups.</param>
        public SaveManager(string fileName, ISaveSerializer serializer = null, int maxBackups = 5, float backupIntervalHours = 6f)
        {
            _savePath = Path.Combine(Application.persistentDataPath, fileName);
            _backupPath = _savePath + ".bak";
            _backupDir = Path.Combine(Application.persistentDataPath, "Backups");
            _serializer = serializer ?? new JsonUtilitySerializer();
            _maxBackups = maxBackups;
            _backupIntervalHours = backupIntervalHours;
        }

        /// <summary>
        /// Save data asynchronously with thread safety and atomic file writes.
        /// </summary>
        public async Task<bool> SaveAsync(T data)
        {
            await _saveLock.WaitAsync();
            try
            {
                string json = _serializer.Serialize(data);
                string tempPath = _savePath + ".tmp";

                // Write to temp file first
                await WriteFileWithRetryAsync(tempPath, json);

                // Validate temp file
                string verification = await ReadFileWithRetryAsync(tempPath);
                if (string.IsNullOrEmpty(verification))
                {
                    Debug.LogError("[SaveManager] Temp file validation failed.");
                    return false;
                }

                // Create backup of current file
                lock (SafeFileManager.GetLock(_savePath))
                {
                    if (File.Exists(_savePath))
                        File.Copy(_savePath, _backupPath, true);

                    // Replace with verified temp
                    if (File.Exists(tempPath))
                        File.Copy(tempPath, _savePath, true);

                    File.Delete(tempPath);
                }

                // Rolling backup
                CreateRollingBackupIfNeeded();

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Save failed: {e.Message}");
                return false;
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// Load data with automatic backup recovery on failure.
        /// </summary>
        public async Task<T> LoadAsync()
        {
            // Try main file
            T data = await TryLoadFileAsync(_savePath);
            if (data != null) return data;

            Debug.LogWarning("[SaveManager] Main save file failed. Trying backup...");

            // Try .bak
            data = await TryLoadFileAsync(_backupPath);
            if (data != null) return data;

            Debug.LogWarning("[SaveManager] Backup failed. Trying rolling backups...");

            // Try rolling backups (newest first)
            data = TryLoadRollingBackups();
            if (data != null) return data;

            Debug.Log("[SaveManager] No valid save found. Returning new instance.");
            return new T();
        }

        /// <summary>
        /// Save synchronously. Use SaveAsync when possible.
        /// </summary>
        public bool Save(T data)
        {
            try
            {
                string json = _serializer.Serialize(data);

                lock (SafeFileManager.GetLock(_savePath))
                {
                    string tempPath = _savePath + ".tmp";
                    File.WriteAllText(tempPath, json);

                    if (File.Exists(_savePath))
                        File.Copy(_savePath, _backupPath, true);

                    File.Copy(tempPath, _savePath, true);
                    File.Delete(tempPath);
                }

                CreateRollingBackupIfNeeded();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Sync save failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load synchronously. Use LoadAsync when possible.
        /// </summary>
        public T Load()
        {
            T data = TryLoadFile(_savePath);
            if (data != null) return data;

            data = TryLoadFile(_backupPath);
            if (data != null) return data;

            data = TryLoadRollingBackups();
            if (data != null) return data;

            return new T();
        }

        /// <summary>
        /// Check if a save file exists.
        /// </summary>
        public bool HasSave()
        {
            return File.Exists(_savePath) || File.Exists(_backupPath);
        }

        /// <summary>
        /// Delete all save files and backups.
        /// </summary>
        public void DeleteAll()
        {
            lock (SafeFileManager.GetLock(_savePath))
            {
                if (File.Exists(_savePath)) File.Delete(_savePath);
                if (File.Exists(_backupPath)) File.Delete(_backupPath);

                if (Directory.Exists(_backupDir))
                {
                    string pattern = Path.GetFileName(_savePath) + ".backup_*";
                    foreach (var file in Directory.GetFiles(_backupDir, pattern))
                        File.Delete(file);
                }
            }
        }

        #region Private Methods

        private async Task<T> TryLoadFileAsync(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = await ReadFileWithRetryAsync(path);
                if (string.IsNullOrEmpty(json)) return null;
                return _serializer.Deserialize<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] Failed to load {path}: {e.Message}");
                return null;
            }
        }

        private T TryLoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json)) return null;
                return _serializer.Deserialize<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] Failed to load {path}: {e.Message}");
                return null;
            }
        }

        private T TryLoadRollingBackups()
        {
            if (!Directory.Exists(_backupDir)) return null;

            string pattern = Path.GetFileName(_savePath) + ".backup_*";
            string[] backups = Directory.GetFiles(_backupDir, pattern);
            Array.Sort(backups);
            Array.Reverse(backups); // Newest first

            foreach (string backup in backups)
            {
                T data = TryLoadFile(backup);
                if (data != null)
                {
                    Debug.Log($"[SaveManager] Recovered from rolling backup: {backup}");
                    return data;
                }
            }
            return null;
        }

        private void CreateRollingBackupIfNeeded()
        {
            if ((DateTime.Now - _lastBackupTime).TotalHours < _backupIntervalHours)
                return;

            try
            {
                if (!Directory.Exists(_backupDir))
                    Directory.CreateDirectory(_backupDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupName = Path.GetFileName(_savePath) + $".backup_{timestamp}";
                string backupFilePath = Path.Combine(_backupDir, backupName);

                File.Copy(_savePath, backupFilePath, true);
                _lastBackupTime = DateTime.Now;

                CleanupOldBackups();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] Rolling backup failed: {e.Message}");
            }
        }

        private void CleanupOldBackups()
        {
            string pattern = Path.GetFileName(_savePath) + ".backup_*";
            string[] backups = Directory.GetFiles(_backupDir, pattern);

            if (backups.Length <= _maxBackups) return;

            Array.Sort(backups);
            for (int i = 0; i < backups.Length - _maxBackups; i++)
            {
                File.Delete(backups[i]);
            }
        }

        private async Task WriteFileWithRetryAsync(string path, string content, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    lock (SafeFileManager.GetLock(path))
                    {
                        File.WriteAllText(path, content);
                    }
                    return;
                }
                catch (IOException)
                {
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(100 * (i + 1)); // Exponential backoff
                }
            }
        }

        private async Task<string> ReadFileWithRetryAsync(string path, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    lock (SafeFileManager.GetLock(path))
                    {
                        return File.ReadAllText(path);
                    }
                }
                catch (IOException)
                {
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(100 * (i + 1));
                }
            }
            return null;
        }

        #endregion
    }
}
