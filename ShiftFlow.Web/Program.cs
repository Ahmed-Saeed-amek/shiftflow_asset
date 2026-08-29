using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ShiftFlow.Application.AI;
using ShiftFlow.Application.Services;
using ShiftFlow.Domain.Entities;
using ShiftFlow.Infrastructure.Data;
using ShiftFlow.Web.Authorization;
using ShiftFlow.Web.Localization;
using ShiftFlow.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Suppress the "Server: Kestrel" response header — reveals server technology to any
// unauthenticated caller for no functional benefit (OWASP A02 Security Misconfiguration).
// Also pin TLS to 1.2/1.3 only: TLS 1.0/1.1 are deprecated, still negotiable by default on
// this host, and offered alongside legacy CBC cipher suites vulnerable to BEAST/LUCKY13.
builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.ConfigureHttpsDefaults(https =>
        https.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13);
});

builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

// IIS in-process hosting (web.config hostingModel="inprocess") auto-surfaces
// whatever identity IIS itself authenticated (e.g. a site-wide Basic Auth
// gate, as used by SmarterASP.NET's temp .ftempurl.com domains) into
// HttpContext.User. Without this, an anonymous visitor who's only passed
// IIS's Basic Auth gets treated by [Authorize(Roles=...)] as "authenticated
// but missing our own role claims" -> Forbid -> AccessDenied, instead of
// "not authenticated" -> Challenge -> redirect to Login. This forces the
// app to rely solely on its own Identity cookie, ignoring IIS's identity.
builder.Services.Configure<IISServerOptions>(o =>
{
    o.AutomaticAuthentication = false;
});

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/sf-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(o =>
{
    // RequireDigit/RequireLowercase/RequireUppercase already default to true. The 8-char
    // minimum and lack of a required special character fell short of a reasonable modern
    // policy; bumped to 12 (every seeded demo password already satisfies this) plus a
    // required special character.
    o.Password.RequiredLength = 12;
    o.Password.RequireNonAlphanumeric = true;
    o.SignIn.RequireConfirmedEmail = false;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    o.Lockout.MaxFailedAccessAttempts = 5;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.HttpOnly = true;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;

    // fetch()/XHR calls never set an "Accept: text/html" the way a real page
    // navigation does. Without this, Identity turns Forbid()/Challenge() into a
    // 302 to the login/access-denied page, which fetch() follows and reports
    // back as a plain 200 OK — callers can't tell the request actually failed.
    static bool IsApiRequest(HttpRequest request) =>
        request.Headers.XRequestedWith == "XMLHttpRequest" ||
        !request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    o.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (IsApiRequest(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    o.Events.OnRedirectToLogin = ctx =>
    {
        if (IsApiRequest(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

// "Sign in with Microsoft" (Azure AD / Entra ID) — registered as an
// additional, non-default scheme so it never displaces the Identity
// cookie set up above (AddAuthentication() with no args here is
// deliberate: giving it a default-scheme argument would hijack the
// XHR-aware 401/403 challenge logic in ConfigureApplicationCookie for
// every existing endpoint, not just the new external-login flow).
//
// Only register the scheme when a tenant/client ID is actually configured.
// ASP.NET Core's AuthenticationMiddleware eagerly validates every
// registered scheme's options on every request (OpenIdConnectOptions.
// Validate() throws on an empty ClientId) — an unconfigured placeholder
// would 500 the entire app, not just the Entra login path, until real
// credentials are supplied.
var azureAdSection = builder.Configuration.GetSection("AzureAd");
var azureAdConfigured = !string.IsNullOrWhiteSpace(azureAdSection["ClientId"])
                      && !string.IsNullOrWhiteSpace(azureAdSection["TenantId"]);
if (azureAdConfigured)
{
    builder.Services.AddAuthentication()
        .AddOpenIdConnect("EntraID", "Microsoft Entra ID", o =>
        {
            o.SignInScheme = IdentityConstants.ExternalScheme;
            // "Instance"/"TenantId" aren't real OpenIdConnectOptions properties (that's
            // a Microsoft.Identity.Web binding convenience) — read them from config
            // directly and build Authority ourselves.
            o.ClientId = azureAdSection["ClientId"];
            o.ClientSecret = azureAdSection["ClientSecret"];
            o.Authority = $"{azureAdSection["Instance"]}{azureAdSection["TenantId"]}/v2.0";
            o.CallbackPath = azureAdSection["CallbackPath"] ?? "/signin-oidc-entra";
            o.ResponseType = "code";
            o.SaveTokens = false;
            o.Scope.Add("email");
            o.Events = new OpenIdConnectEvents
            {
                // Without this, a declined consent screen (or any other error
                // Microsoft redirects back with, e.g. access_denied) throws an
                // OpenIdConnectProtocolException that bubbles up as an unhandled
                // exception page instead of a normal login-page redirect.
                OnRemoteFailure = ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.Redirect("/Account/Login?remoteError=" +
                        Uri.EscapeDataString(ctx.Failure?.Message ?? "Sign-in was cancelled."));
                    return Task.CompletedTask;
                },
            };
        });
}

builder.Services.AddHttpContextAccessor();

// Application services
builder.Services.AddScoped<IInspectionOrderService, InspectionOrderService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ILanguageService, LanguageService>();

// Asset Management services
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<IVendorService, VendorService>();
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
builder.Services.AddScoped<IMaintenanceOrderService, MaintenanceOrderService>();
builder.Services.AddScoped<IContractService, ContractService>();
builder.Services.AddScoped<IAssetScopeService, AssetScopeService>();
builder.Services.AddHostedService<PreventiveMaintenanceSchedulerService>();

// AI Assistant (Rased pattern: OpenAI direct key preferred, Azure OpenAI fallback)
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.Configure<AzureSpeechOptions>(builder.Configuration.GetSection("AzureSpeech"));
builder.Services.Configure<AiAssistantOptions>(builder.Configuration.GetSection("AiAssistant"));
builder.Services.AddScoped<IAiInspectionToolFunctions, AiInspectionToolFunctions>();
builder.Services.AddScoped<AiAssistantOrchestrator>();

// Asset repair-video lookup (getAssetRepairGuidance tool) — scoped to one asset ID at a time,
// see AssetRepairGuidanceService's doc comment for why this can't become a general video search.
builder.Services.Configure<YouTubeOptions>(builder.Configuration.GetSection("YouTube"));
builder.Services.AddSingleton<IYouTubeQuotaTracker, YouTubeQuotaTracker>();
builder.Services.AddScoped<IAssetRepairGuidanceService, AssetRepairGuidanceService>();
builder.Services.AddHttpClient("YouTube", client =>
{
    client.BaseAddress = new Uri("https://www.googleapis.com/youtube/v3/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Per-user throttle on the AI Assistant endpoints — each Query call can chain up to
// AiAssistantOrchestrator.MaxIterations LLM completions, so without a limit an authenticated
// user could drive uncontrolled LLM/Speech billing or hammer the endpoint as a DoS vector.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("ai", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 15,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Identity's built-in lockout (5 failed attempts -> 5 min) only kicks in per-account, so a
    // distributed credential-stuffing run spread across many emails from one IP is otherwise
    // unthrottled. Partition by IP (not by the submitted email, which an attacker controls and
    // can rotate freely) so this catches that case independently of the per-account lockout.
    options.AddPolicy("login", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// Email + WhatsApp
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("SpeechToken");
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IWhatsAppService, TwilioWhatsAppService>();

// Entra directory search (app-only Graph). Always registered (unlike the OIDC scheme
// above) so UsersController's constructor injection never fails to resolve — with blank
// AzureAd credentials the service still constructs fine, it just fails clearly the moment
// SearchAsync is actually called, same as GraphEntraDirectoryService's other error paths.
builder.Services.AddScoped<IEntraDirectoryService, GraphEntraDirectoryService>();

// RBAC
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, AdminClaimsTransformation>();

// Register one ASP.NET Core policy per permission name
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in PermissionCatalog.All)
        options.AddPolicy(permission, policy =>
            policy.Requirements.Add(new PermissionRequirement(permission)));
});

// Cookie-based validation-message localization backed by Translations.
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory,
    ShiftFlow.Web.Localization.TranslationsStringLocalizerFactory>();

var mvcBuilder = builder.Services.AddControllersWithViews(options =>
{
    // Read-only actions rely on default convention-based routing with no explicit [HttpGet],
    // so by default they'd accept ANY HTTP verb (PUT/DELETE/TRACE/invented) identically to GET.
    // This restricts any action with no HTTP-verb-selector attribute of its own to GET/HEAD —
    // actions already carrying [HttpPost] etc. (every write action in this app) are untouched.
    options.Conventions.Add(new ShiftFlow.Web.Infrastructure.Mvc.DefaultGetOnlyConvention());
})
.AddDataAnnotationsLocalization(options =>
{
    options.DataAnnotationLocalizerProvider = (type, factory) => factory.Create(type);
});

if (builder.Environment.IsDevelopment())
    mvcBuilder.AddRazorRuntimeCompilation();

// The antiforgery cookie carries the CSRF-token-binding value validated against
// __RequestVerificationToken on every state-changing POST. Force Secure explicitly in
// Production instead of relying on the framework default (SameAsRequest, which only adds
// Secure when the current request happens to be HTTPS).
// Forcing Always unconditionally was tried and reverted: this app's own dev preview serves
// plain HTTP (no working HTTPS redirect target in that environment — "Failed to determine
// the https port for redirect" — so UseHttpsRedirection() below doesn't save it), and
// DefaultAntiforgery throws InvalidOperationException on every <form>-rendering GET, not just
// POSTs, the moment Cookie.SecurePolicy=Always meets a non-SSL request — this 500'd the entire
// app, confirmed live. Production behind real TLS is unaffected either way.
builder.Services.AddAntiforgery(options =>
{
    if (!builder.Environment.IsDevelopment())
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
});

// Model-binding failures (e.g. a required dropdown/picker posted as "" — can't convert "" to int)
// are raised by the binder itself, before DataAnnotations/IValidatableObject ever run, so
// AddDataAnnotationsLocalization above doesn't cover them. Route the two most common ones
// (empty value posted for a non-nullable field, and a value that fails to parse) through the
// same Translations table so a picker left unselected shows a friendly localized message
// instead of the framework's raw "The value '' is invalid." text.
builder.Services.AddOptions<Microsoft.AspNetCore.Mvc.MvcOptions>().PostConfigure<IHttpContextAccessor>((options, http) =>
{
    string Lang() => http.HttpContext?.Request.Cookies[ShiftFlow.Web.Localization.LanguageService.CookieName] == "ar" ? "ar" : "en";
    var p = options.ModelBindingMessageProvider;
    p.SetValueMustNotBeNullAccessor(_ =>
        ShiftFlow.Web.Localization.Translations.T(Lang(), "Please select a value."));
    p.SetAttemptedValueIsInvalidAccessor((_, _) =>
        ShiftFlow.Web.Localization.Translations.T(Lang(), "Please select a value."));
    p.SetMissingKeyOrValueAccessor(() =>
        ShiftFlow.Web.Localization.Translations.T(Lang(), "Please select a value."));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Friendly branded page for 404/403/etc. that reach the end of the pipeline with no body of
// their own (e.g. a bad URL, or an [Authorize] policy failure) — separate from UseExceptionHandler
// above, which only covers unhandled exceptions. Active in all environments so a 404 always looks
// the same regardless of Development vs Production.
app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

app.UseResponseCompression();
app.UseHttpsRedirection();

// Security headers. script-src/style-src allow 'unsafe-inline' because most views embed
// inline <script>/<style> blocks directly (no nonce plumbing exists yet) — tightening to a
// nonce-based CSP would need that added to every view and is a larger follow-up, not done here.
// connect-src/media-src/worker-src are scoped wide for the Azure Speech SDK used by the AI
// Assistant's voice/avatar feature (dynamic per-region WebSocket + WebRTC signaling endpoints).
// img-src additionally allows jsdelivr (Leaflet's default marker icons) and the OSM tile
// subdomains (a/b/c.tile.openstreetmap.org) used by the Asset Locations map — Leaflet itself
// (JS/CSS) already loads fine via the existing jsdelivr script-src/style-src allowance.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "microphone=(self), camera=(), geolocation=()";
    // Flagged missing by a WSTG-methodology pentest pass (nuclei/nikto) on a sibling app sharing
    // this codebase's lineage — cheap, no-compat-risk additions, no reason not to carry them here too.
    headers["Cross-Origin-Opener-Policy"] = "same-origin";
    headers["Cross-Origin-Resource-Policy"] = "same-origin";
    headers["X-Permitted-Cross-Domain-Policies"] = "none";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "font-src 'self' data: https://cdn.jsdelivr.net; " +
        "img-src 'self' data: https://cdn.jsdelivr.net https://*.tile.openstreetmap.org; " +
        "connect-src 'self' https://cdn.jsdelivr.net https://*.cognitiveservices.azure.com https://*.speech.microsoft.com wss://*.speech.microsoft.com https://graph.microsoft.com; " +
        "media-src 'self' blob:; " +
        "worker-src 'self' blob:; " +
        // youtube-nocookie.com only, matching the exact embed URL getAssetRepairGuidance's
        // video suggestions render as — no default-src fallback would otherwise permit it.
        "frame-src https://www.youtube-nocookie.com; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self';";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var db  = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var um  = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var rm  = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    await DbSeeder.SeedAsync(db, um, rm);
}

app.Run();