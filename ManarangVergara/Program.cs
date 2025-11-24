using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using ManarangVergara.Services;
using ManarangVergara.Models.Database;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// 1. DATABASE CONFIGURATION
// ============================================================
// retrieves the connection string from appsettings.json ("DefaultConnection")
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// registers entity framework core with mysql support
// ServerVersion.AutoDetect ensures compatibility with your specific mariadb/mysql version
builder.Services.AddDbContext<PharmacyDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// ============================================================
// 2. SERVICE REGISTRATION (dependency injection)
// ============================================================
// adds support for controllers and views (mvc architecture)
builder.Services.AddControllersWithViews();

// registers the email service for dependency injection
// "scoped" means a new instance is created once per client request
// used for sending user invitations and password resets
builder.Services.AddScoped<IEmailService, EmailService>();

// ============================================================
// 3. AUTHENTICATION & SECURITY
// ============================================================
// sets up cookie-based authentication (instead of jwt/tokens)
// this manages the user's login state securely in the browser
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        // where to redirect if a user tries to access a protected page without logging in
        options.LoginPath = "/Account/Login";

        // where to redirect if a user is logged in but doesn't have the right role
        // (e.g., cashier trying to access admin page)
        options.AccessDeniedPath = "/Account/AccessDenied";
    });

// ============================================================
// 4. SESSION MANAGEMENT (cart & temp data)
// ============================================================
// adds memory cache to store session data on the server ram
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    // session expires after 30 minutes of inactivity (standard security practice)
    options.IdleTimeout = TimeSpan.FromMinutes(30);

    // prevents client-side scripts (js) from accessing the cookie (prevents xss attacks)
    options.Cookie.HttpOnly = true;

    // marks the cookie as essential so it works even if the user rejects tracking cookies
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// ============================================================
// 5. HTTP REQUEST PIPELINE (middleware)
// ============================================================

// configure error handling based on environment
if (!app.Environment.IsDevelopment())
{
    // in production: show a user-friendly error page instead of code stack traces
    app.UseExceptionHandler("/Home/Error");
    // enforce strict https security headers (hsts)
    app.UseHsts();
}

// forces http requests to upgrade to https (secure)
app.UseHttpsRedirection();

// enables serving files from wwwroot (css, js, images)
app.UseStaticFiles();

// enables the routing engine to match urls to controllers
app.UseRouting();

// enables session support (must be called before auth)
// this allows the shopping cart to work in the pos
app.UseSession();

// 1. who are you? (check cookies/login)
app.UseAuthentication();

// 2. are you allowed here? (check roles/permissions)
app.UseAuthorization();

// ============================================================
// 6. ROUTING PATTERNS
// ============================================================
// defines the default url pattern: /controller/action/id
// defaults to home controller -> index action if no path is specified
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// start the web application
app.Run();