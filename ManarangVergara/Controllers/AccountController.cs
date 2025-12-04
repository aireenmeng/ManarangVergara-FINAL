using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using ManarangVergara.Services;

namespace ManarangVergara.Controllers
{
    public class AccountController : Controller
    {
        private readonly PharmacyDbContext _context;
        private readonly IEmailService _emailService;

        public AccountController(PharmacyDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // ============================================================
        // 1. LOGIN PAGE
        // ============================================================

        public IActionResult Login()
        {
            if (User.Identity!.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Username == model.Username && e.IsActive == true);

            if (employee == null)
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            bool isPasswordCorrect = false;

            try { isPasswordCorrect = BCrypt.Net.BCrypt.Verify(model.Password, employee.Password); }
            catch { }

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

            // --- REDIRECT LOGIC BASED ON ROLE ---
            if (employee.Position == "Cashier")
            {
                return RedirectToAction("Index", "Pos"); // Cashiers go straight to selling
            }

            return RedirectToAction("Index", "Home"); // Owners/Managers go to Dashboard
        }

        // ============================================================
        // 2. FORGOT PASSWORD (PUBLIC)
        // ============================================================

        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string contactInfo)
        {
            var user = await _context.Employees.FirstOrDefaultAsync(e => e.ContactInfo == contactInfo && e.IsActive == true);

            if (user == null)
            {
                return View("ForgotPasswordConfirmation");
            }

            string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
            user.ResetToken = token;
            user.ResetTokenExpiry = DateTime.UtcNow.AddHours(24);

            await _context.SaveChangesAsync();

            var resetLink = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);

            try
            {
                string emailBody = $@"
                    <h3>Password Reset Request</h3>
                    <p>A password reset was requested for your MedTory account.</p>
                    <p>Click the link below to set a new password:</p>
                    <a href='{resetLink}' style='background-color:#f6c23e;color:black;padding:10px 20px;text-decoration:none;border-radius:5px;'>Reset Password</a>
                    <p><small>If you did not request this, please ignore this email.</small></p>";

                await _emailService.SendEmailAsync(user.ContactInfo, "MedTory - Password Reset", emailBody);
            }
            catch
            {
                // Log error silently
            }

            return View("ForgotPasswordConfirmation");
        }

        // ============================================================
        // 3. LOGOUT
        // ============================================================

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // ============================================================
        // 4. RESET PASSWORD (FROM EMAIL LINK)
        // ============================================================

        public IActionResult ResetPassword(string token)
        {
            var user = _context.Employees.FirstOrDefault(e => e.ResetToken == token && e.ResetTokenExpiry > DateTime.UtcNow);

            if (user == null)
            {
                return Content("Error: This password reset link is invalid or has expired.");
            }

            ViewBag.Token = token;
            ViewBag.Email = user.ContactInfo;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string token, string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                var pendingUser = await _context.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.ResetToken == token);

                ViewBag.Token = token;
                ViewBag.Email = pendingUser?.ContactInfo ?? "Unknown";

                ModelState.AddModelError("", "Passwords do not match. Please try again.");
                return View();
            }

            var user = await _context.Employees
                .FirstOrDefaultAsync(e => e.ResetToken == token && e.ResetTokenExpiry > DateTime.UtcNow);

            if (user == null)
            {
                return RedirectToAction("Login");
            }

            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.ResetToken = null;
            user.ResetTokenExpiry = null;
            user.IsActive = true;

            _context.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Password reset successfully. Please login.";
            return RedirectToAction("Login");
        }
    }
}