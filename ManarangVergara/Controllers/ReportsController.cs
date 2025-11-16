using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models.Database;

namespace ManarangVergara.Controllers
{
    // SECURE: Only Admins and Owners should see financial reports
    [Authorize(Roles = "Admin,Owner")]
    public class ReportsController : Controller
    {
        private readonly PharmacyDbContext _context;

        public ReportsController(PharmacyDbContext context)
        {
            _context = context;
        }

        // GET: Daily Report (Defaults to today if no date selected)
        public async Task<IActionResult> DailyReport(DateTime? date)
        {
            // 1. Determine the date range (from 12:00 AM today to 12:00 AM tomorrow)
            var reportDate = date ?? DateTime.Today;
            var nextDay = reportDate.AddDays(1);

            // 2. Fetch all COMPLETED sales for that day
            var sales = await _context.Transactions
                .Include(t => t.Employee)
                .Where(t => t.SalesDate >= reportDate && t.SalesDate < nextDay && t.Status == "Completed")
                .OrderBy(t => t.SalesDate)
                .ToListAsync();

            // 3. Calculate Financial Totals
            ViewData["ReportDate"] = reportDate;
            ViewData["TotalSales"] = sales.Sum(t => t.TotalAmount);
            ViewData["TransactionCount"] = sales.Count;

            // 4. Fetch Action Items (Low Stock Warnings) for the report footer
            // This helps the owner see what needs reordering immediately.
            ViewData["LowStock"] = await _context.Inventories
                .Include(i => i.Product)
                .Where(i => i.Quantity < 20) // The reorder threshold we decided on
                .OrderBy(i => i.Quantity)
                .ToListAsync();

            return View(sales);
        }
    }
}