using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace TunnelManager.Web.Services;

public class ServerAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ServerAuthenticationStateProvider> _logger;
    private Task<AuthenticationState>? _cachedAuthState;

    public ServerAuthenticationStateProvider(
        IHttpContextAccessor httpContextAccessor,
        ILogger<ServerAuthenticationStateProvider> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_cachedAuthState != null)
        {
            return _cachedAuthState;
        }

        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            _logger.LogDebug("[AuthState] User authenticated: {User}", httpContext.User.Identity.Name);
            _cachedAuthState = Task.FromResult(new AuthenticationState(httpContext.User));
            return _cachedAuthState;
        }

        if (httpContext != null)
        {
            _logger.LogDebug("[AuthState] User not authenticated (HttpContext available)");
            _cachedAuthState = Task.FromResult(
                new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
            return _cachedAuthState;
        }

        _logger.LogWarning("[AuthState] HttpContext is null - returning unauthenticated");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
