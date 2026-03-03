using Logger_MM.Agent;
using Renci.SshNet;
using System.Text;

namespace TunnelManager.Web.Services;

/// <summary>
/// Thread-safe singleton SSH service with persistent connections.
/// Connections are kept alive and automatically reconnected when needed.
/// </summary>
public class SshService : IDisposable
{
    private readonly string _host;
    private readonly string _user;
    private readonly string _password;
    private readonly LoggerMMAgent? _logger;
    
    private SshClient? _sshClient;
    private SftpClient? _sftpClient;
    private readonly object _sshLock = new();
    private readonly object _sftpLock = new();

    public SshService(IConfiguration configuration, LoggerMMAgent? logger = null)
    {
        _host = configuration["Vps:Host"] ?? throw new InvalidOperationException("Vps:Host not configured");
        _user = configuration["Vps:User"] ?? throw new InvalidOperationException("Vps:User not configured");
        _password = configuration["Vps:Password"] ?? throw new InvalidOperationException("Vps:Password not configured");
        _logger = logger;
    }

    private SshClient GetSshClient()
    {
        lock (_sshLock)
        {
            if (_sshClient != null && _sshClient.IsConnected)
                return _sshClient;

            _sshClient?.Dispose();
            _logger?.Debug("SshService", "GetSshClient", $"Connecting to SSH host {_host}",
                tags: new[] { "ssh", "connection" });

            _sshClient = new SshClient(_host, _user, _password);
            _sshClient.KeepAliveInterval = TimeSpan.FromSeconds(30);
            try
            {
                _sshClient.Connect();
                _logger?.Info("SshService", "GetSshClient", $"Successfully connected to SSH host {_host}",
                    @params: new { host = _host, user = _user },
                    tags: new[] { "ssh", "connection" });
            }
            catch (Exception ex)
            {
                _logger?.Critical("SshService", "GetSshClient", $"Failed to connect to SSH host {_host}: {ex.Message}",
                    exception: ex,
                    @params: new { host = _host, user = _user },
                    tags: new[] { "ssh", "connection", "error" });
                throw new Exception($"Failed to connect to {_host}: {ex.Message}", ex);
            }
            return _sshClient;
        }
    }

    private SftpClient GetSftpClient()
    {
        lock (_sftpLock)
        {
            if (_sftpClient != null && _sftpClient.IsConnected)
                return _sftpClient;

            _sftpClient?.Dispose();
            _logger?.Debug("SshService", "GetSftpClient", $"Connecting to SFTP host {_host}",
                tags: new[] { "ssh", "sftp", "connection" });

            _sftpClient = new SftpClient(_host, _user, _password);
            _sftpClient.KeepAliveInterval = TimeSpan.FromSeconds(30);
            try
            {
                _sftpClient.Connect();
                _logger?.Info("SshService", "GetSftpClient", $"Successfully connected to SFTP host {_host}",
                    @params: new { host = _host, user = _user },
                    tags: new[] { "ssh", "sftp", "connection" });
            }
            catch (Exception ex)
            {
                _logger?.Critical("SshService", "GetSftpClient", $"Failed to connect to SFTP host {_host}: {ex.Message}",
                    exception: ex,
                    @params: new { host = _host, user = _user },
                    tags: new[] { "ssh", "sftp", "connection", "error" });
                throw;
            }
            return _sftpClient;
        }
    }

    public string ExecuteCommand(string command)
    {
        _logger?.Debug("SshService", "ExecuteCommand", $"Executing SSH command: {command}",
            @params: new { command, host = _host },
            tags: new[] { "ssh", "command" });

        try
        {
            SshClient client;
            lock (_sshLock)
            {
                client = GetSshClient();
            }
            
            var result = client.RunCommand(command);

            if (result.ExitStatus != 0 && !string.IsNullOrEmpty(result.Error))
            {
                _logger?.Error("SshService", "ExecuteCommand", $"SSH command failed with exit status {result.ExitStatus}",
                    exception: new Exception(result.Error),
                    @params: new { command, exitStatus = result.ExitStatus, error = result.Error, host = _host },
                    tags: new[] { "ssh", "command", "error" });
                throw new Exception($"SSH command failed: {result.Error}");
            }

            _logger?.Debug("SshService", "ExecuteCommand", $"SSH command executed successfully",
                @params: new { command, exitStatus = result.ExitStatus, resultLength = result.Result?.Length ?? 0, host = _host },
                tags: new[] { "ssh", "command" });

            return result.Result ?? string.Empty;
        }
        catch (Exception ex) when (!ex.Message.StartsWith("SSH command failed"))
        {
            // Connection might be broken, reset and retry once
            lock (_sshLock)
            {
                _sshClient?.Dispose();
                _sshClient = null;
            }
            
            try
            {
                SshClient client;
                lock (_sshLock)
                {
                    client = GetSshClient();
                }
                var result = client.RunCommand(command);
                if (result.ExitStatus != 0 && !string.IsNullOrEmpty(result.Error))
                    throw new Exception($"SSH command failed: {result.Error}");
                return result.Result;
            }
            catch (Exception retryEx)
            {
                _logger?.Error("SshService", "ExecuteCommand", $"Exception executing SSH command (retry failed): {retryEx.Message}",
                    exception: retryEx,
                    @params: new { command, host = _host },
                    tags: new[] { "ssh", "command", "error" });
                throw;
            }
        }
    }

