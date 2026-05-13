using Logger_MM.Agent;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using MudBlazor.Services;
using Npgsql;
using TunnelManager.Web.Components;
using TunnelManager.Web.Data;
using TunnelManager.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Trust X-Forwarded-Proto/-For from the front-line reverse proxy (tunnelmanager_nginx).
// Without this, Blazor Server and cookie auth see HTTP and emit insecure cookies.
builder.Services.Configure<ForwardedHeadersOptions>(opt =>
{
    opt.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opt.KnownNetworks.Clear();
    opt.KnownProxies.Clear();
});

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

// Add StatsDbContext for PostgreSQL
builder.Services.AddDbContext<StatsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("StatsConnection")));

// Add SSH Service as singleton (persistent connections, thread-safe)
builder.Services.AddSingleton<SshService>();

// Add application services as singletons (they use the singleton SshService + internal caching)
builder.Services.AddSingleton<NginxService>();
builder.Services.AddSingleton<NginxAuthService>();
builder.Services.AddSingleton<SshTunnelService>();
builder.Services.AddSingleton<WireGuardService>();
builder.Services.AddSingleton<FolderService>();

// Add health check service
builder.Services.AddSingleton<HealthCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HealthCheckService>());

// Add background service for stats collection
builder.Services.AddHostedService<StatsCollectorService>();

// Add authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/api/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        // Stage 4: harden cookie. App always runs behind TLS-terminating nginx now.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.HttpOnly = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
builder.Services.AddHttpContextAccessor();

// Add controllers for API endpoints.
// `WithViews` is needed so [ValidateAntiForgeryToken] filter resolves its DI dependencies.
builder.Services.AddControllersWithViews();

// Stage 5: rate-limit login endpoints to mitigate brute-force. 10 attempts / minute / IP.
// Real client IP comes from X-Forwarded-For (already trusted via ForwardedHeaders middleware).
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opt.OnRejected = async (context, token) =>
    {
        // Write the 429 body before the global StatusCodePages middleware can re-execute it.
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"error\":\"Too many requests\",\"retryAfterSeconds\":60}", token);
    };
    opt.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

// Configure DataProtection to use persistent storage
var keysPath = Path.Combine("/app", "data", "keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("TunnelManager");

// Register Logger_MM.Agent as singleton
builder.Services.AddSingleton<LoggerMMAgent>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new LoggerMMAgent(
        observerUrl: config["LoggerMM:Url"] ?? throw new InvalidOperationException("LoggerMM:Url not configured"),
        apiKey: config["LoggerMM:ApiKey"] ?? throw new InvalidOperationException("LoggerMM:ApiKey not configured"),
        appName: config["LoggerMM:AppName"] ?? "TunnelManager",
        appVersion: "1.0.0",
        environment: builder.Environment.EnvironmentName
    );
});

var app = builder.Build();

// Ensure databases are created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();

            // Create tunnel_stats database if it doesn't exist
            var statsConnectionString = app.Configuration.GetConnectionString("StatsConnection");
            if (!string.IsNullOrEmpty(statsConnectionString))
            {
                try
                {
                    // Parse connection string to get server info
                    var connBuilder = new Npgsql.NpgsqlConnectionStringBuilder(statsConnectionString);
                    var dbName = connBuilder.Database;
                    connBuilder.Database = "postgres"; // Connect to default database to create our database

                    using var tempConnection = new Npgsql.NpgsqlConnection(connBuilder.ConnectionString);
                    tempConnection.Open();

                    // Check if database exists
                    var checkDbCommand = tempConnection.CreateCommand();
                    checkDbCommand.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{dbName}'";
                    var exists = checkDbCommand.ExecuteScalar() != null;

                    if (!exists)
                    {
                        // Create database
                        var createDbCommand = tempConnection.CreateCommand();
                        createDbCommand.CommandText = $"CREATE DATABASE {dbName}";
                        createDbCommand.ExecuteNonQuery();
                    }

                    tempConnection.Close();

                    // Ensure StatsDbContext schema is created
                    var statsDbContext = scope.ServiceProvider.GetRequiredService<StatsDbContext>();
                    statsDbContext.Database.EnsureCreated();
                }
                catch (Exception ex)
                {
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                    logger.LogWarning(ex, "Failed to setup PostgreSQL stats database. Stats collection will be disabled. Error: {Error}", ex.Message);
                }
            }
            else
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("StatsConnection string is not configured. Stats collection will be disabled.");
            }
}

// Start Logger_MM.Agent
var loggerAgent = app.Services.GetRequiredService<LoggerMMAgent>();
await loggerAgent.StartAsync();

// Stop Logger_MM.Agent on shutdown
app.Lifetime.ApplicationStopping.Register(() =>
{
    loggerAgent.StopAsync().GetAwaiter().GetResult();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Disable HTTPS redirection for HTTP-only deployment
// app.UseHttpsRedirection();

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapControllers();

app.Run();
