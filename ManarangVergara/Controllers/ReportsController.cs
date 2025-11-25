using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using System.Security.Claims;
using System.Text.Json;

namespace ManarangVergara.Controllers
{
    // ============================================================
    // reports controller (business intelligence)
    // ============================================================
    // handles the deep analytics of the system.
    // gathers data on sales, inventory, profits, and security audits.
    // restricted to boss-level roles (admin, owner, manager).
    [Authorize(Roles = "Admin,Owner,Manager")]
    public class ReportsController : Controller
    {
        private readonly PharmacyDbContext _context;

        public ReportsController(PharmacyDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 1. generate report (main logic)
        // ============================================================

        // get: builds the massive report based on the selected date range.
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            // 1. default to this month if no date selected
            var start = startDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var end = endDate ?? DateTime.Today;

            // 2. base query: fetch completed transactions in range
            // we fetch sales items because they hold the product details we need for analysis.
            var salesData = await _context.SalesItems
                .Include(si => si.Sales)
                .Include(si => si.Product).ThenInclude(p => p.Category)
                .Include(si => si.Product).ThenInclude(p => p.Inventories)
                .Where(si => si.Sales.Status == "Completed"
                           && si.Sales.SalesDate >= start
                           && si.Sales.SalesDate < end.AddDays(1))
                .ToListAsync();

            // 3. tab 1 logic: sales analysis
            // groups sales by category to see which product types are performing best.
            var categoryStats = salesData
                .GroupBy(si => si.Product.Category.CategoryName)
                .Select(g => new CategorySalesData
                {
                    Category = g.Key,
                    TotalSales = g.Sum(si => (si.Price * si.QuantitySold) - si.Discount)
                }).OrderByDescending(x => x.TotalSales).ToList();

            // identifies the top 10 best-selling products by quantity.
            var topProducts = salesData
                .GroupBy(si => si.Product.Name)
                .Select(g => new TopProductData
                {
                    ProductName = g.Key,
                    QuantitySold = g.Sum(si => si.QuantitySold),
                    Revenue = g.Sum(si => (si.Price * si.QuantitySold) - si.Discount)
                }).OrderByDescending(x => x.QuantitySold).Take(10).ToList();

            // 4. tab 2 logic: inventory health (current state)
            // gets all active inventory to check for expiry and stock levels.
            var inventory = await _context.Inventories.Include(i => i.Product).Where(i => i.Quantity > 0).ToListAsync();

            // filters for items expiring within 90 days.
            var nearExpiry = inventory.Where(i => i.ExpiryDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(90)))
                                      .OrderBy(i => i.ExpiryDate)
                                      .Select(i => new InventoryListViewModel
                                      {
                                          ProductName = i.Product.Name,
                                          BatchNumber = i.BatchNumber,
                                          ExpiryDate = i.ExpiryDate,
                                          Quantity = i.Quantity
                                      }).ToList();

            // 5. tab 3 logic: profitability analysis
            // calculates estimated profit by comparing sales revenue against current cost price.
            var profitability = salesData
                .GroupBy(si => si.Product)
                .Select(g => {
                    // uses the latest inventory batch cost as an estimate for cost of goods sold.
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

            // 6. tab 4: security audit (voids)
            // fetches the log of cancelled transactions to monitor for suspicious activity.
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
                    ManagerName = v.Employee.EmployeeName, // authorized by
                    Reason = v.VoidReason,
                    TotalAmount = v.Sales.TotalAmount
                }).ToListAsync();

            // 7. security check (financial privacy)
            // prevents managers from seeing sensitive cost/profit data.
            bool canViewFinancials = User.IsInRole("Owner") || User.IsInRole("Admin");

            // build the final model package
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

                // sensitive data handling
                ShowFinancials = canViewFinancials,

                // if manager, hide these total asset numbers (set to 0)
                TotalInventoryValue = canViewFinancials ? inventory.Sum(i => i.Quantity * i.CostPrice) : 0,
                GrossProfit = canViewFinancials ? profitability.Sum(x => x.Profit) : 0,

                // we still pass the list to managers so they can see revenue, 
                // but the view will hide the cost/profit columns.
                ProductProfitability = profitability
            };

            return View(model);
        }
    }
}