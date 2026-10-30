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
/// Starts the "forgot password" flow by asking Cognito to email a confirmation code to the user.
/// Always responds with a generic success message, whether or not the email is registered, so the
/// endpoint can't be used to enumerate which addresses have an account.
/// </summary>
public class ForgotPasswordHandler : IApiRequestHandler
{
    private const string GenericSuccessMessage = "If an account exists for this email, a password reset code has been sent.";

    private readonly AmazonCognitoIdentityProviderClient _cognitoClient;

    public ForgotPasswordHandler(AmazonCognitoIdentityProviderClient cognitoClient)
    {
        _cognitoClient = cognitoClient ?? throw new ArgumentNullException(nameof(cognitoClient));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!TryDeserializeRequest(request, out var forgotPasswordRequest))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Invalid request body", context.Logger);
        }

        try
        {
            PersonalDataValidator.ValidateEmail(forgotPasswordRequest.Email);
        }
        catch (ValidationException ex)
        {
            return ResponseBuilder.CreateErrorResponse(400, ex.Message, context.Logger);
        }

        try
        {
            await _cognitoClient.ForgotPasswordAsync(new Amazon.CognitoIdentityProvider.Model.ForgotPasswordRequest
            {
                ClientId = ResourceNames.ClientId,
                Username = forgotPasswordRequest.Email,
            });
        }
        catch (UserNotFoundException)
        {
            // Deliberately swallowed: the response must not reveal whether the email exists.
        }
        catch (Exception ex)
        {
            context.Logger.LogLine($"ForgotPassword request failed for an email: {ex.Message}");
        }

        return ResponseBuilder.CreateSuccessResponse(GenericSuccessMessage);
    }

    private static bool TryDeserializeRequest(APIGatewayProxyRequest request, out ForgotPasswordRequest forgotPasswordRequest)
    {
        try
        {
            forgotPasswordRequest = JsonSerializer.Deserialize<ForgotPasswordRequest>(request.Body);
            return forgotPasswordRequest != null;
        }
        catch (JsonException)
        {
            forgotPasswordRequest = null;
            return false;
        }
    }
}
