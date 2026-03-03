using Logger_MM.Agent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TunnelManager.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly LoggerMMAgent? _loggerMM;

    public AuthController(IConfiguration configuration, ILogger<AuthController> logger, LoggerMMAgent? loggerMM = null)
    {
        _configuration = configuration;
        _logger = logger;
        _loggerMM = loggerMM;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { error = "Username and password are required" });
        }

        var isValid = ValidateCredentials(request.Username, request.Password);

        if (isValid)
        {
            await SignInUserAsync(request.Username);
            _logger.LogInformation("[Auth] User '{Username}' logged in successfully", request.Username);
            _loggerMM?.UserAction("AuthController", "Login", $"User logged in successfully",
                user: request.Username,
                @params: new { username = request.Username },
                tags: new[] { "auth", "login" });
            return Ok(new { success = true, username = request.Username });
        }

        _logger.LogWarning("[Auth] Login failed for user '{Username}'", request.Username);
        _loggerMM?.UserAction("AuthController", "Login", $"Login failed - invalid credentials",
            user: request.Username,
            @params: new { username = request.Username },
            tags: new[] { "auth", "login", "failure" });
        return Unauthorized(new { error = "Invalid username or password" });
    }

    [HttpPost("login-form")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginForm([FromForm] string username, [FromForm] string password, [FromForm] string? returnUrl)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return Redirect("/login?error=Username+and+password+are+required");
        }

        var isValid = ValidateCredentials(username, password);

        if (isValid)
        {
            await SignInUserAsync(username);
            _logger.LogInformation("[Auth] User '{Username}' logged in successfully via form", username);
            _loggerMM?.UserAction("AuthController", "LoginForm", $"User logged in successfully via form",
                user: username,
                @params: new { username, returnUrl },
                tags: new[] { "auth", "login" });

            var targetUrl = string.IsNullOrEmpty(returnUrl) ? "/" : Uri.UnescapeDataString(returnUrl);
            return Redirect(targetUrl);
        }

        _logger.LogWarning("[Auth] Form login failed for user '{Username}'", username);
        _loggerMM?.UserAction("AuthController", "LoginForm", $"Form login failed - invalid credentials",
            user: username,
            @params: new { username },
            tags: new[] { "auth", "login", "failure" });
        return Redirect("/login?error=Invalid+username+or+password");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name ?? "unknown";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("[Auth] User logged out");
        _loggerMM?.UserAction("AuthController", "Logout", $"User logged out",
            user: username,
            tags: new[] { "auth", "logout" });
        return Ok(new { success = true });
    }

    [HttpGet("logout-redirect")]
    public async Task<IActionResult> LogoutRedirect()
    {
        var username = User.Identity?.Name ?? "unknown";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("[Auth] User logged out via redirect");
        _loggerMM?.UserAction("AuthController", "LogoutRedirect", $"User logged out via redirect",
            user: username,
            tags: new[] { "auth", "logout" });
        return Redirect("/login");
    }

    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(new { error = "Not authenticated" });
        }

        var username = User.Identity.Name;
        return Ok(new { username });
    }

    private bool ValidateCredentials(string username, string password)
    {
        var configUsername = _configuration["Auth:Username"];
        var configPassword = _configuration["Auth:Password"];

        return username.Equals(configUsername, StringComparison.OrdinalIgnoreCase) &&
               password == configPassword;
    }

    private async Task SignInUserAsync(string username)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, "Admin")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
            });
    }
}

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
