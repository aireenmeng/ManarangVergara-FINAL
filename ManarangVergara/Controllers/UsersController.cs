using ManarangVergara.Helpers;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using ManarangVergara.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ManarangVergara.Controllers
{
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

        // --- SECURITY HELPER: Enforce Hierarchy ---
        // Returns TRUE if the current user is allowed to modify the target user
        private bool CanModifyUser(string targetRole)
        {
            var myRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // 1. Admins and Owners can touch anyone
            if (myRole == "Admin" || myRole == "Owner") return true;

            // 2. Managers cannot touch Admins or Owners
            if (myRole == "Manager" && (targetRole == "Admin" || targetRole == "Owner")) return false;

            return true;
        }

        // GET: Users List
        public async Task<IActionResult> Index(string sortOrder, int? pageNumber = 1)
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

            // Convert to List first because we are using the raw Entity here
            var data = await users.ToListAsync();

            // PAGINATION: 10 Items
            return View(PaginatedList<ManarangVergara.Models.Database.Employee>.Create(data.AsQueryable(), pageNumber ?? 1, 10));
        }

        // GET: Users/Create - RESTRICTED TO ADMIN/OWNER
        [Authorize(Roles = "Admin,Owner")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Users/Create (INVITE FLOW)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Create(Employee employee)
        {
            // Note: We removed the 'rawPassword' parameter because we generate it now.
            ModelState.Remove("Password");

            if (ModelState.IsValid)
            {
                // --- SECURITY: HIERARCHY CHECK ---
                // Standard: Admin accounts cannot create other Admin accounts. Only the Owner can.
                if (employee.Position == "Admin" && !User.IsInRole("Owner"))
                {
                    ModelState.AddModelError("Position", "Only the Owner can create new Admin accounts.");
                    return View(employee);
                }
                // ---------------------------------

                if (await _context.Employees.AnyAsync(e => e.Username == employee.Username))
                {
                    ModelState.AddModelError("Username", "Username is already taken.");
                    return View(employee);
                }

                // 1. Create a Random Secure Placeholder Password
                // The user doesn't know this, so they MUST use the email link to log in.
                string tempPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
                employee.Password = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                // 2. Generate Invite Token (Reusing the ResetToken logic)
                string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
                employee.ResetToken = token;
                employee.ResetTokenExpiry = DateTime.UtcNow.AddHours(48); // 48 hours to accept invite
                employee.IsActive = true;

                _context.Add(employee);
                await _context.SaveChangesAsync();

                // 3. Send Welcome Email
                var resetLink = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);

                try
                {
                    string emailBody = $@"
                        <h3>Welcome to MedTory!</h3>
                        <p>Hello {employee.EmployeeName},</p>
                        <p>An account has been created for you.</p>
                        <p>Please click the link below to set your secure password and activate your account:</p>
                        <a href='{resetLink}' style='background-color:#4e73df;color:white;padding:10px 20px;text-decoration:none;border-radius:5px;'>Set My Password</a>
                        <p><small>This link expires in 48 hours.</small></p>";

                    await _emailService.SendEmailAsync(employee.ContactInfo, "Welcome to MedTory - Activate Account", emailBody);

                    TempData["SuccessMessage"] = $"Invitation sent to {employee.ContactInfo}!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    // If email fails, we should probably delete the user so they can try again
                    _context.Employees.Remove(employee);
                    await _context.SaveChangesAsync();

                    TempData["ErrorMessage"] = "Could not send invitation email. User was not created. Error: " + ex.Message;
                    return View(employee);
                }
            }
            return View(employee);
        }

        // GET: Edit
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            // HIERARCHY CHECK
            if (!CanModifyUser(employee.Position))
            {
                TempData["ErrorMessage"] = "You do not have permission to edit this user.";
                return RedirectToAction(nameof(Index));
            }

            return View(employee);
        }

        // POST: Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Edit(int id, Employee employee)
        {
            if (id != employee.EmployeeId) return NotFound();

            ModelState.Remove("Password");
            ModelState.Remove("ResetToken");

            if (ModelState.IsValid)
            {
                var existingUser = await _context.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmployeeId == id);
                if (existingUser == null) return NotFound();

                // HIERARCHY CHECK
                if (!CanModifyUser(existingUser.Position))
                {
                    TempData["ErrorMessage"] = "You do not have permission to edit this user.";
                    return RedirectToAction(nameof(Index));
                }

                // Preserve sensitive fields
                employee.Password = existingUser.Password;
                employee.ResetToken = existingUser.ResetToken;
                employee.ResetTokenExpiry = existingUser.ResetTokenExpiry;
                employee.IsActive = existingUser.IsActive;

                _context.Update(employee);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "User details updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            return View(employee);
        }

        // POST: Deactivate
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var user = await _context.Employees.FindAsync(id);
            if (user == null) return NotFound();

            // HIERARCHY CHECK
            if (!CanModifyUser(user.Position))
            {
                TempData["ErrorMessage"] = "You do not have permission to deactivate this user.";
                return RedirectToAction(nameof(Index));
            }

            // Toggle Active Status
            user.IsActive = !user.IsActive;
            _context.Update(user);
            await _context.SaveChangesAsync();

            string status = user.IsActive ? "Re-activated" : "Deactivated";
            TempData["SuccessMessage"] = $"User {user.EmployeeName} has been {status}.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Send Reset Link (Managers CAN do this, subject to Hierarchy Check)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendResetLink(int id)
        {
            var user = await _context.Employees.FindAsync(id);
            if (user == null) return NotFound();

            // HIERARCHY CHECK
            if (!CanModifyUser(user.Position))
            {
                TempData["ErrorMessage"] = "You do not have permission to reset this user's password.";
                return RedirectToAction(nameof(Index));
            }

            // Email Validation
            if (!user.ContactInfo.Contains("@") || !user.ContactInfo.Contains("."))
            {
                TempData["ErrorMessage"] = $"Cannot send email to '{user.ContactInfo}'. It looks like a phone number.";
                return RedirectToAction(nameof(Index));
            }

            // Generate Token
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
            user.ResetToken = token;
            user.ResetTokenExpiry = DateTime.UtcNow.AddHours(24);

            _context.Update(user);
            await _context.SaveChangesAsync();

            var resetLink = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);

            try
            {
                string emailBody = $@"<h3>Password Reset</h3><p>Click here: <a href='{resetLink}'>Reset My Password</a></p>";
                await _emailService.SendEmailAsync(user.ContactInfo, "MedTory - Password Reset", emailBody);
                TempData["SuccessMessage"] = $"Reset link sent to {user.ContactInfo}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Email Config Error: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}