    public void WriteFile(string remotePath, string content)
    {
        _logger?.Debug("SshService", "WriteFile", $"Writing file via SFTP: {remotePath}",
            @params: new { remotePath, contentLength = content.Length, host = _host },
            tags: new[] { "ssh", "sftp", "file", "write" });

        try
        {
            SftpClient client;
            lock (_sftpLock)
            {
                client = GetSftpClient();
            }
            var bytes = Encoding.UTF8.GetBytes(content);
            using var stream = client.Create(remotePath);
            stream.Write(bytes, 0, bytes.Length);

            _logger?.Info("SshService", "WriteFile", $"File written successfully: {remotePath}",
                @params: new { remotePath, contentLength = content.Length, bytesWritten = bytes.Length, host = _host },
                tags: new[] { "ssh", "sftp", "file", "write" });
        }
        catch (Exception ex)
        {
            _logger?.Error("SshService", "WriteFile", $"Failed to write file {remotePath}: {ex.Message}",
                exception: ex,
                @params: new { remotePath, contentLength = content.Length, host = _host },
                tags: new[] { "ssh", "sftp", "file", "write", "error" });
            throw;
        }
    }

    public string ReadFile(string remotePath)
    {
        _logger?.Debug("SshService", "ReadFile", $"Reading file via SFTP: {remotePath}",
            @params: new { remotePath, host = _host },
            tags: new[] { "ssh", "sftp", "file", "read" });

        try
        {
            SftpClient client;
            lock (_sftpLock)
            {
                client = GetSftpClient();
            }
            using var stream = client.OpenRead(remotePath);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            _logger?.Info("SshService", "ReadFile", $"File read successfully: {remotePath}",
                @params: new { remotePath, contentLength = content.Length, host = _host },
                tags: new[] { "ssh", "sftp", "file", "read" });

            return content;
        }
        catch (Exception ex)
        {
            _logger?.Error("SshService", "ReadFile", $"Failed to read file {remotePath}: {ex.Message}",
                exception: ex,
                @params: new { remotePath, host = _host },
                tags: new[] { "ssh", "sftp", "file", "read", "error" });
            throw;
        }
    }

    public bool FileExists(string remotePath)
    {
        _logger?.Debug("SshService", "FileExists", $"Checking if file exists: {remotePath}",
            @params: new { remotePath, host = _host },
            tags: new[] { "ssh", "sftp", "file" });

        try
        {
            SftpClient client;
            lock (_sftpLock)
            {
                client = GetSftpClient();
            }
            var exists = client.Exists(remotePath);

            _logger?.Debug("SshService", "FileExists", $"File existence check result: {exists}",
                @params: new { remotePath, exists, host = _host },
                tags: new[] { "ssh", "sftp", "file" });

            return exists;
        }
        catch (Exception ex)
        {
            _logger?.Warning("SshService", "FileExists", $"Exception checking file existence {remotePath}: {ex.Message}",
                @params: new { remotePath, host = _host },
                tags: new[] { "ssh", "sftp", "file", "error" });
            return false;
        }
    }

    public void DeleteFile(string remotePath)
    {
        _logger?.Debug("SshService", "DeleteFile", $"Deleting file via SFTP: {remotePath}",
            @params: new { remotePath, host = _host },
            tags: new[] { "ssh", "sftp", "file", "delete" });

        try
        {
            SftpClient client;
            lock (_sftpLock)
            {
                client = GetSftpClient();
            }
            if (client.Exists(remotePath))
            {
                client.DeleteFile(remotePath);
                _logger?.Info("SshService", "DeleteFile", $"File deleted successfully: {remotePath}",
                    @params: new { remotePath, host = _host },
                    tags: new[] { "ssh", "sftp", "file", "delete" });
            }
            else
            {
                _logger?.Debug("SshService", "DeleteFile", $"File does not exist, skipping delete: {remotePath}",
                    @params: new { remotePath, host = _host },
                    tags: new[] { "ssh", "sftp", "file", "delete" });
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("SshService", "DeleteFile", $"Failed to delete file {remotePath}: {ex.Message}",
                exception: ex,
                @params: new { remotePath, host = _host },
                tags: new[] { "ssh", "sftp", "file", "delete", "error" });
            throw;
        }
    }

    public void CreateDirectory(string remotePath)
    {
        _logger?.Debug("SshService", "CreateDirectory", $"Creating directory via SFTP: {remotePath}",
            @params: new { remotePath, host = _host },
            tags: new[] { "ssh", "sftp", "directory" });

        try
        {
            SftpClient client;
            lock (_sftpLock)
            {
                client = GetSftpClient();
            }
            if (!client.Exists(remotePath))
            {
                client.CreateDirectory(remotePath);
                _logger?.Info("SshService", "CreateDirectory", $"Directory created successfully: {remotePath}",
                    @params: new { remotePath, host = _host },
                    tags: new[] { "ssh", "sftp", "directory" });
            }
            else
            {
                _logger?.Debug("SshService", "CreateDirectory", $"Directory already exists, skipping: {remotePath}",
                    @params: new { remotePath, host = _host },
                    tags: new[] { "ssh", "sftp", "directory" });
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("SshService", "CreateDirectory", $"Failed to create directory {remotePath}: {ex.Message}",
                exception: ex,
                @params: new { remotePath, host = _host },
                tags: new[] { "ssh", "sftp", "directory", "error" });
            throw;
        }
    }

    public void Dispose()
    {
        _logger?.Debug("SshService", "Dispose", "Disposing SSH service connections",
            tags: new[] { "ssh", "dispose" });

        lock (_sshLock)
        {
            _sshClient?.Dispose();
            _sshClient = null;
        }
        lock (_sftpLock)
        {
            _sftpClient?.Dispose();
            _sftpClient = null;
        }
    }
}
