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

        // ============================================================
        // 1. LOGIN PAGE
        // ============================================================

        // get: /account/login
        // shows the login form. redirects to dashboard if user is already logged in.
        public IActionResult Login()
        {
            if (User.Identity!.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // post: /account/login
        // handles the actual login logic, validating credentials against the database.
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            // check if the form data is valid (e.g., username/password not empty)
            if (!ModelState.IsValid) return View(model);

            // new line (add the isactive check):
            // searches for the user. crucially, it also checks if isactive == true.
            // if a user is deactivated (fired/resigned), this query returns null, blocking access.
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Username == model.Username && e.IsActive == true);

            // if they are found but isactive is false, this returns null, 
            // and the existing "invalid username or password" error will show. perfect security.
            if (employee == null)
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            bool isPasswordCorrect = false;

            // 1. try secure hash
            // attempts to verify the input password against the bcrypt hash in the database.
            try { isPasswordCorrect = BCrypt.Net.BCrypt.Verify(model.Password, employee.Password); }
            catch { }

            // 2. fallback: plain text (auto-updates to hash)
            // this handles legacy accounts that might still have plain text passwords.
            // if the plain text matches, we immediately hash it for future security.
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

            // create user claims (identity card)
            // these details are stored in the cookie so we don't have to query the db on every page load.
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, employee.Username),
                new Claim("FullName", employee.EmployeeName),
                new Claim(ClaimTypes.Role, employee.Position), // used for [authorize(roles="...")]
                new Claim("EmployeeId", employee.EmployeeId.ToString()) // used for tracking sales/logs
            };

            // create the secure login session (cookie)
            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

            return RedirectToAction("Index", "Home");
        }

        // ============================================================
        // 2. LOGOUT
        // ============================================================

        // logs the user out by clearing their authentication cookie.
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // ============================================================
        // 3. PASSWORD RESET (EMAIL FLOW)
        // ============================================================

        // --- new: reset password (clicked from email) ---
        // verifies the token from the email link. if valid, shows the "set new password" form.
        public IActionResult ResetPassword(string token)
        {
            // security check: token must match db and must not be expired.
            var user = _context.Employees.FirstOrDefault(e => e.ResetToken == token && e.ResetTokenExpiry > DateTime.UtcNow);

            if (user == null)
            {
                return Content("Error: This password reset link is invalid or has expired.");
            }

            // pass the token to the view so it can be sent back in the post request.
            ViewBag.Token = token;
            return View();
        }

        // handles the submission of the new password.
        [HttpPost]
        public async Task<IActionResult> ResetPassword(string token, string newPassword)
        {
            // re-verify token to prevent tampering
            var user = await _context.Employees.FirstOrDefaultAsync(e => e.ResetToken == token && e.ResetTokenExpiry > DateTime.UtcNow);
            if (user == null) return RedirectToAction("Login");

            // hash the new password securely
            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);

            // invalidate the token so it cannot be used again (security best practice)
            user.ResetToken = null;
            user.ResetTokenExpiry = null;

            _context.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Password reset successfully. Please login.";
            return RedirectToAction("Login");
        }
    }
}