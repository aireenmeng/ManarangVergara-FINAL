using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Services;
using ManarangVergara.Models.Database;
using System.Security.Claims;
using ManarangVergara.Helpers;

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

        // ============================================================
        // 1. ACTIVE USERS LIST (Modified)
        // ============================================================
        public async Task<IActionResult> Index(string sortOrder, int? pageNumber = 1)
        {
            ViewData["CurrentSort"] = sortOrder;
            ViewData["UserSortParm"] = String.IsNullOrEmpty(sortOrder) ? "user_desc" : "";
            ViewData["NameSortParm"] = sortOrder == "Name" ? "name_desc" : "Name";
            ViewData["RoleSortParm"] = sortOrder == "Role" ? "role_desc" : "Role";
            ViewData["ContactSortParm"] = sortOrder == "Contact" ? "contact_desc" : "Contact";

            // FILTER: Show ONLY Active Users who are fully registered
            var users = _context.Employees
                .Where(e => e.ResetToken == null && e.IsActive == true) // <--- CHANGED
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
            return View(PaginatedList<Employee>.Create(data.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ============================================================
        // 2. DEACTIVATED USERS (New Archive Page)
        // ============================================================
        public async Task<IActionResult> Deactivated(string sortOrder, int? pageNumber = 1)
        {
            ViewData["CurrentSort"] = sortOrder;

            // FILTER: Show ONLY Deactivated Users who are fully registered
            var users = _context.Employees
                .Include(e => e.Transactions) // Include history to check for delete safety
                .Include(e => e.ItemLogs)
                .Include(e => e.Voids)
                .Where(e => e.ResetToken == null && e.IsActive == false)
                .AsQueryable();

            // Default Sort: Recently Deactivated (or just ID order)
            users = users.OrderByDescending(e => e.EmployeeId);

            var data = await users.ToListAsync();
            return View(PaginatedList<Employee>.Create(data.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ============================================================
        // 3. PENDING INVITATIONS
        // ============================================================
        public async Task<IActionResult> Pending(int? pageNumber = 1)
        {
            var query = _context.Employees
                .Where(e => e.ResetToken != null)
                .OrderByDescending(e => e.ResetTokenExpiry);

            var list = await query.ToListAsync();
            return View(PaginatedList<Employee>.Create(list.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ============================================================
        // 4. CREATE / EDIT / DEACTIVATE ACTIONS
        // ============================================================

        [Authorize(Roles = "Admin,Owner")]
        public IActionResult Create() { return View(); }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Create(Employee employee)
        {
            ModelState.Remove("Password");

            if (!string.IsNullOrEmpty(employee.Username) && employee.Username.Contains(" "))
                ModelState.AddModelError("Username", "Username cannot contain spaces.");

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

                if (await _context.Employees.AnyAsync(e => e.ContactInfo == employee.ContactInfo))
                {
                    ModelState.AddModelError("ContactInfo", "This email address is already in use.");
                    return View(employee);
                }

                string tempPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
                employee.Password = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
                employee.ResetToken = token;
                employee.ResetTokenExpiry = DateTime.UtcNow.AddHours(48);
                employee.IsActive = false; // Set false so they appear in Pending, not Active

                _context.Add(employee);
                await _context.SaveChangesAsync();

                var resetLink = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);

                try
                {
                    string emailBody = $@"<h3>Welcome to MedTory!</h3><p>Hello {employee.EmployeeName},</p><p>Please click the link below to set your password:</p><a href='{resetLink}'>Set My Password</a>";
                    await _emailService.SendEmailAsync(employee.ContactInfo, "Welcome to MedTory - Activate Account", emailBody);
                    TempData["SuccessMessage"] = $"Invitation sent to {employee.ContactInfo}!";
                    return RedirectToAction(nameof(Pending));
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

        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            if (!CanModifyUser(employee.Position))
            {
                TempData["ErrorMessage"] = "You do not have permission to edit this user.";
                return RedirectToAction(nameof(Index));
            }
            return View(employee);
        }

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

        // ACTION: Deactivate (Moves from Index -> Deactivated)
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

            user.IsActive = false; // Deactivate
            _context.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"User {user.EmployeeName} deactivated and moved to archives.";
            return RedirectToAction(nameof(Index));
        }

        // ACTION: Restore (Moves from Deactivated -> Index)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Restore(int id)
        {
            var user = await _context.Employees.FindAsync(id);
            if (user == null) return NotFound();

            user.IsActive = true; // Reactivate
            _context.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"User {user.EmployeeName} restored to active list.";
            return RedirectToAction(nameof(Deactivated));
        }

        // ACTION: Permanent Delete (From Deactivated List)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> DeletePermanent(int id)
        {
            var user = await _context.Employees
                .Include(u => u.Transactions)
                .Include(u => u.ItemLogs)
                .Include(u => u.Voids)
                .FirstOrDefaultAsync(u => u.EmployeeId == id);

            if (user == null) return NotFound();

            // Safety Check: Do they have history?
            bool hasHistory = user.Transactions.Any() || user.ItemLogs.Any() || user.Voids.Any();

            if (hasHistory)
            {
                TempData["ErrorMessage"] = "Cannot delete: This user has transaction or audit history.";
                return RedirectToAction(nameof(Deactivated));
            }

            _context.Employees.Remove(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "User record permanently deleted.";
            return RedirectToAction(nameof(Deactivated));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelInvite(int id)
        {
            var user = await _context.Employees.FirstOrDefaultAsync(u => u.EmployeeId == id);
            if (user == null) return NotFound();

            bool isNewInvite = !user.IsActive;

            if (isNewInvite)
            {
                _context.Employees.Remove(user);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Invitation revoked. User deleted.";
            }
            else
            {
                user.ResetToken = null;
                user.ResetTokenExpiry = null;
                _context.Update(user);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Reset cancelled.";
            }
            return RedirectToAction(nameof(Pending));
        }

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

        private bool CanModifyUser(string targetRole)
        {
            var myRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (myRole == "Owner") return true;
            if (myRole == "Admin" && targetRole != "Admin" && targetRole != "Owner") return true;
            if (myRole == "Manager" && targetRole == "Cashier") return true;
            return false;
        }
    }
}