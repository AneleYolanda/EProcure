using EProcure.Web.Data;
using EProcure.Web.Domain;
using EProcure.Web.Infrastructure;
using EProcure.Web.Services;
using EProcure.Web.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
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
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ICurrentOrganisation, CurrentOrganisation>();
builder.Services.AddScoped<DemoDataSeeder>();

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
