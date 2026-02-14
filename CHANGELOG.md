# Changelog

## [1.0.0] - 2026-02-14
### Added
- SaveManager<T>: Generic async save manager with thread-safe operations
- Atomic file writes with temp file validation
- Rolling backup system (configurable count and interval)
- Automatic corruption recovery from .bak and rolling backups
- SafeFileManager: Per-file locking for concurrent access safety
- ISaveSerializer: Pluggable serialization interface
- JsonUtilitySerializer: Default Unity JsonUtility implementation
- Exponential backoff retry for file I/O contention
- Sync and async API (Save/SaveAsync, Load/LoadAsync)
