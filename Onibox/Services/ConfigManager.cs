using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Onibox.Services;

public sealed class ConfigManager
{
    private readonly HttpClient _httpClient;
    private readonly SettingsStorage _storage;

    public ConfigManager(SettingsStorage storage)
    {
        _storage = storage;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public Task<string> DownloadAsync(string url, CancellationToken cancellationToken = default)
        => DownloadAsync(url, null, cancellationToken);

    public async Task<string> DownloadAsync(string url, NetworkCredential? credentials, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException(Localization.GetString("Error.ConfigUrl.Required"));
        }

        var trimmedUrl = url.Trim();
        string content;

        if (LooksLikeUri(trimmedUrl))
        {
            if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException(Localization.GetString("Error.ConfigUrl.Scheme"));
            }

            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                content = await DownloadHttpAsync(uri, credentials, cancellationToken);
            }
            else
            {
                throw new ArgumentException(Localization.GetString("Error.ConfigUrl.Scheme"));
            }
        }
        else
        {
            var localPath = ResolveLocalPath(trimmedUrl);
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                throw new ArgumentException(Localization.GetString("Error.ConfigFile.Missing"));
            }

            content = await File.ReadAllTextAsync(localPath, cancellationToken);
        }

        using var _ = JsonWithComments.ParseDocument(content);

        _storage.EnsureAppDataDirectory();
        await File.WriteAllTextAsync(_storage.ConfigPath, content, new UTF8Encoding(false), cancellationToken);

        return _storage.ConfigPath;
    }

    private async Task<string> DownloadHttpAsync(Uri uri, NetworkCredential? credentials, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (credentials is not null)
        {
            request.Headers.Authorization = BuildBasicAuthHeader(credentials);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && IsBasicAuthChallenge(response))
        {
            if (credentials is null)
            {
                throw new BasicAuthRequiredException(uri);
            }

            throw new BasicAuthInvalidException(uri);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static AuthenticationHeaderValue BuildBasicAuthHeader(NetworkCredential credentials)
    {
        var raw = $"{credentials.UserName}:{credentials.Password}";
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static bool IsBasicAuthChallenge(HttpResponseMessage response)
    {
        foreach (var challenge in response.Headers.WwwAuthenticate)
        {
            if (string.Equals(challenge.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveLocalPath(string input)
    {
        var path = input.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        path = Environment.ExpandEnvironmentVariables(path);
        if (File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }
        catch
        {
            // ignore invalid paths
        }

        return path;
    }

    private static bool LooksLikeUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.IndexOf("://", StringComparison.Ordinal) > 0)
        {
            return true;
        }

        return value.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("https:", StringComparison.OrdinalIgnoreCase);
    }
}
