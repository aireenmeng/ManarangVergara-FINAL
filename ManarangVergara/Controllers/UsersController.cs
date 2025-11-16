using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models.Database;

namespace ManarangVergara.Controllers
{
    // SECURE: Only the biggest bosses can manage users
    [Authorize(Roles = "Admin,Owner")]
    public class UsersController : Controller
    {
        private readonly PharmacyDbContext _context;

        public UsersController(PharmacyDbContext context)
        {
            _context = context;
        }

        // GET: Users List (With Sorting)
        public async Task<IActionResult> Index(string sortOrder)
        {
            // --- Setup Sort Toggles ---
            ViewData["CurrentSort"] = sortOrder;
            ViewData["UserSortParm"] = String.IsNullOrEmpty(sortOrder) ? "user_desc" : "";
            ViewData["NameSortParm"] = sortOrder == "Name" ? "name_desc" : "Name";
            ViewData["RoleSortParm"] = sortOrder == "Role" ? "role_desc" : "Role";
            ViewData["ContactSortParm"] = sortOrder == "Contact" ? "contact_desc" : "Contact";

            // --- Start Query ---
            var users = from e in _context.Employees
                        select e;

            // --- Apply Sorting ---
            users = sortOrder switch
            {
                "user_desc" => users.OrderByDescending(e => e.Username),
                "Name" => users.OrderBy(e => e.EmployeeName),
                "name_desc" => users.OrderByDescending(e => e.EmployeeName),
                "Role" => users.OrderBy(e => e.Position),
                "role_desc" => users.OrderByDescending(e => e.Position),
                "Contact" => users.OrderBy(e => e.ContactInfo),
                "contact_desc" => users.OrderByDescending(e => e.ContactInfo),
                _ => users.OrderBy(e => e.Username), // Default: Username Ascending
            };

            return View(await users.ToListAsync());
        }

        // GET: Users/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee employee, string rawPassword)
        {
            // We perform manual validation for rawPassword
            if (string.IsNullOrEmpty(rawPassword) || rawPassword.Length < 6)
            {
                // FIX 1: Attach error to "rawPassword" to match the input name
                ModelState.AddModelError("rawPassword", "Password must be at least 6 characters.");
            }

            // FIX 2: Tell validation to ignore the model's main "Password" property
            // This stops the "ModelState.IsValid" check from failing silently.
            ModelState.Remove("Password");

            if (ModelState.IsValid)
            {
                // 1. Check if username is taken
                if (await _context.Employees.AnyAsync(e => e.Username == employee.Username))
                {
                    ModelState.AddModelError("Username", "Username is already taken.");
                    return View(employee);
                }

                // 2. HASH THE PASSWORD
                employee.Password = BCrypt.Net.BCrypt.HashPassword(rawPassword);

                _context.Add(employee);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"User {employee.Username} created successfully!";
                return RedirectToAction(nameof(Index));
            }
            return View(employee);
        }
    }
}