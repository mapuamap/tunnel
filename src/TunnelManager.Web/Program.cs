using Logger_MM.Agent;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using TunnelManager.Web.Components;
using TunnelManager.Web.Data;
using TunnelManager.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor
builder.Services.AddMudServices(config =>
{
    config.PopoverOptions.ThrowOnDuplicateProvider = false;
});

// Add Entity Framework
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add SSH Service as singleton (persistent connections, thread-safe)
builder.Services.AddSingleton<SshService>();

// Add application services as singletons (they use the singleton SshService + internal caching)
builder.Services.AddSingleton<NginxService>();
builder.Services.AddSingleton<NginxAuthService>();
builder.Services.AddSingleton<SshTunnelService>();
builder.Services.AddSingleton<WireGuardService>();

// Add background service for stats collection
builder.Services.AddHostedService<StatsCollectorService>();

// Add authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/api/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
builder.Services.AddHttpContextAccessor();

// Add controllers for API endpoints
builder.Services.AddControllers();

// Configure DataProtection to use persistent storage
var keysPath = Path.Combine("/app", "data", "keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("TunnelManager");

// Register Logger_MM.Agent as singleton (optional – if config section is missing, services get null)
var loggerMmUrl = builder.Configuration["LoggerMM:Url"];
var loggerMmApiKey = builder.Configuration["LoggerMM:ApiKey"];
if (!string.IsNullOrEmpty(loggerMmUrl) && !string.IsNullOrEmpty(loggerMmApiKey))
{
    builder.Services.AddSingleton<LoggerMMAgent>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        return new LoggerMMAgent(
            observerUrl: loggerMmUrl,
            apiKey: loggerMmApiKey,
            appName: config["LoggerMM:AppName"] ?? "TunnelManager",
            appVersion: "1.0.0",
            environment: builder.Environment.EnvironmentName
        );
    });
}

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
}

// Start Logger_MM.Agent (if configured)
var loggerAgent = app.Services.GetService<LoggerMMAgent>();
if (loggerAgent != null)
{
    await loggerAgent.StartAsync();

    // Stop Logger_MM.Agent on shutdown
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        loggerAgent.StopAsync().GetAwaiter().GetResult();
    });
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Disable HTTPS redirection for HTTP-only deployment
// app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

app.Run();
