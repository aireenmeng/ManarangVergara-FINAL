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

        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            // 1. Default to This Month if no date selected
            var start = startDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var end = endDate ?? DateTime.Today;

            // 2. BASE QUERY: Completed Transactions in Range
            var salesData = await _context.SalesItems
                .Include(si => si.Sales)
                .Include(si => si.Product).ThenInclude(p => p.Category)
                .Include(si => si.Product).ThenInclude(p => p.Inventories)
                .Where(si => si.Sales.Status == "Completed"
                          && si.Sales.SalesDate >= start
                          && si.Sales.SalesDate < end.AddDays(1))
                .ToListAsync();

            // 3. TAB 1 LOGIC: SALES
            var categoryStats = salesData
                .GroupBy(si => si.Product.Category.CategoryName)
                .Select(g => new CategorySalesData
                {
                    Category = g.Key,
                    TotalSales = g.Sum(si => (si.Price * si.QuantitySold) - si.Discount)
                }).OrderByDescending(x => x.TotalSales).ToList();

            var topProducts = salesData
                .GroupBy(si => si.Product.Name)
                .Select(g => new TopProductData
                {
                    ProductName = g.Key,
                    QuantitySold = g.Sum(si => si.QuantitySold),
                    Revenue = g.Sum(si => (si.Price * si.QuantitySold) - si.Discount)
                }).OrderByDescending(x => x.QuantitySold).Take(10).ToList();

            // 4. TAB 2 LOGIC: INVENTORY (Current State, not Historical)
            var inventory = await _context.Inventories.Include(i => i.Product).Where(i => i.Quantity > 0).ToListAsync();

            var nearExpiry = inventory.Where(i => i.ExpiryDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(90)))
                                      .OrderBy(i => i.ExpiryDate)
                                      .Select(i => new InventoryListViewModel
                                      {
                                          ProductName = i.Product.Name,
                                          BatchNumber = i.BatchNumber, // Assuming you added this to VM, if not ignore
                                          ExpiryDate = i.ExpiryDate,
                                          Quantity = i.Quantity
                                      }).ToList();

            // 5. TAB 3 LOGIC: PROFITABILITY
            var profitability = salesData
                .GroupBy(si => si.Product)
                .Select(g => {
                    // Estimated Cost (Latest Batch)
                    decimal cost = g.Key.Inventories.OrderByDescending(i => i.LastUpdated).FirstOrDefault()?.CostPrice ?? 0;
                    decimal revenue = g.Sum(si => (si.Price * si.QuantitySold) - si.Discount);
                    int qty = g.Sum(si => si.QuantitySold);

                    return new ProductProfitData
                    {
                        ProductName = g.Key.Name,
                        QtySold = qty,
                        Revenue = revenue,
                        Cost = cost * qty
                    };
                }).OrderByDescending(x => x.Profit).ToList();

            // 6. TAB 4: AUDIT (Voids)
            var voids = await _context.Voids
                .Include(v => v.Sales).ThenInclude(s => s.Employee)
                .Include(v => v.Employee)
                .Where(v => v.VoidedAt >= start && v.VoidedAt < end.AddDays(1))
                .OrderByDescending(v => v.VoidedAt)
                .Select(v => new VoidHistoryViewModel
                {
                    TransactionId = v.SalesId,
                    VoidDate = v.VoidedAt,
                    CashierName = v.Sales.Employee.EmployeeName,
                    ManagerName = v.Employee.EmployeeName, // Authorized By
                    Reason = v.VoidReason,
                    TotalAmount = v.Sales.TotalAmount
                }).ToListAsync();

            // 7. SECURITY CHECK (New Logic)
            // Only Owners and Admins can see Cost, Profit, and Margins.
            bool canViewFinancials = User.IsInRole("Owner") || User.IsInRole("Admin");

            // BUILD MODEL
            var model = new ReportViewModel
            {
                StartDate = start,
                EndDate = end,
                TotalRevenue = salesData.Sum(si => (si.Price * si.QuantitySold) - si.Discount),
                TransactionCount = salesData.Select(si => si.SalesId).Distinct().Count(),
                SalesByCategory = categoryStats,
                TopSellingProducts = topProducts,
                NearExpiryItems = nearExpiry,
                RecentVoids = voids,
                GeneratedBy = User.Identity.Name,
                GeneratedAt = DateTime.Now,

                // SENSITIVE DATA HANDLING
                ShowFinancials = canViewFinancials,

                // If Manager, hide these numbers (Set to 0 or Empty List)
                TotalInventoryValue = canViewFinancials ? inventory.Sum(i => i.Quantity * i.CostPrice) : 0,
                GrossProfit = canViewFinancials ? profitability.Sum(x => x.Profit) : 0,

                // We still pass the list to Managers so they can see REVENUE, 
                // but the View will hide the Cost/Profit columns.
                ProductProfitability = profitability
            };

            return View(model);
        }
    }
    // NOTE: We are replacing the old "DailyReport" with this dynamic "Index" page.
    // This one page can generate Daily, Monthly, or Yearly reports just by changing the dates!
}