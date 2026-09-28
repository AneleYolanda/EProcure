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
    .AddDefaultTokenProviders();
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

app.UseHttpsRedirection();

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
