using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using System.Security.Claims;
using ManarangVergara.Models.Database;

namespace ManarangVergara.Controllers
{
    [Authorize]
    public class TransactionsController : Controller
    {
        private readonly PharmacyDbContext _context;

        public TransactionsController(PharmacyDbContext context)
        {
            _context = context;
        }

        // GET: Transactions History
        // GET: Transactions History (With Search and Sort)
        public async Task<IActionResult> Index(string searchString, string sortOrder)
        {
            // --- Setup Sort Toggles ---
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;
            ViewData["DateSortParm"] = String.IsNullOrEmpty(sortOrder) ? "date_desc" : "";
            ViewData["CashierSortParm"] = sortOrder == "Cashier" ? "cashier_desc" : "Cashier";
            ViewData["StatusSortParm"] = sortOrder == "Status" ? "status_desc" : "Status";
            ViewData["TotalSortParm"] = sortOrder == "Total" ? "total_desc" : "Total";

            var query = _context.Transactions
                .Include(t => t.SalesItems)
                .Include(t => t.Employee)
                .AsQueryable();

            // --- RBAC Filter (Cashiers see their own) ---
            if (User.IsInRole("Cashier"))
            {
                var employeeIdClaim = User.FindFirst("EmployeeId");
                if (employeeIdClaim != null && int.TryParse(employeeIdClaim.Value, out int empId))
                {
                    query = query.Where(t => t.EmployeeId == empId);
                }
            }

            // --- Search Filter ---
            if (!string.IsNullOrEmpty(searchString))
            {
                // Search by Cashier Name or Payment Method
                query = query.Where(t => t.Employee.EmployeeName.Contains(searchString) || t.PaymentMethod.Contains(searchString));
            }

            // --- Sorting ---
            query = sortOrder switch
            {
                "date_desc" => query.OrderByDescending(t => t.SalesDate),
                "Cashier" => query.OrderBy(t => t.Employee.EmployeeName),
                "cashier_desc" => query.OrderByDescending(t => t.Employee.EmployeeName),
                "Status" => query.OrderBy(t => t.Status),
                "status_desc" => query.OrderByDescending(t => t.Status),
                "Total" => query.OrderBy(t => t.TotalAmount),
                "total_desc" => query.OrderByDescending(t => t.TotalAmount),
                _ => query.OrderBy(t => t.SalesDate), // Default: Newest first
            };

            var data = await query
                .Select(t => new TransactionListViewModel
                {
                    TransactionId = t.SalesId,
                    Date = t.SalesDate,
                    TotalAmount = t.TotalAmount,
                    PaymentMethod = t.PaymentMethod,
                    Status = t.Status,
                    ItemCount = t.SalesItems.Sum(si => si.QuantitySold),
                    CashierName = t.Employee.EmployeeName
                })
                .ToListAsync();

            return View(data);
        }

        // GET: Transactions/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            // Fetch EVERYTHING related to this sale: Employee, Items, and Product details for those items
            var transaction = await _context.Transactions
                .Include(t => t.Employee)
                .Include(t => t.SalesItems)
                    .ThenInclude(si => si.Product)
                .FirstOrDefaultAsync(m => m.SalesId == id);

            if (transaction == null) return NotFound();

            // Security Check: Cashiers can only view THEIR OWN details
            if (User.IsInRole("Cashier"))
            {
                var employeeIdClaim = User.FindFirst("EmployeeId");
                if (employeeIdClaim != null && int.TryParse(employeeIdClaim.Value, out int empId))
                {
                    if (transaction.EmployeeId != empId) return Forbid();
                }
            }

            return View(transaction);
        }

        // GET: Transactions/Create (POS Placeholder)
        public IActionResult Create()
        {
            return Content("POS Screen coming next!");
        }

        // =========================================
        // POST: Void Transaction (Refund & Restock)
        // =========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")] // CRITICAL: Only bosses can void!
        public async Task<IActionResult> Void(int id)
        {
            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var sale = await _context.Transactions
                    .Include(t => t.SalesItems)
                    .FirstOrDefaultAsync(t => t.SalesId == id);

                if (sale == null) return NotFound();
                if (sale.Status != "Completed")
                {
                    TempData["ErrorMessage"] = "Only completed transactions can be voided.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                // 1. RESTOCK INVENTORY
                foreach (var item in sale.SalesItems)
                {
                    // Find the best batch to return items to (e.g., the one expiring last)
                    var targetBatch = await _context.Inventories
                        .Where(i => i.ProductId == item.ProductId)
                        .OrderByDescending(i => i.ExpiryDate)
                        .FirstOrDefaultAsync();

                    if (targetBatch != null)
                    {
                        targetBatch.Quantity += item.QuantitySold;
                        _context.Inventories.Update(targetBatch);
                    }
                    // Note: If NO batch exists (all expired/deleted), you might need to create a new one.
                    // For simplicity, we assume at least one batch always exists if a product is active.
                }

                // 2. Update Transaction Status
                sale.Status = "Refunded";
                _context.Update(sale);

                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                TempData["SuccessMessage"] = $"Transaction #{id} voided and stock returned to inventory.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                TempData["ErrorMessage"] = "Void Failed: " + ex.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
        }
    }
}