using System.Text.Json;

namespace TunnelManager.Web.Services;

public class FolderService
{
    private readonly string _storePath;
    private readonly ILogger<FolderService> _logger;
    private readonly object _lock = new();
    private FolderStore _store = new();

    public FolderService(IConfiguration configuration, ILogger<FolderService> logger)
    {
        _logger = logger;
        _storePath = configuration["Folders:StorePath"]
            ?? Path.Combine("/app", "data", "forward-folders.json");

        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Load();
    }

    public List<string> GetFolders()
    {
        lock (_lock)
        {
            return new List<string>(_store.Folders);
        }
    }

    public Dictionary<string, string> GetDomainFolders()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_store.DomainFolders);
        }
    }

    public string GetDomainFolder(string domain)
    {
        lock (_lock)
        {
            return _store.DomainFolders.TryGetValue(domain, out var f) ? f : string.Empty;
        }
    }

    public void SetDomainFolder(string domain, string folder)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        lock (_lock)
        {
            if (string.IsNullOrEmpty(folder))
            {
                _store.DomainFolders.Remove(domain);
            }
            else
            {
                _store.DomainFolders[domain] = folder;
            }
            Save();
        }
    }

    public bool AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        folder = folder.Trim();
        lock (_lock)
        {
            if (_store.Folders.Any(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            _store.Folders.Add(folder);
            Save();
            return true;
        }
    }

    public bool RemoveFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        lock (_lock)
        {
            var idx = _store.Folders.FindIndex(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            var removed = _store.Folders[idx];
            _store.Folders.RemoveAt(idx);

            // Clear domain assignments that reference the removed folder
            var keys = _store.DomainFolders
                .Where(kv => kv.Value.Equals(removed, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in keys)
            {
                _store.DomainFolders.Remove(key);
            }
            Save();
            return true;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                _store = new FolderStore();
                return;
            }
            var json = File.ReadAllText(_storePath);
            _store = JsonSerializer.Deserialize<FolderStore>(json) ?? new FolderStore();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load folder store from {Path}; starting empty", _storePath);
            _store = new FolderStore();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save folder store to {Path}", _storePath);
        }
    }

    private class FolderStore
    {
        public List<string> Folders { get; set; } = new();
        public Dictionary<string, string> DomainFolders { get; set; } = new();
    }
}
