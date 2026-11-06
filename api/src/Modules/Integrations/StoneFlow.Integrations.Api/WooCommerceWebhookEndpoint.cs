using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoneFlow.Integrations.Application.WooCommerce;
using StoneFlow.Shared.Application.Cqrs;

namespace StoneFlow.Integrations.Api;

/// <summary>
/// <c>POST /webhooks/woocommerce/orders</c> (SF-12): poza <c>/api</c>, więc bez CSRF i ciasteczek;
/// uwierzytelnienie podpisem HMAC w handlerze. Bez treści odpowiedzi.
/// </summary>
internal static class WooCommerceWebhookEndpoint
{
    public const string Route = "/webhooks/woocommerce/orders";

    public const string RateLimitPolicy = "woocommerce-webhook";

    public const int MaxBodyBytes = 1024 * 1024;

    public static void AddRateLimit(IServiceCollection services) =>
        services.AddRateLimiter(options => options.AddPolicy(RateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(Route, ReceiveAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicy)
            .ExcludeFromDescription();

    private static async Task<IResult> ReceiveAsync(HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = MaxBodyBytes;
        }

        if (context.Request.ContentLength > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        byte[] body;
        try
        {
            body = await ReadBodyAsync(context.Request, cancellationToken);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (body.Length > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var headers = context.Request.Headers;
        var outcome = await dispatcher.SendAsync(
            new ReceiveWooCommerceWebhookCommand(
                body,
                headers["X-WC-Webhook-Signature"].ToString(),
                headers["X-WC-Webhook-Topic"].ToString(),
                headers["X-WC-Webhook-Delivery-ID"].ToString()),
            cancellationToken);

        return Results.StatusCode(outcome switch
        {
            WebhookOutcome.Accepted => StatusCodes.Status200OK,
            WebhookOutcome.Unauthorized => StatusCodes.Status401Unauthorized,
            WebhookOutcome.Disabled => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status503ServiceUnavailable,
        });
    }

    /// <summary>Surowe bajty treści, najwyżej <see cref="MaxBodyBytes"/> + 1 (nadmiar → 413).</summary>
    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBodyBytes)
            {
                break;
            }
        }

        return buffer.ToArray();
    }
}
