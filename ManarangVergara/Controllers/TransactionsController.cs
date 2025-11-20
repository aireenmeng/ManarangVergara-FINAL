using ManarangVergara.Helpers;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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
        public async Task<IActionResult> Index(string searchString, string sortOrder, int? pageNumber = 1)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentSort"] = sortOrder;

            // Sorting Parameters
            ViewData["DateSortParm"] = String.IsNullOrEmpty(sortOrder) ? "date_desc" : "";
            ViewData["CashierSortParm"] = sortOrder == "Cashier" ? "cashier_desc" : "Cashier";
            ViewData["StatusSortParm"] = sortOrder == "Status" ? "status_desc" : "Status";
            ViewData["TotalSortParm"] = sortOrder == "Total" ? "total_desc" : "Total";

            var query = _context.Transactions
                .Include(t => t.SalesItems)
                .Include(t => t.Employee)
                .AsQueryable();

            // Security: Cashiers only see their own sales
            if (User.IsInRole("Cashier"))
            {
                var employeeIdClaim = User.FindFirst("EmployeeId");
                if (employeeIdClaim != null && int.TryParse(employeeIdClaim.Value, out int empId))
                {
                    query = query.Where(t => t.EmployeeId == empId);
                }
            }

            // Search Logic
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(t => t.Employee.EmployeeName.Contains(searchString)
                                     || t.PaymentMethod.Contains(searchString)
                                     || (t.ReferenceNo != null && t.ReferenceNo.Contains(searchString)));
            }

            // Sort Logic
            query = sortOrder switch
            {
                "date_desc" => query.OrderByDescending(t => t.SalesDate),
                "Cashier" => query.OrderBy(t => t.Employee.EmployeeName),
                "cashier_desc" => query.OrderByDescending(t => t.Employee.EmployeeName),
                "Status" => query.OrderBy(t => t.Status),
                "status_desc" => query.OrderByDescending(t => t.Status),
                "Total" => query.OrderBy(t => t.TotalAmount),
                "total_desc" => query.OrderByDescending(t => t.TotalAmount),
                _ => query.OrderByDescending(t => t.SalesDate),
            };

            // Project to ViewModel
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

            // --- PAGINATION LOGIC ---
            int pageSize = 10; // Set to 10 as requested
            return View(PaginatedList<TransactionListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // GET: Transactions/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var transaction = await _context.Transactions
                .Include(t => t.Employee)
                .Include(t => t.SalesItems)
                    .ThenInclude(si => si.Product)
                .FirstOrDefaultAsync(m => m.SalesId == id);

            if (transaction == null) return NotFound();

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

        // POST: Void Transaction
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Void(int id, string reason) // Added reason parameter
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
                    var targetBatch = await _context.Inventories
                        .Where(i => i.ProductId == item.ProductId)
                        .OrderByDescending(i => i.ExpiryDate)
                        .FirstOrDefaultAsync();

                    if (targetBatch != null)
                    {
                        targetBatch.Quantity += item.QuantitySold;
                        _context.Inventories.Update(targetBatch);
                    }
                }

                // 2. Update Transaction Status
                sale.Status = "Refunded";
                _context.Update(sale);

                // 3. Log the Void
                var managerId = int.Parse(User.FindFirst("EmployeeId").Value);
                var voidLog = new ManarangVergara.Models.Database.Void
                {
                    SalesId = id,
                    EmployeeId = managerId, // The manager who voided it
                    VoidedAt = DateTime.Now,
                    VoidReason = reason
                };
                _context.Voids.Add(voidLog);

                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                TempData["SuccessMessage"] = $"Transaction #{id} voided successfully.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                TempData["ErrorMessage"] = "Void Failed: " + ex.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        // GET: Void History
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> VoidHistory()
        {
            var voids = await _context.Voids
                .Include(v => v.Sales)
                .Include(v => v.Sales.Employee) // Original Cashier
                .Include(v => v.Employee)       // Manager who voided
                .OrderByDescending(v => v.VoidedAt)
                .Select(v => new VoidHistoryViewModel
                {
                    TransactionId = v.SalesId,
                    VoidDate = v.VoidedAt,
                    CashierName = v.Sales.Employee.EmployeeName,
                    ManagerName = v.Employee.EmployeeName,
                    TotalAmount = v.Sales.TotalAmount,
                    Reason = v.VoidReason
                })
                .ToListAsync();

            return View(voids);
        }
    }
}