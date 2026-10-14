using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);

var resource = RequiredSetting(builder.Configuration, "Mcp:Resource");
var secretString = RequiredSetting(builder.Configuration, "Mcp:SecretString");
var scope = builder.Configuration["Mcp:Scope"] ?? "mcp.access";
var entraScope = builder.Configuration["Entra:Scope"] ?? $"{resource}/{scope}";
var tenantId = RequiredSetting(builder.Configuration, "Entra:TenantId");
var audience = RequiredSetting(builder.Configuration, "Entra:Audience");
var authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAudience = audience,
            NameClaimType = "name"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("McpTokenDiagnostics");
                var tokenAudiences = context.Principal?.FindAll("aud").Select(claim => claim.Value).ToArray() ?? [];
                var tokenScopes = context.Principal?.FindAll("scp")
                    .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .ToArray() ?? [];

                logger.LogInformation("Validated MCP token. Audiences: {Audiences}; scopes: {Scopes}",
                    string.Join(",", tokenAudiences), string.Join(",", tokenScopes));
                return Task.CompletedTask;
            }
        };
    })
    .AddMcp(options =>
    {
        options.ResourceMetadata = new()
        {
            Resource = resource,
            AuthorizationServers = { authority },
            ScopesSupported = [entraScope],
            BearerMethodsSupported = ["header"]
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("McpAccess", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(context => context.User.FindAll("scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(claimScope => claimScope.Equals(scope, StringComparison.Ordinal)
                || claimScope.Equals(entraScope, StringComparison.Ordinal))));
});

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<SecretStringTool>();
builder.Services.AddSingleton(secretString);

var app = builder.Build();
app.Use(async (context, next) =>
{
    await next();

    if (context.Response.StatusCode == StatusCodes.Status403Forbidden && context.Request.Path == "/mcp")
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("McpAuthorizationDiagnostics");
        var scopes = context.User.FindAll("scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        logger.LogWarning("MCP request forbidden. Authenticated: {Authenticated}; scopes: {Scopes}",
            context.User.Identity?.IsAuthenticated ?? false, string.Join(",", scopes));
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.MapMcp("/mcp").RequireAuthorization("McpAccess");

app.Run();


static string RequiredSetting(IConfiguration configuration, string key) =>
    configuration[key] is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Missing required configuration value: {key}");

[McpServerToolType]
public sealed class SecretStringTool(string secretString)
{
    [McpServerTool(Name = "get_secret_string"), Description("Returns the configured secret string for this authenticated connection test.")]
    public string GetSecretString() => secretString;
}
