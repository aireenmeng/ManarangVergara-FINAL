using ManarangVergara.Helpers;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ManarangVergara.Controllers
{
    // locks this whole section so only logged-in users can enter
    [Authorize]
    public class TransactionsController : Controller
    {
        private readonly PharmacyDbContext _context;

        // connects to your database so we can pull sales data
        public TransactionsController(PharmacyDbContext context)
        {
            _context = context;
        }

        // MAIN FUNCTION: SHOWS THE LIST OF SALES HISTORY (THE TABLE VIEW)
        // this runs when you click the "transactions" tab
        public async Task<IActionResult> Index(string searchString, string sortOrder, DateTime? startDate, DateTime? endDate, int? pageNumber = 1)
        {
            // default settings: if you didn't pick a date, just show today's sales
            if (!startDate.HasValue) startDate = DateTime.Today;
            if (!endDate.HasValue) endDate = DateTime.Today.AddDays(1).AddTicks(-1); // sets time to 11:59 pm

            // saves these dates so they stay in the date picker box on the screen
            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");

            // database query: get transactions and grab the items and employee info with them
            var query = _context.Transactions
                .Include(t => t.SalesItems)
                .Include(t => t.Employee)
                .Where(t => t.SalesDate >= startDate && t.SalesDate <= endDate) // filter by the dates chosen above
                .AsQueryable();

            // SECURITY: CHECK IF USER IS A CASHIER
            // if it's a cashier, filter the list to ONLY show sales they made themselves
            if (User.IsInRole("Cashier"))
            {
                var employeeIdClaim = User.FindFirst("EmployeeId");
                if (employeeIdClaim != null && int.TryParse(employeeIdClaim.Value, out int empId))
                {
                    query = query.Where(t => t.EmployeeId == empId);
                }
            }

            // SEARCH BAR LOGIC
            // if you typed something, filter the list by cashier name, payment type (gcash), or reference number
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(t => t.Employee.EmployeeName.Contains(searchString)
                                     || t.PaymentMethod.Contains(searchString)
                                     || (t.ReferenceNo != null && t.ReferenceNo.Contains(searchString)));
            }

            // SORTING LOGIC
            // rearranges the list based on which column header you clicked
            query = sortOrder switch
            {
                "date_desc" => query.OrderByDescending(t => t.SalesDate), // newest first
                "Cashier" => query.OrderBy(t => t.Employee.EmployeeName),
                "cashier_desc" => query.OrderByDescending(t => t.Employee.EmployeeName),
                "Status" => query.OrderBy(t => t.Status),
                "status_desc" => query.OrderByDescending(t => t.Status),
                "Total" => query.OrderBy(t => t.TotalAmount),
                "total_desc" => query.OrderByDescending(t => t.TotalAmount),
                _ => query.OrderByDescending(t => t.SalesDate), // default sorting
            };

            // CONVERT TO VIEWMODEL
            // this cleans up the data before sending it to the webpage (calculating total items, etc.)
            var data = await query
                .Select(t => new TransactionListViewModel
                {
                    TransactionId = t.SalesId,
                    Date = t.SalesDate,
                    TotalAmount = t.TotalAmount,
                    PaymentMethod = t.PaymentMethod,
                    Status = t.Status,
                    ItemCount = t.SalesItems.Sum(si => si.QuantitySold), // counts how many items were in the cart
                    CashierName = t.Employee.EmployeeName
                })
                .ToListAsync();

            // PAGINATION LOGIC
            // splits the list into pages of 10 items so the page doesn't lag
            int pageSize = 10;
            return View(PaginatedList<TransactionListViewModel>.Create(data.AsQueryable(), pageNumber ?? 1, pageSize));
        }

        // FUNCTION: OPENS THE DETAILS OF ONE SPECIFIC SALE
        // this runs when you click "details" on a specific row
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            // find the transaction and load the specific products sold
            var transaction = await _context.Transactions
                .Include(t => t.Employee)
                .Include(t => t.SalesItems)
                    .ThenInclude(si => si.Product)
                .FirstOrDefaultAsync(m => m.SalesId == id);

            if (transaction == null) return NotFound();

            // PRIVACY CHECK
            // if a cashier tries to view a sale that isn't theirs, block them
            if (User.IsInRole("Cashier"))
            {
                var employeeIdClaim = User.FindFirst("EmployeeId");
                if (employeeIdClaim != null && int.TryParse(employeeIdClaim.Value, out int empId))
                {
                    if (transaction.EmployeeId != empId) return Forbid(); // access denied
                }
            }

            return View(transaction);
        }

        // FUNCTION: VOID/CANCEL A TRANSACTION
        // this runs when a manager/admin clicks "void" and gives a reason
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Owner,Manager")] // only bosses can do this
        public async Task<IActionResult> Void(int id, string reason)
        {
            // start a "database transaction" - this acts like a safety net. 
            // if anything fails halfway, it undoes everything.
            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var sale = await _context.Transactions
                    .Include(t => t.SalesItems)
                    .FirstOrDefaultAsync(t => t.SalesId == id);

                if (sale == null) return NotFound();

                // you can't void something that was already refunded or pending
                if (sale.Status != "Completed")
                {
                    TempData["ErrorMessage"] = "Only completed transactions can be voided.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                // STEP 1: RESTOCK INVENTORY
                // loop through every item bought in this sale
                foreach (var item in sale.SalesItems)
                {
                    // find the batch of medicine this item came from
                    var targetBatch = await _context.Inventories
                        .Where(i => i.ProductId == item.ProductId)
                        .OrderByDescending(i => i.ExpiryDate)
                        .FirstOrDefaultAsync();

                    // put the items back on the shelf (add quantity back to database)
                    if (targetBatch != null)
                    {
                        targetBatch.Quantity += item.QuantitySold;
                        _context.Inventories.Update(targetBatch);
                    }
                }

                // STEP 2: UPDATE STATUS
                // mark the sale as refunded so it doesn't count in daily profit
                sale.Status = "Refunded";
                _context.Update(sale);

                // STEP 3: CREATE AN AUDIT LOG
                // record who voided this and why, for security purposes
                var managerId = int.Parse(User.FindFirst("EmployeeId").Value);
                var voidLog = new ManarangVergara.Models.Database.Void
                {
                    SalesId = id,
                    EmployeeId = managerId, // the manager who clicked void
                    VoidedAt = DateTime.Now,
                    VoidReason = reason
                };
                _context.Voids.Add(voidLog);

                // save all changes permanently. if we reached here, everything worked.
                await _context.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                TempData["SuccessMessage"] = $"Transaction #{id} voided successfully.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                // if something crashed, undo all changes (don't return stock, don't change status)
                await dbTransaction.RollbackAsync();
                TempData["ErrorMessage"] = "Void Failed: " + ex.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        // FUNCTION: SHOWS THE HISTORY OF VOIDED SALES
        // separate page to see a list of everything that was cancelled
        [Authorize(Roles = "Admin,Owner,Manager")]
        public async Task<IActionResult> VoidHistory(int? pageNumber = 1)
        {
            // get list of voids, including who sold it originally and who cancelled it
            var query = _context.Voids
                .Include(v => v.Sales)
                .Include(v => v.Sales.Employee) // original cashier
                .Include(v => v.Employee)       // manager who voided
                .OrderByDescending(v => v.VoidedAt);

            // make a clean list for the view
            var list = await query.Select(v => new VoidHistoryViewModel
            {
                TransactionId = v.SalesId,
                VoidDate = v.VoidedAt,
                CashierName = v.Sales.Employee.EmployeeName,
                ManagerName = v.Employee.EmployeeName,
                TotalAmount = v.Sales.TotalAmount,
                Reason = v.VoidReason
            }).ToListAsync();

            // pagination again - 10 items per page
            int pageSize = 10;
            return View(PaginatedList<VoidHistoryViewModel>.Create(list.AsQueryable(), pageNumber ?? 1, pageSize));
        }
    }
}