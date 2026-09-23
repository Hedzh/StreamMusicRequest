using System.Security.Cryptography;
using System.Text;
using Stream.MusicRequest.Core.Infrastructure.Storage;

namespace Stream.MusicRequest.Infrastructure.Storage;

public class DpapiProtectedStorage : IProtectedStorage
{
    private readonly string _storageDirectory;

    public DpapiProtectedStorage(
        string storageDirectory)
    {
        _storageDirectory = storageDirectory;

        Directory.CreateDirectory(
            _storageDirectory);
    }

    public async Task SaveAsync(
        string key,
        string value)
    {
        var filePath = GetFilePath(key);

        var plainBytes =
            Encoding.UTF8.GetBytes(value);

        var encryptedBytes =
            ProtectedData.Protect(
                plainBytes,
                null,
                DataProtectionScope.CurrentUser);

        await File.WriteAllBytesAsync(
            filePath,
            encryptedBytes);
    }

    public async Task<string?> LoadAsync(
        string key)
    {
        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
        {
            return null;
        }

        var encryptedBytes =
            await File.ReadAllBytesAsync(
                filePath);

        var plainBytes =
            ProtectedData.Unprotect(
                encryptedBytes,
                null,
                DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(
            plainBytes);
    }

    public Task DeleteAsync(
        string key)
    {
        var filePath = GetFilePath(key);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(
        string key)
    {
        var filePath = GetFilePath(key);

        return Task.FromResult(
            File.Exists(filePath));
    }

    private string GetFilePath(
        string key)
    {
        var safeKey =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(key));

        return Path.Combine(
            _storageDirectory,
            $"{safeKey}.dat");
    }
}