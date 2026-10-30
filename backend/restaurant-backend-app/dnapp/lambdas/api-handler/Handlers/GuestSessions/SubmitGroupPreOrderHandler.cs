using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GuestSessions;
using APIHandler.Services.GuestSessions;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

/// <summary>
/// Owner-only: consolidates every submitted guest cart in the group into the reservation's own
/// order (merging identical dishes by dishId), marks that order SubmittedByCustomer, and marks
/// the group reservation Submitted. Guest dishes never reach the kitchen order before this call.
/// </summary>
public class SubmitGroupPreOrderHandler : IApiRequestHandler
{
    private readonly IGuestSessionService _guestSessionService;

    public SubmitGroupPreOrderHandler(IGuestSessionService guestSessionService)
    {
        _guestSessionService = guestSessionService ?? throw new ArgumentNullException(nameof(guestSessionService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Missing 'groupReservationId' in path parameters.", context.Logger);
        }

        if (!TryDeserializeRequest(request, out var submitRequest) || string.IsNullOrWhiteSpace(submitRequest?.ReservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Invalid request body: 'reservationId' is required.", context.Logger);
        }

        if (request.RequestContext?.Authorizer?.Claims == null ||
            !request.RequestContext.Authorizer.Claims.TryGetValue("sub", out var userId))
        {
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);
        }

        try
        {
            var mergedDishes = await _guestSessionService.SubmitGroupPreOrderAsync(userId, groupReservationId, submitRequest.ReservationId);
            return ResponseBuilder.CreateSuccessResponse(mergedDishes);
        }
        catch (GroupReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ActionForbiddenException ex)
        {
            return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger);
        }
        catch (GroupReservationExpiredException ex)
        {
            return ResponseBuilder.CreateErrorResponse(409, ex.Message, context.Logger);
        }
        catch (GroupReservationSubmittedException ex)
        {
            return ResponseBuilder.CreateErrorResponse(409, ex.Message, context.Logger);
        }
        catch (ValidationException ex)
        {
            return ResponseBuilder.CreateErrorCodeResponse(ex);
        }
    }

    private static bool TryDeserializeRequest(APIGatewayProxyRequest request, out SubmitGroupPreOrderRequest? submitRequest)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            submitRequest = JsonSerializer.Deserialize<SubmitGroupPreOrderRequest>(request.Body, options);
            return submitRequest != null;
        }
        catch (JsonException)
        {
            submitRequest = null;
            return false;
        }
    }
}
