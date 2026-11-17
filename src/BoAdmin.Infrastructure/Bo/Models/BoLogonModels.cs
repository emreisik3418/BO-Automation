using System.Text.Json.Serialization;

namespace BoAdmin.Infrastructure.Bo.Models;

/// <summary>POST /logon/long gövdesi.</summary>
public sealed record BoLogonRequest(
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("auth")] string Auth);

/// <summary>POST /logon/long yanıtı. Token ayrıca X-SAP-LogonToken header'ında da gelir.</summary>
public sealed class BoLogonResponse
{
    [JsonPropertyName("logonToken")]
    public string? LogonToken { get; set; }
}

/// <summary>POST /v1/cmsquery gövdesi.</summary>
public sealed record CmsQueryRequest([property: JsonPropertyName("query")] string Query);
