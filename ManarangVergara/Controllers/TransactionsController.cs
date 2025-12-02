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

        // ============================================================
        // 1. TRANSACTION HISTORY (INDEX)
        // ============================================================
        public async Task<IActionResult> Index(string searchString, string sortOrder, DateTime? startDate, DateTime? endDate, int? pageNumber = 1)
        {
            // --- 1. DATE FILTERING DEFAULTS ---
            if (!startDate.HasValue) startDate = DateTime.Today;
            if (!endDate.HasValue) endDate = DateTime.Today.AddDays(1).AddTicks(-1);

            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");

            // --- 2. SEARCH & SORT MEMORY ---
            ViewData["CurrentSort"] = sortOrder;
            ViewData["CurrentFilter"] = searchString;

            // --- 3. SORT TOGGLE LOGIC ---
            // If the current sort is "Date", the next click should be "date_desc". 
            // If it is anything else (or null), the next click should be "Date" (Ascending).
            ViewData["DateSortParm"] = sortOrder == "Date" ? "date_desc" : "Date";
            ViewData["CashierSortParm"] = sortOrder == "Cashier" ? "cashier_desc" : "Cashier";
            ViewData["TotalSortParm"] = sortOrder == "Total" ? "total_desc" : "Total";
            ViewData["StatusSortParm"] = sortOrder == "Status" ? "status_desc" : "Status";

            // --- 4. BASE QUERY ---
            var query = _context.Transactions
                .Include(t => t.SalesItems)
                .Include(t => t.Employee)
                .Where(t => t.SalesDate >= startDate && t.SalesDate <= endDate)
                .AsQueryable();

            // --- 5. SECURITY: CASHIER LIMITATION ---
            if (User.IsInRole("Cashier"))
            {
                var employeeIdClaim = User.FindFirst("EmployeeId");
                if (employeeIdClaim != null && int.TryParse(employeeIdClaim.Value, out int empId))
                {
                    query = query.Where(t => t.EmployeeId == empId);
                }
            }

            // --- 6. SEARCH LOGIC ---
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(t => t.Employee.EmployeeName.Contains(searchString)
                                     || t.PaymentMethod.Contains(searchString)
                                     || (t.ReferenceNo != null && t.ReferenceNo.Contains(searchString)));
            }

            // --- 7. SORTING SWITCH ---
            query = sortOrder switch
            {
                "Date" => query.OrderBy(t => t.SalesDate),           // Ascending (Oldest First)
                "date_desc" => query.OrderByDescending(t => t.SalesDate), // Descending (Newest First)

                "Cashier" => query.OrderBy(t => t.Employee.EmployeeName),
                "cashier_desc" => query.OrderByDescending(t => t.Employee.EmployeeName),

                "Total" => query.OrderBy(t => t.TotalAmount),
                "total_desc" => query.OrderByDescending(t => t.TotalAmount),

                "Status" => query.OrderBy(t => t.Status),
                "status_desc" => query.OrderByDescending(t => t.Status),

                _ => query.OrderByDescending(t => t.SalesDate), // Default: Newest First
            };

            // --- 8. MAP TO VIEW MODEL ---
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

            // --- 9. PAGINATION ---
            int pageSize = 10;
            return View(PaginatedList<TransactionListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // ============================================================
        // 2. DETAILS VIEW
        // ============================================================
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var transaction = await _context.Transactions
                .Include(t => t.Employee)
                .Include(t => t.SalesItems)
                    .ThenInclude(si => si.Product)
                .FirstOrDefaultAsync(m => m.SalesId == id);

            if (transaction == null) return NotFound();

            // Security Check for Cashiers
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

        // ============================================================
        // 3. VOID LOGIC
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> Void(int id, string reason)
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

                // Restock Inventory
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

                // Update Status
                sale.Status = "Refunded";
                _context.Update(sale);

                // Create Log
                var managerId = int.Parse(User.FindFirst("EmployeeId").Value);
                var voidLog = new ManarangVergara.Models.Database.Void
                {
                    SalesId = id,
                    EmployeeId = managerId,
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

        // ============================================================
        // 4. VOID HISTORY
        // ============================================================
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> VoidHistory(int? pageNumber = 1)
        {
            var query = _context.Voids
                .Include(v => v.Sales)
                .Include(v => v.Sales.Employee)
                .Include(v => v.Employee)
                .OrderByDescending(v => v.VoidedAt);

            var list = await query.Select(v => new VoidHistoryViewModel
            {
                TransactionId = v.SalesId,
                VoidDate = v.VoidedAt,
                CashierName = v.Sales.Employee.EmployeeName,
                ManagerName = v.Employee.EmployeeName,
                TotalAmount = v.Sales.TotalAmount,
                Reason = v.VoidReason
            }).ToListAsync();

            int pageSize = 10;
            return View(PaginatedList<VoidHistoryViewModel>.Create(list.AsQueryable(), pageNumber ?? 1, pageSize));
        }
    }
}