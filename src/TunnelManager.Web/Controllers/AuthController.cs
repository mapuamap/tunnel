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

    public AuthController(IConfiguration configuration, ILogger<AuthController> logger)
    {
        _configuration = configuration;
        _logger = logger;
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
            return Ok(new { success = true, username = request.Username });
        }

        _logger.LogWarning("[Auth] Login failed for user '{Username}'", request.Username);
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

            var targetUrl = string.IsNullOrEmpty(returnUrl) ? "/" : Uri.UnescapeDataString(returnUrl);
            return Redirect(targetUrl);
        }

        _logger.LogWarning("[Auth] Form login failed for user '{Username}'", username);
        return Redirect("/login?error=Invalid+username+or+password");
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("[Auth] User logged out");
        return Ok(new { success = true });
    }

    [HttpGet("logout-redirect")]
    public async Task<IActionResult> LogoutRedirect()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("[Auth] User logged out via redirect");
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
