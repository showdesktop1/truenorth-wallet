using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TrueNorthWallet;

public class RpcClient
{
    private readonly HttpClient _http = new();
    private readonly string _url;
    private readonly string _user;
    private readonly string _password;
    private readonly string? _cookiePath;

    public RpcClient(string host, int port, string user, string password, string? wallet = null, string? cookiePath = null)
    {
        _url = $"http://{host}:{port}" + (wallet != null ? $"/wallet/{wallet}" : "");
        _user = user;
        _password = password;
        _cookiePath = cookiePath;
    }

    private string AuthToken()
    {
        string credentials;
        if (!string.IsNullOrWhiteSpace(_cookiePath))
        {
            try { credentials = File.ReadAllText(_cookiePath).Trim(); }
            catch (Exception ex) { throw new Exception("Cannot read the cookie file: " + ex.Message); }
        }
        else
        {
            credentials = _user + ":" + _password;
        }
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
    }

    public async Task<JsonElement> CallAsync(string method, params object[] parameters)
    {
        var body = JsonSerializer.Serialize(new { jsonrpc = "1.0", id = "wallet", method, @params = parameters });

        using var request = new HttpRequestMessage(HttpMethod.Post, _url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", AuthToken());
        request.Content = new StringContent(body, Encoding.UTF8, "text/plain");

        var response = await _http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new Exception(string.IsNullOrWhiteSpace(_cookiePath)
                ? "Wrong RPC username or password"
                : "The node rejected the cookie. Check that the cookie file belongs to the running node.");

        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);

        var error = doc.RootElement.GetProperty("error");
        if (error.ValueKind != JsonValueKind.Null)
            throw new Exception(error.GetProperty("message").GetString());

        return doc.RootElement.GetProperty("result").Clone();
    }
}
