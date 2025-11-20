using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using System.Security.Claims;

namespace ManarangVergara.Controllers
{
    // Only Bosses and Managers need access to detailed reports
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class ReportsController : Controller
    {
        private readonly PharmacyDbContext _context;

        public ReportsController(PharmacyDbContext context)
        {
            _context = context;
        }

        // GET: Reports (The Advanced Filter Page)
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate, string? paymentMethod, int? cashierId)
        {
            // 1. Default Defaults: If no date chosen, show THIS MONTH
            if (!startDate.HasValue) startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            if (!endDate.HasValue) endDate = DateTime.Today;

            // 2. Start Query
            var query = _context.Transactions
                .Include(t => t.Employee)
                .Include(t => t.SalesItems)
                .Where(t => t.Status == "Completed"); // Only report real sales

            // 3. Apply Filters
            // Date Filter (Add 1 day to EndDate to include the full day's transactions)
            query = query.Where(t => t.SalesDate >= startDate && t.SalesDate < endDate.Value.AddDays(1));

            if (!string.IsNullOrEmpty(paymentMethod))
            {
                query = query.Where(t => t.PaymentMethod == paymentMethod);
            }

            if (cashierId.HasValue)
            {
                query = query.Where(t => t.EmployeeId == cashierId);
            }

            // 4. Execute Query
            var results = await query.OrderByDescending(t => t.SalesDate).ToListAsync();

            // 5. Prepare ViewModel
            var model = new ReportViewModel
            {
                StartDate = startDate,
                EndDate = endDate,
                PaymentMethod = paymentMethod,
                CashierId = cashierId,

                Transactions = results,
                TotalRevenue = results.Sum(t => t.TotalAmount),
                TransactionCount = results.Count,

                // Load Cashiers for the dropdown list
                CashierList = await _context.Employees
                    .Where(e => e.Position == "Cashier" && e.IsActive)
                    .ToListAsync(),

                // Metadata for the PDF/Print footer
                GeneratedBy = User.FindFirst("FullName")?.Value ?? User.Identity.Name,
                GeneratedAt = DateTime.Now
            };

            return View(model);
        }

        // NOTE: We are replacing the old "DailyReport" with this dynamic "Index" page.
        // This one page can generate Daily, Monthly, or Yearly reports just by changing the dates!
    }
}