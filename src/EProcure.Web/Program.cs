using System.Globalization;
using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Infrastructure;
using EProcure.Web.Infrastructure.Filters;
using EProcure.Web.Services;
using EProcure.Web.Services.External;
using EProcure.Web.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ---- Database ----
// The connection string is read from configuration (appsettings.Development.json locally,
// user-secrets or environment variables elsewhere). It is never hard-coded here.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddDbContext<EProcureDbContext>(options =>
    options.UseSqlServer(connectionString));

// ---- Tenancy ----
// Scoped = one instance per HTTP request, so each request sees its own user's organisation.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// ---- Identity (users, roles, password hashing) ----
// Login/registration pages are added in step 2; this registers the stores and password rules.
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<EProcureDbContext>()
    .AddDefaultTokenProviders()
    .AddTokenProvider<InviteTokenProvider<ApplicationUser>>(InviteTokenProvider<ApplicationUser>.ProviderName);

// Password-reset links work once (the password change updates the security stamp) and expire after 1 hour.
// Staff invitations use their own provider so they can last 3 days.
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.Configure<InviteTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(3));
// Re-check each signed-in user's security stamp every minute (default 30): a deactivated account, a role change or a
// password reset ends other sessions within a minute.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
// Add our custom claims principal factory so the signed-in user's ClaimsPrincipal
// includes the tenant claim "eprocure:org_id" when the user has an OrganisationId.
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, OrganisationClaimsPrincipalFactory>();
builder.Services.AddScoped<ICurrentOrganisation, CurrentOrganisation>();
builder.Services.AddScoped<IAuditService, AuditService>();
// ---- External services (swappable) ----
// Each outside service sits behind an interface. appsettings.json "ExternalServices" chooses the
// implementation, so moving to Azure Blob / PayFast / SMSPortal later is a new class + a config change.
var external = builder.Configuration.GetSection("ExternalServices");
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection("Uploads"));

switch (external["Otp"] ?? "Mock")
{
    case "Mock": builder.Services.AddScoped<IOtpSender, MockOtpSender>(); break;
    default: throw new InvalidOperationException($"Unknown ExternalServices:Otp provider '{external["Otp"]}'.");
}
switch (external["Email"] ?? "Mock")
{
    case "Mock": builder.Services.AddSingleton<IEmailSender, MockEmailSender>(); break;
    default: throw new InvalidOperationException($"Unknown ExternalServices:Email provider '{external["Email"]}'.");
}
switch (external["FileStorage"] ?? "Local")
{
    case "Local": builder.Services.AddSingleton<IFileStorage, LocalFileStorage>(); break;
    default: throw new InvalidOperationException($"Unknown ExternalServices:FileStorage provider '{external["FileStorage"]}'.");
}
switch (external["Payment"] ?? "Mock")
{
    case "Mock":
        builder.Services.AddSingleton<MockPaymentGateway>();
        builder.Services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<MockPaymentGateway>());
        break;
    default: throw new InvalidOperationException($"Unknown ExternalServices:Payment provider '{external["Payment"]}'.");
}
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<EnsurePhoneVerifiedFilter>();
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddScoped<ITenderService, TenderService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IEvaluationService, EvaluationService>();
builder.Services.AddScoped<ILinkBuilder, LinkBuilder>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IStaffService, StaffService>();

// Scheduled emails (closing-date reminders, "tender closed"): a background timer inside the app. See ReminderService.
builder.Services.Configure<ReminderOptions>(builder.Configuration.GetSection("Reminders"));
builder.Services.AddScoped(sp => new ReminderService(
    sp.GetRequiredService<DbContextOptions<EProcureDbContext>>(),
    db => new NotificationService(db, sp.GetRequiredService<IEmailSender>(), sp.GetRequiredService<ILinkBuilder>(), sp.GetRequiredService<ILogger<NotificationService>>()),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReminderOptions>>(),
    sp.GetRequiredService<ILogger<ReminderService>>()));
builder.Services.AddHostedService<ReminderWorker>();

// Configure the authentication cookie per product requirements:
// HttpOnly, Secure, SameSite=Lax, 8-hour sliding expiry. Also configure the
// paths for login and access denied to use our AccountController.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SupplierOnly", policy => policy.RequireRole(AppRoles.Supplier));
    options.AddPolicy("OrgStaff", policy => policy.RequireRole(AppRoles.OrgAdmin, AppRoles.Evaluator));
    options.AddPolicy("OrgAdminOnly", policy => policy.RequireRole(AppRoles.OrgAdmin));
    // Separation of duties: BEC members evaluate bids; the SCM Officer records the BAC's award decision.
    options.AddPolicy("BecOnly", policy => policy.RequireRole(AppRoles.Evaluator));
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddControllersWithViews(options =>
{
    // Protect every POST by default; public GET pages can opt out through [AllowAnonymous].
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    // Development only: bring the local database up to the latest migration on start-up, so pulling new
    // code and pressing Run is enough. Production databases are migrated deliberately with the SQL scripts
    // in docs/schema, never automatically.
    await scope.ServiceProvider.GetRequiredService<EProcureDbContext>().Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    await seeder.SeedAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Friendly pages for empty error responses (404, 400 ...); the status code itself is kept.
app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");

app.UseHttpsRedirection();

// ---- Security headers on every response ----
// Everything (scripts, styles, fonts, images) is served from this site, so the browser is told to refuse
// anything else. 'unsafe-inline' for styles only: the layouts write the organisation's brand colours in a
// small <style> block. No inline scripts exist. frame-ancestors 'none' stops the site being framed (clickjacking).
// Development also allows localhost on other ports for Visual Studio's hot reload / Browser Link.
var isDev = app.Environment.IsDevelopment();
var connectSources = isDev ? "'self' ws://localhost:* wss://localhost:* http://localhost:* https://localhost:*" : "'self'";
var scriptSources = isDev ? "'self' http://localhost:* https://localhost:*" : "'self'";
var contentSecurityPolicy = $"default-src 'self'; script-src {scriptSources}; style-src 'self' 'unsafe-inline'; " +
    "img-src 'self' data:; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; " +
    $"frame-ancestors 'none'; connect-src {connectSources}";
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] = contentSecurityPolicy;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    await next();
});

// ---- Culture ----
// South African English, but with "." as the decimal separator for every visitor and every machine.
// Without this, a server set to the South Africa region reads "500.00" wrongly (en-ZA uses a comma),
// and the same form could behave differently on two laptops. Browser language is deliberately ignored.
var southAfrica = (CultureInfo)CultureInfo.GetCultureInfo("en-ZA").Clone();
southAfrica.NumberFormat.NumberDecimalSeparator = ".";
southAfrica.NumberFormat.CurrencyDecimalSeparator = ".";
var localization = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(southAfrica),
    SupportedCultures = new[] { southAfrica },
    SupportedUICultures = new[] { southAfrica }
};
localization.RequestCultureProviders.Clear();
app.UseRequestLocalization(localization);

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication(); // who are you? (reads the Identity cookie)
app.UseAuthorization();  // are you allowed? (roles)

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
