using System.Text.Json.Serialization;

namespace APIHandler.Models;

public class ConfirmForgotPasswordRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; set; }

    [JsonPropertyName("confirmationCode")]
    public required string ConfirmationCode { get; set; }

    [JsonPropertyName("newPassword")]
    public required string NewPassword { get; set; }

    [JsonPropertyName("confirmNewPassword")]
    public required string ConfirmNewPassword { get; set; }
}
