using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using ManarangVergara.Services; // required for sending emails

namespace ManarangVergara.Controllers
{
    // manages user accounts, logging in, and resetting passwords
    public class AccountController : Controller
    {
        private readonly PharmacyDbContext _context;
        private readonly IEmailService _emailService; // added email service

        // loads the tools we need: the database connection and the email sender
        public AccountController(PharmacyDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // ============================================================
        // 1. LOGIN PAGE
        // ============================================================

        // get: /account/login
        // shows the login screen. if you are already logged in, it kicks you to the homepage instead.
        public IActionResult Login()
        {
            if (User.Identity!.IsAuthenticated)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // post: /account/login
        // MAIN FUNCTION: PROCESS THE LOGIN ATTEMPT
        // this checks if the username and password match what is in the database
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            // find the user in the database. also makes sure they aren't "fired" (inactive).
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Username == model.Username && e.IsActive == true);

            if (employee == null)
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            bool isPasswordCorrect = false;

            // 1. try secure hash
            // attempts to match the password using modern encryption (bcrypt)
            try { isPasswordCorrect = BCrypt.Net.BCrypt.Verify(model.Password, employee.Password); }
            catch { }

            // 2. fallback: plain text (auto-updates to hash)
            // if the old password was not encrypted yet, this checks it, then automatically encrypts it for next time
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

            // create the user's "digital id card" (claims)
            // this data stays with the user while they browse the site so we know who they are
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, employee.Username),
                new Claim("FullName", employee.EmployeeName),
                new Claim(ClaimTypes.Role, employee.Position), // saves if they are admin, cashier, etc.
                new Claim("EmployeeId", employee.EmployeeId.ToString())
            };

            // actually logs them into the browser using cookies
            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

            return RedirectToAction("Index", "Home");
        }

        // ============================================================
        // 2. FORGOT PASSWORD (PUBLIC)
        // ============================================================

        // get: /account/forgotpassword
        // shows the simple form asking for an email address
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // post: /account/forgotpassword
        // MAIN FUNCTION: GENERATE RESET LINK AND SEND EMAIL
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string contactInfo)
        {
            var user = await _context.Employees.FirstOrDefaultAsync(e => e.ContactInfo == contactInfo && e.IsActive == true);

            // security feature:
            // even if we didn't find the email, we pretend we did. 
            // this stops hackers from guessing emails to see who has an account here.
            if (user == null)
            {
                return View("ForgotPasswordConfirmation");
            }

            // generate a random secret code (token)
            string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
            user.ResetToken = token;
            user.ResetTokenExpiry = DateTime.UtcNow.AddHours(24); // the link dies in 24 hours

            // save the token to the database so we can check it later
            await _context.SaveChangesAsync();

            // creates the clickable link
            var resetLink = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);

            try
            {
                // formatting the email message with html
                string emailBody = $@"
                    <h3>Password Reset Request</h3>
                    <p>A password reset was requested for your MedTory account.</p>
                    <p>Click the link below to set a new password:</p>
                    <a href='{resetLink}' style='background-color:#f6c23e;color:black;padding:10px 20px;text-decoration:none;border-radius:5px;'>Reset Password</a>
                    <p><small>If you did not request this, please ignore this email.</small></p>";

                // uses the email service to actually send it out
                await _emailService.SendEmailAsync(user.ContactInfo, "MedTory - Password Reset", emailBody);
            }
            catch
            {
                // if sending fails, we just ignore it for now so the app doesn't crash
            }

            return View("ForgotPasswordConfirmation");
        }

        // ============================================================
        // 3. LOGOUT
        // ============================================================

        // deletes the login cookie and sends user back to login page
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // ============================================================
        // 4. RESET PASSWORD (FROM EMAIL LINK)
        // ============================================================

        // get: verifies the token from the email link.
        // this runs when the user clicks the link in their email
        public IActionResult ResetPassword(string token)
        {
            // check if the token exists and hasn't expired yet
            var user = _context.Employees.FirstOrDefault(e => e.ResetToken == token && e.ResetTokenExpiry > DateTime.UtcNow);

            if (user == null)
            {
                return Content("Error: This password reset link is invalid or has expired.");
            }

            // pass the token to the page so we can send it back with the new password
            ViewBag.Token = token;
            ViewBag.Email = user.ContactInfo;
            return View();
        }

        // post: saves the new password.
        // MAIN FUNCTION: SAVE THE NEW PASSWORD
        [HttpPost]
        public async Task<IActionResult> ResetPassword(string token, string newPassword)
        {
            // double check the token is still valid
            var user = await _context.Employees.FirstOrDefaultAsync(e => e.ResetToken == token && e.ResetTokenExpiry > DateTime.UtcNow);
            if (user == null) return RedirectToAction("Login");

            // scramble (encrypt) the new password before saving it
            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);

            // clear the token so this link cannot be used a second time
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