using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Constants;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers;

/// <summary>
/// Completes the "forgot password" flow: verifies the confirmation code Cognito emailed to the
/// user and sets the new password.
/// </summary>
public class ConfirmForgotPasswordHandler : IApiRequestHandler
{
    private readonly AmazonCognitoIdentityProviderClient _cognitoClient;

    public ConfirmForgotPasswordHandler(AmazonCognitoIdentityProviderClient cognitoClient)
    {
        _cognitoClient = cognitoClient ?? throw new ArgumentNullException(nameof(cognitoClient));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!TryDeserializeRequest(request, out var confirmRequest))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Invalid request body", context.Logger);
        }

        try
        {
            PersonalDataValidator.ValidateEmail(confirmRequest.Email);

            if (string.IsNullOrWhiteSpace(confirmRequest.ConfirmationCode))
            {
                throw new ValidationException("Confirmation code is required.");
            }

            PersonalDataValidator.ValidatePasswords(confirmRequest.NewPassword, confirmRequest.ConfirmNewPassword);
        }
        catch (ValidationException ex)
        {
            return ResponseBuilder.CreateErrorResponse(400, ex.Message, context.Logger);
        }

        try
        {
            await _cognitoClient.ConfirmForgotPasswordAsync(new Amazon.CognitoIdentityProvider.Model.ConfirmForgotPasswordRequest
            {
                ClientId = ResourceNames.ClientId,
                Username = confirmRequest.Email,
                ConfirmationCode = confirmRequest.ConfirmationCode,
                Password = confirmRequest.NewPassword,
            });
        }
        catch (CodeMismatchException)
        {
            return ResponseBuilder.CreateErrorResponse(400, "The confirmation code is incorrect.", context.Logger);
        }
        catch (ExpiredCodeException)
        {
            return ResponseBuilder.CreateErrorResponse(400, "The confirmation code has expired. Please request a new one.", context.Logger);
        }
        catch (InvalidPasswordException ex)
        {
            return ResponseBuilder.CreateErrorResponse(400, ex.Message, context.Logger);
        }
        catch (UserNotFoundException)
        {
            // Same email-enumeration concern as ForgotPasswordHandler: an unknown email and a
            // wrong/expired code should look the same to the caller.
            return ResponseBuilder.CreateErrorResponse(400, "The confirmation code is incorrect or has expired.", context.Logger);
        }
        catch (Exception ex)
        {
            return ResponseBuilder.CreateErrorResponse(500, $"Failed to reset password: {ex.Message}", context.Logger);
        }

        return ResponseBuilder.CreateSuccessResponse("Password has been reset successfully.");
    }

    private static bool TryDeserializeRequest(APIGatewayProxyRequest request, out APIHandler.Models.ConfirmForgotPasswordRequest confirmRequest)
    {
        try
        {
            confirmRequest = JsonSerializer.Deserialize<APIHandler.Models.ConfirmForgotPasswordRequest>(request.Body);
            return confirmRequest != null;
        }
        catch (JsonException)
        {
            confirmRequest = null;
            return false;
        }
    }
}
