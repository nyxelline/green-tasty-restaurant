using System.Text.Json.Serialization;

namespace APIHandler.Models;

public class ForgotPasswordRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; set; }
}
