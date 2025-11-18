using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Services;
using ManarangVergara.Models.Database;

namespace ManarangVergara.Controllers
{
    // ALLOW Managers to access the controller generally
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class UsersController : Controller
    {
        private readonly PharmacyDbContext _context;
        private readonly IEmailService _emailService;

        public UsersController(PharmacyDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // GET: Users List
        public async Task<IActionResult> Index(string sortOrder)
        {
            ViewData["CurrentSort"] = sortOrder;
            ViewData["UserSortParm"] = String.IsNullOrEmpty(sortOrder) ? "user_desc" : "";
            ViewData["NameSortParm"] = sortOrder == "Name" ? "name_desc" : "Name";
            ViewData["RoleSortParm"] = sortOrder == "Role" ? "role_desc" : "Role";
            ViewData["ContactSortParm"] = sortOrder == "Contact" ? "contact_desc" : "Contact";

            var users = from e in _context.Employees select e;

            users = sortOrder switch
            {
                "user_desc" => users.OrderByDescending(e => e.Username),
                "Name" => users.OrderBy(e => e.EmployeeName),
                "name_desc" => users.OrderByDescending(e => e.EmployeeName),
                "Role" => users.OrderBy(e => e.Position),
                "role_desc" => users.OrderByDescending(e => e.Position),
                "Contact" => users.OrderBy(e => e.ContactInfo),
                "contact_desc" => users.OrderByDescending(e => e.ContactInfo),
                _ => users.OrderBy(e => e.Username),
            };

            return View(await users.ToListAsync());
        }

        // GET: Users/Create - RESTRICTED TO ADMIN/OWNER
        [Authorize(Roles = "Admin,Owner")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Users/Create - RESTRICTED TO ADMIN/OWNER
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Create(Employee employee, string rawPassword)
        {
            if (string.IsNullOrEmpty(rawPassword) || rawPassword.Length < 6)
            {
                ModelState.AddModelError("rawPassword", "Password must be at least 6 characters.");
            }
            ModelState.Remove("Password");

            if (ModelState.IsValid)
            {
                if (await _context.Employees.AnyAsync(e => e.Username == employee.Username))
                {
                    ModelState.AddModelError("Username", "Username is already taken.");
                    return View(employee);
                }

                employee.Password = BCrypt.Net.BCrypt.HashPassword(rawPassword);

                _context.Add(employee);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"User {employee.Username} created successfully!";
                return RedirectToAction(nameof(Index));
            }
            return View(employee);
        }

        // ... existing Create and SendResetLink methods ...

        // GET: Users/Edit/5
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();
            return View(employee);
        }

        // POST: Users/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Edit(int id, Employee employee)
        {
            if (id != employee.EmployeeId) return NotFound();

            // Remove Password validation because we are NOT editing the password here
            ModelState.Remove("Password");
            ModelState.Remove("ResetToken");

            if (ModelState.IsValid)
            {
                try
                {
                    // 1. Get the EXISTING user from DB to preserve their Password
                    var existingUser = await _context.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmployeeId == id);
                    if (existingUser == null) return NotFound();

                    // 2. Keep the old password and token
                    employee.Password = existingUser.Password;
                    employee.ResetToken = existingUser.ResetToken;
                    employee.ResetTokenExpiry = existingUser.ResetTokenExpiry;

                    // 3. Keep them Active (unless they were already inactive)
                    employee.IsActive = existingUser.IsActive;

                    _context.Update(employee);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "User details updated successfully.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Employees.AnyAsync(e => e.EmployeeId == employee.EmployeeId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(employee);
        }

        // POST: Deactivate (Resigned)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var user = await _context.Employees.FindAsync(id);
            if (user == null) return NotFound();

            // Toggle status: If Active -> Deactivate. If Inactive -> Reactivate.
            user.IsActive = !user.IsActive;

            _context.Update(user);
            await _context.SaveChangesAsync();

            string status = user.IsActive ? "Re-activated" : "Deactivated";
            TempData["SuccessMessage"] = $"User {user.EmployeeName} has been {status}.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Users/SendResetLink
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendResetLink(int id)
        {
            var user = await _context.Employees.FindAsync(id);
            if (user == null) return NotFound();

            // SAFETY CHECK: Is this actually an email address?
            if (!user.ContactInfo.Contains("@") || !user.ContactInfo.Contains("."))
            {
                TempData["ErrorMessage"] = $"Cannot send email to '{user.ContactInfo}'. It looks like a phone number.";
                return RedirectToAction(nameof(Index));
            }

            // 1. Generate Token
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
            user.ResetToken = token;
            user.ResetTokenExpiry = DateTime.UtcNow.AddHours(24);

            _context.Update(user);
            await _context.SaveChangesAsync();

            // 2. Create Link
            var resetLink = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);

            // 3. Send Email
            try
            {
                string emailBody = $@"
            <h3>Password Reset Request</h3>
            <p>Hello {user.EmployeeName},</p>
            <p>Your password reset link is below:</p>
            <a href='{resetLink}'>Reset My Password</a>";

                await _emailService.SendEmailAsync(user.ContactInfo, "MedTory - Password Reset", emailBody);
                TempData["SuccessMessage"] = $"Reset link sent to {user.ContactInfo}";
            }
            catch (Exception ex)
            {
                // This catches configuration errors (like missing appsettings)
                TempData["ErrorMessage"] = "Email Config Error: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}