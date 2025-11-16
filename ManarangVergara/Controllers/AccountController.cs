using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;

namespace ManarangVergara.Controllers
{
    public class AccountController : Controller
    {
        private readonly PharmacyDbContext _context;

        public AccountController(PharmacyDbContext context)
        {
            _context = context;
        }

        // GET: /Account/Login
        public IActionResult Login()
        {
            // If already logged in, redirect to Home
            if (User.Identity!.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            // 1. Find user by Username ONLY first
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Username == model.Username);

            if (employee == null)
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            // 2. Verify Password
            bool isPasswordCorrect = false;
            // A. Try BCrypt verification first (Standard secure way)
            try { isPasswordCorrect = BCrypt.Net.BCrypt.Verify(model.Password, employee.Password); }
            catch { /* Ignore invalid hash format errors if any */ }

            // B. FALLBACK: If BCrypt failed, check plain text (for your old dummy data)
            // Once you have updated all users, you can REMOVE this fallback for max security.
            if (!isPasswordCorrect && employee.Password == model.Password)
            {
                isPasswordCorrect = true;
                // Optional: Auto-update them to secure hash now that we know who they are
                employee.Password = BCrypt.Net.BCrypt.HashPassword(model.Password);
                await _context.SaveChangesAsync();
            }

            if (!isPasswordCorrect)
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            // 2. Create User Claims (The info stored in their login cookie)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, employee.Username),
                new Claim("FullName", employee.EmployeeName),
                new Claim(ClaimTypes.Role, employee.Position),
                new Claim("EmployeeId", employee.EmployeeId.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            // 3. Sign In (Creates the cookie)
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));

            return RedirectToAction("Index", "Home");
        }

        // GET: /Account/Logout
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}