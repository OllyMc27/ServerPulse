using System.Net;
using System.Security.Claims;
using Data.Models.Client;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ServerPulse;

public sealed class GuidanceAdminEndpointStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<GuidanceAdminEndpointMiddleware>();
        next(app);
    };
}

public sealed class GuidanceAdminEndpointMiddleware(
    RequestDelegate next,
    AnalyticsStore store,
    ServerPulseConfig config,
    DemosToDiscordIntegrationClient integration,
    IAntiforgery antiforgery)
{
    private static readonly PathString BasePath = new("/api/serverpulse/guidance");

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) ||
            !context.Request.Path.StartsWithSegments(BasePath, StringComparison.OrdinalIgnoreCase, out var remaining))
        {
            await next(context);
            return;
        }

        await PopulateUserAsync(context);
        if (!await IsAdministratorAsync(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (!context.Request.HasFormContentType)
        {
            await WriteStatusAsync(context, StatusCodes.Status415UnsupportedMediaType, "A form submission was expected.");
            return;
        }
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            await WriteStatusAsync(context, StatusCodes.Status400BadRequest,
                "The form expired or could not be verified. Refresh the page and try again.");
            return;
        }

        var id = (remaining.Value ?? string.Empty).Trim('/');
        var item = store.GetPlayerGuidanceEvent(id);
        if (item is null)
        {
            await WriteStatusAsync(context, StatusCodes.Status404NotFound, "The guidance event was not found.");
            return;
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var operation = Value(form, "operation") ?? string.Empty;
        var actorId = CurrentClientId(context.User) ?? 0;
        var actorName = CurrentName(context.User);
        var notes = (Value(form, "notes") ?? string.Empty).Trim();
        if (notes.Length > 500)
        {
            await WriteStatusAsync(context, StatusCodes.Status400BadRequest, "Review notes must be 500 characters or fewer.");
            return;
        }

        if (operation.Equals("Dismiss", StringComparison.OrdinalIgnoreCase))
        {
            store.UpdatePlayerGuidanceEvent(id, value =>
            {
                value.ReviewStatus = "Dismissed";
                value.ResolutionMethod = "Dismissed by administrator";
                value.ResolvedByClientId = actorId;
                value.ResolvedByName = actorName;
                value.ResolvedAt = DateTimeOffset.UtcNow;
                value.ReviewNotes = notes;
                value.DemosToDiscordError = string.Empty;
            });
            Redirect(context, "dismissed");
            return;
        }

        var createCase = operation.Equals("ResolveAndCreateCase", StringComparison.OrdinalIgnoreCase) ||
                         operation.Equals("RetryCase", StringComparison.OrdinalIgnoreCase);
        if (!operation.Equals("Resolve", StringComparison.OrdinalIgnoreCase) && !createCase)
        {
            await WriteStatusAsync(context, StatusCodes.Status400BadRequest, "Unknown guidance review operation.");
            return;
        }

        if (!operation.Equals("RetryCase", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(Value(form, "targetClientId"), out var targetId))
            {
                await WriteStatusAsync(context, StatusCodes.Status400BadRequest, "Select the accused player.");
                return;
            }
            var target = item.PlayersAtCapture.FirstOrDefault(value => value.ClientId == targetId && !value.IsBot &&
                !value.PlayerKey.Equals(item.ReporterKey, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                await WriteStatusAsync(context, StatusCodes.Status400BadRequest,
                    "The selected player was not in the retained match snapshot.");
                return;
            }
            store.UpdatePlayerGuidanceEvent(id, value =>
            {
                value.TargetClientId = target.ClientId;
                value.TargetNetworkId = target.NetworkId;
                value.TargetKey = target.PlayerKey;
                value.TargetName = target.PlayerName;
                value.ResolutionMethod = "Manual administrator resolution";
                value.ReviewStatus = createCase ? "CaseQueued" : "ManuallyResolved";
                value.ResolvedByClientId = actorId;
                value.ResolvedByName = actorName;
                value.ResolvedAt = DateTimeOffset.UtcNow;
                value.ReviewNotes = notes;
                value.DemosToDiscordError = string.Empty;
            });
            item = store.GetPlayerGuidanceEvent(id)!;
        }

        if (!createCase)
        {
            Redirect(context, "resolved");
            return;
        }
        if (!config.PlayerGuidance.EnableDemosToDiscordEscalation)
        {
            await WriteStatusAsync(context, StatusCodes.Status409Conflict,
                "DemosToDiscord escalation is disabled in ServerPulse configuration.");
            return;
        }
        if (item.TargetClientId is null || item.TargetNetworkId is null)
        {
            await WriteStatusAsync(context, StatusCodes.Status400BadRequest, "Resolve the target before creating a case.");
            return;
        }

        try
        {
            var caseId = await integration.SubmitAsync(new DemosToDiscordCaseRequest(
                item.Id,
                item.CapturedAt.UtcDateTime,
                item.ServerId,
                item.ServerName,
                item.LegacyServerId,
                item.Game,
                item.Map,
                item.Mode,
                item.TargetClientId.Value,
                item.TargetNetworkId.Value,
                item.TargetName,
                item.Category,
                item.Excerpt ?? string.Empty,
                item.ContextMessages.OrderBy(value => value.CapturedAt)
                    .Select(value => $"{value.CapturedAt:HH:mm:ss} {value.PlayerName}: {value.Message}").ToArray(),
                actorId,
                actorName,
                notes), context.RequestAborted);
            store.UpdatePlayerGuidanceEvent(id, value =>
            {
                value.ReviewStatus = "CaseCreated";
                value.DemosToDiscordCaseId = caseId;
                value.DemosToDiscordError = string.Empty;
            });
            Redirect(context, "case-created");
        }
        catch (Exception exception)
        {
            store.UpdatePlayerGuidanceEvent(id, value =>
            {
                value.ReviewStatus = "CaseFailed";
                value.DemosToDiscordError = exception.GetBaseException().Message;
            });
            Redirect(context, "case-failed");
        }
    }

    private async Task<bool> IsAdministratorAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return false;
        var authorization = context.RequestServices.GetService<IAuthorizationService>();
        if (authorization is null ||
            !(await authorization.AuthorizeAsync(context.User, null, "Permissions.AdminMenu.Read")).Succeeded)
            return false;
        return Enum.TryParse<EFClient.Permission>(context.User.FindFirstValue(ClaimTypes.Role), true, out var permission) &&
               permission >= config.WebfrontMinimumPermission;
    }

    private static async Task PopulateUserAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            return;
        try
        {
            var result = await context.AuthenticateAsync();
            if (result.Succeeded && result.Principal is not null)
                context.User = result.Principal;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string? Value(IFormCollection form, string key) =>
        form.TryGetValue(key, out var value) ? value.ToString() : null;
    private static int? CurrentClientId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.Sid), out var id) ? id : null;
    private static string CurrentName(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Administrator";
    private static void Redirect(HttpContext context, string result) => context.Response.Redirect(
        $"/Interaction/Render/{ServerPulseWebfront.InteractionKey}?view=guidance&period=30&saved={WebUtility.UrlEncode(result)}");
    private static async Task WriteStatusAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(message, context.RequestAborted);
    }
}
