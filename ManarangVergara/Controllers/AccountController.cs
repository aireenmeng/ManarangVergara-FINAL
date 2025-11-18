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

            // NEW LINE (Add the IsActive check):
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Username == model.Username && e.IsActive == true);

            // If they are found but IsActive is false, this returns null, 
            // and the existing "Invalid username or password" error will show. Perfect security.

            if (employee == null)
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            bool isPasswordCorrect = false;
            // 1. Try Secure Hash
            try { isPasswordCorrect = BCrypt.Net.BCrypt.Verify(model.Password, employee.Password); }
            catch { }

            // 2. Fallback: Plain text (auto-updates to hash)
            if (!isPasswordCorrect && employee.Password == model.Password)
            {
                isPasswordCorrect = true;
                employee.Password = BCrypt.Net.BCrypt.HashPassword(model.Password);
                await _context.SaveChangesAsync();
            }

            if (!isPasswordCorrect)
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, employee.Username),
                new Claim("FullName", employee.EmployeeName),
                new Claim(ClaimTypes.Role, employee.Position),
                new Claim("EmployeeId", employee.EmployeeId.ToString())
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

            return RedirectToAction("Index", "Home");
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // --- NEW: Reset Password (Clicked from Email) ---
        public IActionResult ResetPassword(string token)
        {
            var user = _context.Employees.FirstOrDefault(e => e.ResetToken == token && e.ResetTokenExpiry > DateTime.UtcNow);
            if (user == null)
            {
                return Content("Error: This password reset link is invalid or has expired.");
            }
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string token, string newPassword)
        {
            var user = await _context.Employees.FirstOrDefaultAsync(e => e.ResetToken == token && e.ResetTokenExpiry > DateTime.UtcNow);
            if (user == null) return RedirectToAction("Login");

            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.ResetToken = null;
            user.ResetTokenExpiry = null;

            _context.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Password reset successfully. Please login.";
            return RedirectToAction("Login");
        }
    }
}