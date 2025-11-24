using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Services;
using ManarangVergara.Models.Database;
using System.Security.Claims;
using ManarangVergara.Helpers; // Required for PaginatedList

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

        // GET: Users List (Active Users Only)
        public async Task<IActionResult> Index(string sortOrder, int? pageNumber = 1)
        {
            ViewData["CurrentSort"] = sortOrder;
            ViewData["UserSortParm"] = String.IsNullOrEmpty(sortOrder) ? "user_desc" : "";
            ViewData["NameSortParm"] = sortOrder == "Name" ? "name_desc" : "Name";
            ViewData["RoleSortParm"] = sortOrder == "Role" ? "role_desc" : "Role";
            ViewData["ContactSortParm"] = sortOrder == "Contact" ? "contact_desc" : "Contact";

            // Filter: Only show users who have successfully registered (ResetToken is NULL)
            // OR users who are legacy (Active = true)
            var users = _context.Employees
                .Where(e => e.ResetToken == null)
                .AsQueryable();

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

            var data = await users.ToListAsync();

            // Pagination: 10 Items
            int pageSize = 10;
            return View(PaginatedList<Employee>.Create(data.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // GET: Pending Invites List
        public async Task<IActionResult> Pending(int? pageNumber = 1)
        {
            // Logic: Users who have a ResetToken are "Pending"
            var query = _context.Employees
                .Where(e => e.ResetToken != null)
                .OrderByDescending(e => e.ResetTokenExpiry);

            var list = await query.ToListAsync();

            return View(PaginatedList<Employee>.Create(list.AsQueryable(), pageNumber ?? 1, 10));
        }

        // POST: Cancel Invite
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> CancelInvite(int id)
        {
            var user = await _context.Employees.FindAsync(id);
            if (user == null) return NotFound();

            if (string.IsNullOrEmpty(user.ResetToken))
            {
                TempData["ErrorMessage"] = "Cannot cancel. This user is already active.";
                return RedirectToAction(nameof(Index));
            }

            _context.Employees.Remove(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Invitation cancelled and user removed.";
            return RedirectToAction(nameof(Pending));
        }

        // GET: Create (Invite)
        [Authorize(Roles = "Admin,Owner")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Create (Invite Flow)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Create(Employee employee)
        {
            ModelState.Remove("Password");

            // 1. VALIDATION: No Spaces in Username
            if (employee.Username.Contains(" "))
            {
                ModelState.AddModelError("Username", "Username cannot contain spaces.");
            }

            if (ModelState.IsValid)
            {
                bool iAmOwner = User.IsInRole("Owner");

                if ((employee.Position == "Owner" || employee.Position == "Admin") && !iAmOwner)
                {
                    ModelState.AddModelError("Position", "Only the System Owner can create Admin or Owner accounts.");
                    return View(employee);
                }

                if (await _context.Employees.AnyAsync(e => e.Username == employee.Username))
                {
                    ModelState.AddModelError("Username", "Username is already taken.");
                    return View(employee);
                }

                // 3. Generate Temp Password & Token
                string tempPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
                employee.Password = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
                employee.ResetToken = token;
                employee.ResetTokenExpiry = DateTime.UtcNow.AddHours(48);
                employee.IsActive = true;

                _context.Add(employee);
                await _context.SaveChangesAsync();

                // 4. Send Email
                var resetLink = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);

                try
                {
                    string emailBody = $@"
                        <h3>Welcome to MedTory!</h3>
                        <p>Hello {employee.EmployeeName},</p>
                        <p>An account has been created for you.</p>
                        <p>Please click the link below to set your password and activate your account:</p>
                        <a href='{resetLink}' style='background-color:#4e73df;color:white;padding:10px 20px;text-decoration:none;border-radius:5px;'>Set My Password</a>
                        <p><small>This link expires in 48 hours.</small></p>";

                    await _emailService.SendEmailAsync(employee.ContactInfo, "Welcome to MedTory - Activate Account", emailBody);

                    TempData["SuccessMessage"] = $"Invitation sent to {employee.ContactInfo}!";
                    return RedirectToAction(nameof(Pending)); // Redirect to Pending list
                }
                catch (Exception ex)
                {
                    _context.Employees.Remove(employee);
                    await _context.SaveChangesAsync();
                    TempData["ErrorMessage"] = "Email failed. User not created. Error: " + ex.Message;
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

            // Use logic helper to prevent Manager from editing Admin/Manager
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

                if (!CanModifyUser(existingUser.Position))
                {
                    TempData["ErrorMessage"] = "You do not have permission to edit this user.";
                    return RedirectToAction(nameof(Index));
                }

                employee.Password = existingUser.Password;
                employee.ResetToken = existingUser.ResetToken;
                employee.ResetTokenExpiry = existingUser.ResetTokenExpiry;
                employee.IsActive = existingUser.IsActive;

                _context.Update(employee);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "User updated successfully.";
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

            if (!CanModifyUser(user.Position))
            {
                TempData["ErrorMessage"] = "Permission denied.";
                return RedirectToAction(nameof(Index));
            }

            user.IsActive = !user.IsActive;
            _context.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = user.IsActive ? "User Reactivated." : "User Deactivated.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Send Reset Link (Managers CAN do this, subject to Hierarchy)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendResetLink(int id)
        {
            var user = await _context.Employees.FindAsync(id);
            if (user == null) return NotFound();

            if (!CanModifyUser(user.Position))
            {
                TempData["ErrorMessage"] = "Permission denied.";
                return RedirectToAction(nameof(Index));
            }

            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
            user.ResetToken = token;
            user.ResetTokenExpiry = DateTime.UtcNow.AddHours(24);

            _context.Update(user);
            await _context.SaveChangesAsync();

            var resetLink = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);

            // Send Email Logic (Simplified)
            try
            {
                await _emailService.SendEmailAsync(user.ContactInfo, "Reset Password", $"<a href='{resetLink}'>Reset Here</a>");
                TempData["SuccessMessage"] = "Reset link sent.";
            }
            catch
            {
                TempData["ErrorMessage"] = "Failed to send email.";
            }

            return RedirectToAction(nameof(Index));
        }

        // --- SECURITY HELPER ---
        private bool CanModifyUser(string targetRole)
        {
            var myRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (myRole == "Owner") return true; // Owner can do anything
            if (myRole == "Admin" && targetRole != "Admin" && targetRole != "Owner") return true; // Admin can touch Manager/Cashier
            if (myRole == "Manager" && targetRole == "Cashier") return true; // Manager can ONLY touch Cashier

            return false; // Everyone else blocked
        }
    }
}