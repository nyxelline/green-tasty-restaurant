using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.Feedbacks;
using APIHandler.Services.Feedbacks;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Feedbacks;

public class UpdateFeedbackHandler : IApiRequestHandler
{
    private readonly IFeedbackService _feedbackService;

    public UpdateFeedbackHandler(IFeedbackService feedbackService)
    {
        _feedbackService = feedbackService ?? throw new ArgumentNullException(nameof(feedbackService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!TryGetUserId(request, out var userId, out var errorMessage))
        {
            return ResponseBuilder.CreateErrorResponse(401, errorMessage!, context.Logger);
        }

        if (!TryParseRequestBody(request, out var feedbackRequest, out errorMessage))
        {
            return ResponseBuilder.CreateErrorResponse(400, errorMessage!, context.Logger);
        }

        // Ownership is checked against the feedback actually stored for this reservation/type,
        // never against a UserId the client could simply omit or forge in the request body.
        var existingFeedback = await _feedbackService.GetFeedbackForReservationAsync(feedbackRequest!.ReservationId);
        var ownedFeedback = existingFeedback
            .Where(f => f.Type == feedbackRequest.Type)
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefault();

        if (ownedFeedback is null)
        {
            return ResponseBuilder.CreateErrorResponse(404, "No feedback of this type found for the reservation.", context.Logger);
        }

        if (ownedFeedback.UserId != userId)
        {
            return ResponseBuilder.CreateErrorResponse(403, "You can only edit your own feedback.", context.Logger);
        }

        feedbackRequest.UserId = userId;

        try
        {
            await _feedbackService.UpdateFeedbackAsync(feedbackRequest);
            return ResponseBuilder.CreateSuccessResponse("Feedback updated successfully.");
        }
        catch (ValidationException ex)
        {
            context.Logger.LogError($"Invalid request: {ex.Message}");
            return ResponseBuilder.CreateErrorResponse(400, ex.Message, context.Logger);
        }
    }

    private bool TryParseRequestBody(
        APIGatewayProxyRequest request,
        out CreateFeedbackRequest? feedbackRequest,
        out string? errorMessage)
    {
        try
        {
            feedbackRequest = JsonSerializer.Deserialize<CreateFeedbackRequest>(request.Body);
            errorMessage = null;
            return feedbackRequest is not null;
        }
        catch (Exception ex)
        {
            feedbackRequest = null;
            errorMessage = ex.Message;
            return false;
        }
    }

    private bool TryGetUserId(
        APIGatewayProxyRequest request,
        out string? userId,
        out string? errorMessage)
    {
        if (request.RequestContext.Authorizer is null ||
            !request.RequestContext.Authorizer.Claims.TryGetValue("sub", out var sub) ||
            string.IsNullOrWhiteSpace(sub))
        {
            userId = null;
            errorMessage = "User is not authenticated or 'sub' claim is missing.";
            return false;
        }

        userId = sub;
        errorMessage = null;
        return true;
    }
}