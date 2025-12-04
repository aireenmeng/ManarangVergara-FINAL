using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using System.Security.Claims;

namespace ManarangVergara.Controllers
{
    [Authorize(Roles = "Owner,Manager")]
    public class ReportsController : Controller
    {
        private readonly PharmacyDbContext _context;

        public ReportsController(PharmacyDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate, string searchString, string statusFilter, string activeTab = "tab-sales")
        {
            var start = startDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var end = endDate ?? DateTime.Today;

            ViewData["StartDate"] = start.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = end.ToString("yyyy-MM-dd");
            ViewData["CurrentFilter"] = searchString;
            ViewData["StatusFilter"] = statusFilter;
            ViewData["ActiveTab"] = activeTab;

            // 1. FETCH SALES
            var salesQuery = _context.SalesItems
                .Include(si => si.Sales).ThenInclude(s => s.Employee)
                .Include(si => si.Product).ThenInclude(p => p.Category)
                .Include(si => si.Product).ThenInclude(p => p.Inventories)
                .Where(si => si.Sales.Status == "Completed"
                           && si.Sales.SalesDate >= start
                           && si.Sales.SalesDate < end.AddDays(1));

            if (!string.IsNullOrEmpty(searchString))
            {
                salesQuery = salesQuery.Where(s => s.Product.Name.Contains(searchString)
                                                || s.Sales.SalesId.ToString().Contains(searchString));
            }

            var rawSales = await salesQuery.OrderByDescending(si => si.Sales.SalesDate).ToListAsync();

            var detailedSales = rawSales.Select(s => new DetailedSalesData
            {
                Date = s.Sales.SalesDate,
                TransactionId = s.Sales.SalesId.ToString(),
                ProductName = s.Product.Name,
                Category = s.Product.Category.CategoryName,
                Qty = s.QuantitySold,
                Price = s.Price,
                Cashier = s.Sales.Employee.EmployeeName
            }).ToList();

            // 2. FETCH INVENTORY
            var inventoryQuery = _context.Inventories
                .Include(i => i.Product)
                .Where(i => i.Quantity >= 0) 
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                inventoryQuery = inventoryQuery.Where(i => i.Product.Name.Contains(searchString) || i.BatchNumber.Contains(searchString));
            }

            var rawInventory = await inventoryQuery.OrderBy(i => i.ExpiryDate).ToListAsync();

            var fullInventory = rawInventory.Select(i => {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var nearExpiryThreshold = today.AddDays(30); // 30 Days warning

                string status = "Active";
                if (i.Quantity == 0) status = "Out of Stock";
                else if (i.ExpiryDate <= today) status = "Expired";
                else if (i.ExpiryDate <= nearExpiryThreshold) status = "Near Expiry";
                else if (i.Quantity < 20) status = "Low Stock";

                return new DetailedInventoryData
                {
                    ProductName = i.Product.Name,
                    BatchNo = i.BatchNumber,
                    ExpiryDate = i.ExpiryDate,
                    Quantity = i.Quantity,
                    DaysUntilExpiry = i.ExpiryDate.DayNumber - today.DayNumber,
                    Status = status
                };
            }).ToList();

            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
            {
                fullInventory = fullInventory.Where(i => i.Status == statusFilter).ToList();
            }

            // 3. FINANCIAL ANALYSIS (MERGED SALES + LOSSES)

            // A. Get Sales Data Grouped
            var salesStats = rawSales
                .GroupBy(si => si.Product)
                .Select(g => new {
                    ProductId = g.Key.ProductId,
                    Name = g.Key.Name,
                    QtySold = g.Sum(si => si.QuantitySold),
                    Revenue = g.Sum(si => (si.Price * si.QuantitySold) - si.Discount),
                    // Estimate Cost based on latest inventory batch
                    UnitCost = g.Key.Inventories.OrderByDescending(i => i.LastUpdated).FirstOrDefault()?.CostPrice ?? 0
                }).ToList();

            // B. Get Loss Data (Removed Items)
            var rawLosses = await _context.ItemLogs
                .Include(l => l.Product).ThenInclude(p => p.Inventories)
                .Where(l => l.Action == "Removed"
                         && l.LoggedAt >= start
                         && l.LoggedAt < end.AddDays(1))
                .ToListAsync();

            var lossStats = rawLosses
                .GroupBy(l => l.Product)
                .Select(g => new {
                    ProductId = g.Key.ProductId,
                    Name = g.Key.Name,
                    QtyLost = g.Sum(l => l.Quantity),
                    UnitCost = g.Key.Inventories.OrderByDescending(i => i.LastUpdated).FirstOrDefault()?.CostPrice ?? 0
                }).ToList();

            // C. Merge Lists (Full Outer Join Logic)
            var allProductIds = salesStats.Select(s => s.ProductId)
                                .Union(lossStats.Select(l => l.ProductId))
                                .Distinct();

            var financialReport = new List<ProductProfitData>();

            foreach (var pid in allProductIds)
            {
                var s = salesStats.FirstOrDefault(x => x.ProductId == pid);
                var l = lossStats.FirstOrDefault(x => x.ProductId == pid);

                string pName = s?.Name ?? l?.Name ?? "Unknown";
                decimal cost = s?.UnitCost ?? l?.UnitCost ?? 0;

                var row = new ProductProfitData
                {
                    ProductName = pName,

                    // Sales Info
                    QtySold = s?.QtySold ?? 0,
                    Revenue = s?.Revenue ?? 0,
                    CostOfGoodsSold = (s?.QtySold ?? 0) * cost,

                    // Loss Info
                    QtyLost = l?.QtyLost ?? 0,
                    LossValue = (l?.QtyLost ?? 0) * cost
                };
                financialReport.Add(row);
            }

            // 4. VOID LOGS
            var voidQuery = _context.Voids
                .Include(v => v.Sales).ThenInclude(s => s.Employee)
                .Include(v => v.Employee)
                .Where(v => v.VoidedAt >= start && v.VoidedAt < end.AddDays(1));

            var voids = await voidQuery.OrderByDescending(v => v.VoidedAt)
                .Select(v => new VoidHistoryViewModel
                {
                    TransactionId = v.SalesId,
                    VoidDate = v.VoidedAt,
                    CashierName = v.Sales.Employee.EmployeeName,
                    ManagerName = v.Employee.EmployeeName,
                    Reason = v.VoidReason,
                    TotalAmount = v.Sales.TotalAmount
                }).ToListAsync();

            bool canViewFinancials = User.IsInRole("Owner");

            var model = new ReportViewModel
            {
                StartDate = start,
                EndDate = end,
                DetailedSales = detailedSales,
                TotalRevenue = detailedSales.Sum(x => x.Total),
                FullInventory = fullInventory,
                TotalAssetValue = canViewFinancials ? rawInventory.Sum(i => i.Quantity * i.CostPrice) : 0,

                // Merged Financials
                ProductProfitability = financialReport.OrderByDescending(x => x.NetProfit).ToList(),
                GrossProfit = canViewFinancials ? financialReport.Sum(x => x.GrossProfit) : 0,
                TotalLossValue = canViewFinancials ? financialReport.Sum(x => x.LossValue) : 0,

                VoidLogs = voids,
                GeneratedBy = User.Identity.Name,
                GeneratedAt = DateTime.Now,
                ShowFinancials = canViewFinancials
            };

            return View(model);
        }
    }
}