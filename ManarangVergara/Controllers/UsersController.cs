using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Services;
using ManarangVergara.Models.Database;
using System.Security.Claims;
using ManarangVergara.Helpers;

namespace ManarangVergara.Controllers
{
    // security: only bosses (admins, owners, managers) can enter this entire section
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class UsersController : Controller
    {
        private readonly PharmacyDbContext _context;
        private readonly IEmailService _emailService;

        // connects the database and email sender tool
        public UsersController(PharmacyDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // ============================================================
        // 1. ACTIVE USERS LIST (Modified)
        // ============================================================

        // MAIN FUNCTION: SHOWS THE LIST OF ACTIVE EMPLOYEES
        // displays the table of people who are currently working and have accepted their invites
        public async Task<IActionResult> Index(string sortOrder, int? pageNumber = 1)
        {
            // handles the arrows on the table headers (sort by name, role, etc.)
            ViewData["CurrentSort"] = sortOrder;
            ViewData["UserSortParm"] = String.IsNullOrEmpty(sortOrder) ? "user_desc" : "";
            ViewData["NameSortParm"] = sortOrder == "Name" ? "name_desc" : "Name";
            ViewData["RoleSortParm"] = sortOrder == "Role" ? "role_desc" : "Role";
            ViewData["ContactSortParm"] = sortOrder == "Contact" ? "contact_desc" : "Contact";

            // FILTER: Show ONLY Active Users who are fully registered
            // "resettoken == null" means they already clicked the link in their email and set a password
            var users = _context.Employees
                .Where(e => e.ResetToken == null && e.IsActive == true) // <--- CHANGED
                .AsQueryable();

            // sorts the list based on what header you clicked
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

            // pagination: limits list to 10 people per page
            var data = await users.ToListAsync();
            return View(PaginatedList<Employee>.Create(data.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ============================================================
        // 2. DEACTIVATED USERS (New Archive Page)
        // ============================================================

        // FUNCTION: SHOWS THE 'TRASH CAN' OR ARCHIVED USERS
        // these are employees who were fired or quit, but we keep their records for history
        public async Task<IActionResult> Deactivated(string sortOrder, int? pageNumber = 1)
        {
            ViewData["CurrentSort"] = sortOrder;

            // FILTER: Show ONLY Deactivated Users
            // we load their transactions and logs so we can see what they did before leaving
            var users = _context.Employees
                .Include(e => e.Transactions) // include history to check for delete safety
                .Include(e => e.ItemLogs)
                .Include(e => e.Voids)
                .Where(e => e.ResetToken == null && e.IsActive == false)
                .AsQueryable();

            // default sort: show the most recently hired people first
            users = users.OrderByDescending(e => e.EmployeeId);

            var data = await users.ToListAsync();
            return View(PaginatedList<Employee>.Create(data.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ============================================================
        // 3. PENDING INVITATIONS
        // ============================================================

        // FUNCTION: SHOWS EMAILS SENT BUT NOT YET ACCEPTED
        // if you invited a new cashier but they haven't set their password yet, they appear here
        public async Task<IActionResult> Pending(int? pageNumber = 1)
        {
            var query = _context.Employees
                .Where(e => e.ResetToken != null) // token exists means they haven't finished setup
                .OrderByDescending(e => e.ResetTokenExpiry);

            var list = await query.ToListAsync();
            return View(PaginatedList<Employee>.Create(list.AsQueryable(), pageNumber ?? 1, 10));
        }

        // ============================================================
        // 4. CREATE / EDIT / DEACTIVATE ACTIONS
        // ============================================================

        // shows the "add employee" form
        [Authorize(Roles = "Admin,Owner")]
        public IActionResult Create() { return View(); }

        // FUNCTION: SAVE NEW EMPLOYEE & SEND EMAIL INVITE
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")] // managers cannot hire people, only admins/owners
        public async Task<IActionResult> Create(Employee employee)
        {
            ModelState.Remove("Password"); // we don't set a password here; the user sets it via email

            if (!string.IsNullOrEmpty(employee.Username) && employee.Username.Contains(" "))
                ModelState.AddModelError("Username", "Username cannot contain spaces.");

            if (ModelState.IsValid)
            {
                // hierarchy protection: an admin cannot create an owner account.
                bool iAmOwner = User.IsInRole("Owner");
                if ((employee.Position == "Owner" || employee.Position == "Admin") && !iAmOwner)
                {
                    ModelState.AddModelError("Position", "Only the System Owner can create Admin or Owner accounts.");
                    return View(employee);
                }

                // check if username or email is already taken
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

                // create a temporary hidden password just to satisfy database rules
                string tempPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
                employee.Password = BCrypt.Net.BCrypt.HashPassword(tempPassword);

                // create the secure invitation token
                string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
                employee.ResetToken = token;
                employee.ResetTokenExpiry = DateTime.UtcNow.AddHours(48); // link expires in 2 days
                employee.IsActive = false; // set false so they appear in pending, not active list

                _context.Add(employee);
                await _context.SaveChangesAsync();

                // generate the email link
                var resetLink = Url.Action("ResetPassword", "Account", new { token = token }, Request.Scheme);

                // try to send the email
                try
                {
                    string emailBody = $@"<h3>Welcome to MedTory!</h3><p>Hello {employee.EmployeeName},</p><p>Please click the link below to set your password:</p><a href='{resetLink}'>Set My Password</a>";
                    await _emailService.SendEmailAsync(employee.ContactInfo, "Welcome to MedTory - Activate Account", emailBody);
                    TempData["SuccessMessage"] = $"Invitation sent to {employee.ContactInfo}!";
                    return RedirectToAction(nameof(Pending));
                }
                catch (Exception ex)
                {
                    // if email fails, delete the user we just created so we can try again
                    _context.Employees.Remove(employee);
                    await _context.SaveChangesAsync();
                    TempData["ErrorMessage"] = "Email failed. User not created. Error: " + ex.Message;
                    return View(employee);
                }
            }
            return View(employee);
        }

        // FUNCTION: SHOW EDIT FORM
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null) return NotFound();

            // check if i am allowed to edit this person (e.g., admin cannot edit owner)
            if (!CanModifyUser(employee.Position))
            {
                TempData["ErrorMessage"] = "You do not have permission to edit this user.";
                return RedirectToAction(nameof(Index));
            }
            return View(employee);
        }

        // FUNCTION: SAVE EDITS
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

                // double check permissions before saving
                if (!CanModifyUser(existingUser.Position))
                {
                    TempData["ErrorMessage"] = "You do not have permission to edit this user.";
                    return RedirectToAction(nameof(Index));
                }

                // protect sensitive fields: ensure password and token don't get erased during edit
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

        // FUNCTION: DEACTIVATE ("FIRE") AN EMPLOYEE
        // moves them from the active list to the deactivated/archive list
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

            user.IsActive = false; // deactivate
            _context.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"User {user.EmployeeName} deactivated and moved to archives.";
            return RedirectToAction(nameof(Index));
        }

        // FUNCTION: RESTORE AN EMPLOYEE
        // moves them from the archive back to the active list
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Restore(int id)
        {
            var user = await _context.Employees.FindAsync(id);
            if (user == null) return NotFound();

            user.IsActive = true; // reactivate
            _context.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"User {user.EmployeeName} restored to active list.";
            return RedirectToAction(nameof(Deactivated));
        }

        // FUNCTION: PERMANENTLY DELETE
        // actually removes them from database. ONLY allowed if they have no history.
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

            // SAFETY CHECK: DO THEY HAVE HISTORY?
            // if this cashier ever sold an item, we CANNOT delete them, because it would break the sales reports.
            bool hasHistory = user.Transactions.Any() || user.ItemLogs.Any() || user.Voids.Any();

            if (hasHistory)
            {
                TempData["ErrorMessage"] = "Cannot delete: This user has transaction or audit history.";
                return RedirectToAction(nameof(Deactivated));
            }

            // if they have no history (never did anything), it's safe to delete.
            _context.Employees.Remove(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "User record permanently deleted.";
            return RedirectToAction(nameof(Deactivated));
        }

        // FUNCTION: CANCEL AN INVITE
        // used when you invited the wrong email address
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelInvite(int id)
        {
            var user = await _context.Employees.FirstOrDefaultAsync(u => u.EmployeeId == id);
            if (user == null) return NotFound();

            bool isNewInvite = !user.IsActive;

            if (isNewInvite)
            {
                // if they never logged in, just delete the whole record
                _context.Employees.Remove(user);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Invitation revoked. User deleted.";
            }
            else
            {
                // if it was just a password reset request, just clear the token
                user.ResetToken = null;
                user.ResetTokenExpiry = null;
                _context.Update(user);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Reset cancelled.";
            }
            return RedirectToAction(nameof(Pending));
        }

        // FUNCTION: MANUALLY SEND PASSWORD RESET
        // useful if an employee forgot their password and can't find the email link
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

            // create a new token and email it
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

        // HELPER: RULES FOR WHO CAN BOSS WHOM
        private bool CanModifyUser(string targetRole)
        {
            var myRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (myRole == "Owner") return true; // owner can edit anyone
            if (myRole == "Admin" && targetRole != "Admin" && targetRole != "Owner") return true; // admin can edit managers/cashiers
            if (myRole == "Manager" && targetRole == "Cashier") return true; // manager can only edit cashiers
            return false;
        }
    }
}