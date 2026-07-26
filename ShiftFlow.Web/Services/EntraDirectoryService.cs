using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace ShiftFlow.Web.Services;

public record EntraDirectoryUser(
    string Id, string DisplayName, string Email, string? JobTitle, string? Department,
    string? GivenName, string? Surname, string? MobilePhone, string? EmployeeId,
    string? OfficeLocation, string? CompanyName, string? City, string? Country, bool? AccountEnabled);

public class EntraDirectorySearchException : Exception
{
    public EntraDirectorySearchException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface IEntraDirectoryService
{
    Task<List<EntraDirectoryUser>> SearchAsync(string query, CancellationToken ct = default);
}

// App-only (client-credentials) Microsoft Graph search — separate from the delegated
// OIDC scheme used for sign-in (Program.cs "EntraID"). Requires the Graph "User.Read.All"
// Application permission granted with tenant-admin consent on the same app registration;
// that's an Entra-portal action, not something this service can do or detect ahead of time.
public class GraphEntraDirectoryService : IEntraDirectoryService
{
    private const string TokenCacheKey = "EntraDirectoryService:GraphToken";

    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GraphEntraDirectoryService> _log;

    public GraphEntraDirectoryService(IConfiguration config, IHttpClientFactory httpFactory,
        IMemoryCache cache, ILogger<GraphEntraDirectoryService> log)
    {
        _config = config;
        _httpFactory = httpFactory;
        _cache = cache;
        _log = log;
    }

    // Constructed lazily (not in the DI constructor) so blank AzureAd credentials — the
    // expected state until an admin configures Entra — don't throw for every UsersController
    // request; the error only surfaces the moment directory search is actually attempted.
    private ClientSecretCredential BuildCredential()
    {
        var section = _config.GetSection("AzureAd");
        var tenantId = section["TenantId"];
        var clientId = section["ClientId"];
        var clientSecret = section["ClientSecret"];

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new EntraDirectorySearchException("Microsoft Entra sign-in isn't configured yet, so directory search is unavailable.");

        return new ClientSecretCredential(tenantId, clientId, clientSecret);
    }

    public async Task<List<EntraDirectoryUser>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return new List<EntraDirectoryUser>();

        var token = await GetTokenAsync(ct);

        // $filter is embedded in the URL, not parameterized — escape the OData string
        // literal so a name/email containing a single quote can't break the query.
        var q = query.Trim().Replace("'", "''");
        var filter = $"startswith(displayName,'{q}') or startswith(mail,'{q}') or startswith(userPrincipalName,'{q}')";
        var url = "https://graph.microsoft.com/v1.0/users" +
                   $"?$filter={Uri.EscapeDataString(filter)}" +
                   "&$select=id,displayName,mail,userPrincipalName,jobTitle,department," +
                   "givenName,surname,mobilePhone,businessPhones,employeeId,officeLocation," +
                   "companyName,city,country,accountEnabled" +
                   "&$top=25";

        var client = _httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Required by Graph for $filter with startswith() on properties like mail/userPrincipalName.
        request.Headers.Add("ConsistencyLevel", "eventual");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Graph directory search request failed");
            throw new EntraDirectorySearchException("Could not reach Microsoft Graph. Check network connectivity.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Graph directory search returned {Status}: {Body}", response.StatusCode, body);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new EntraDirectorySearchException(
                    "Directory search isn't available — the app registration is missing the Microsoft Graph " +
                    "\"User.Read.All\" Application permission, or it hasn't been granted admin consent yet.");

            throw new EntraDirectorySearchException($"Microsoft Graph search failed ({(int)response.StatusCode}).");
        }

        return ParseResults(await response.Content.ReadAsStreamAsync(ct), ct);
    }

    private static List<EntraDirectoryUser> ParseResults(Stream stream, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(stream);
        var results = new List<EntraDirectoryUser>();
        if (!doc.RootElement.TryGetProperty("value", out var items))
            return results;

        foreach (var item in items.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString() ?? "";
            var displayName = item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
            var mail = item.TryGetProperty("mail", out var m) ? m.GetString() : null;
            var upn = item.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null;
            var jobTitle = item.TryGetProperty("jobTitle", out var jt) ? jt.GetString() : null;
            var department = item.TryGetProperty("department", out var d) ? d.GetString() : null;
            var givenName = item.TryGetProperty("givenName", out var gn) ? gn.GetString() : null;
            var surname = item.TryGetProperty("surname", out var sn) ? sn.GetString() : null;
            var mobilePhone = item.TryGetProperty("mobilePhone", out var mp) ? mp.GetString() : null;
            var employeeId = item.TryGetProperty("employeeId", out var eid) ? eid.GetString() : null;
            var officeLocation = item.TryGetProperty("officeLocation", out var ol) ? ol.GetString() : null;
            var companyName = item.TryGetProperty("companyName", out var cn) ? cn.GetString() : null;
            var city = item.TryGetProperty("city", out var ci) ? ci.GetString() : null;
            var country = item.TryGetProperty("country", out var co) ? co.GetString() : null;
            var accountEnabled = item.TryGetProperty("accountEnabled", out var ae) && ae.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? ae.GetBoolean() : (bool?)null;

            // businessPhones is an array — fall back to its first entry when mobilePhone is absent.
            if (string.IsNullOrWhiteSpace(mobilePhone) && item.TryGetProperty("businessPhones", out var bp) && bp.ValueKind == JsonValueKind.Array)
            {
                var first = bp.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.String) mobilePhone = first.GetString();
            }

            // "mail" is often empty for UPN-only accounts — fall back to userPrincipalName.
            var email = !string.IsNullOrWhiteSpace(mail) ? mail : upn;
            if (string.IsNullOrWhiteSpace(email)) continue;

            results.Add(new EntraDirectoryUser(
                id, string.IsNullOrWhiteSpace(displayName) ? email : displayName, email, jobTitle, department,
                givenName, surname, mobilePhone, employeeId, officeLocation, companyName, city, country, accountEnabled));
        }
        return results;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<string>(TokenCacheKey, out var cached) && cached != null)
            return cached;

        var credential = BuildCredential(); // throws its own clear EntraDirectorySearchException if unconfigured

        try
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }), ct);

            var ttl = token.ExpiresOn - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
            _cache.Set(TokenCacheKey, token.Token, ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(1));
            return token.Token;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to acquire Microsoft Graph app-only token");
            throw new EntraDirectorySearchException(
                "Could not authenticate with Microsoft Graph. Check the AzureAd client credentials.", ex);
        }
    }
}
