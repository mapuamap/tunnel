using Renci.SshNet;
using System.Text;

namespace TunnelManager.Web.Services;

public class SshService : IDisposable
{
    private readonly string _host;
    private readonly string _user;
    private readonly string _password;
    private SshClient? _sshClient;
    private SftpClient? _sftpClient;

    public SshService(IConfiguration configuration)
    {
        _host = configuration["Vps:Host"] ?? throw new InvalidOperationException("Vps:Host not configured");
        _user = configuration["Vps:User"] ?? throw new InvalidOperationException("Vps:User not configured");
        _password = configuration["Vps:Password"] ?? throw new InvalidOperationException("Vps:Password not configured");
    }

    private SshClient GetSshClient()
    {
        if (_sshClient == null || !_sshClient.IsConnected)
        {
            _sshClient?.Dispose();
            _sshClient = new SshClient(_host, _user, _password);
            try
            {
                _sshClient.Connect();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect to {_host}: {ex.Message}", ex);
            }
        }
        return _sshClient;
    }

    private SftpClient GetSftpClient()
    {
        if (_sftpClient == null || !_sftpClient.IsConnected)
        {
            _sftpClient?.Dispose();
            _sftpClient = new SftpClient(_host, _user, _password);
            _sftpClient.Connect();
        }
        return _sftpClient;
    }

    public string ExecuteCommand(string command)
    {
        var client = GetSshClient();
        var result = client.RunCommand(command);
        if (result.ExitStatus != 0 && !string.IsNullOrEmpty(result.Error))
        {
            throw new Exception($"SSH command failed: {result.Error}");
        }
        return result.Result;
    }

    public void WriteFile(string remotePath, string content)
    {
        var client = GetSftpClient();
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = client.Create(remotePath);
        stream.Write(bytes, 0, bytes.Length);
    }

    public string ReadFile(string remotePath)
    {
        var client = GetSftpClient();
        using var stream = client.OpenRead(remotePath);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public bool FileExists(string remotePath)
    {
        try
        {
            var client = GetSftpClient();
            return client.Exists(remotePath);
        }
        catch
        {
            return false;
        }
    }

    public void DeleteFile(string remotePath)
    {
        var client = GetSftpClient();
        if (client.Exists(remotePath))
        {
            client.DeleteFile(remotePath);
        }
    }

    public void CreateDirectory(string remotePath)
    {
        var client = GetSftpClient();
        if (!client.Exists(remotePath))
        {
            client.CreateDirectory(remotePath);
        }
    }

    public void Dispose()
    {
        _sshClient?.Dispose();
        _sftpClient?.Dispose();
    }
}
