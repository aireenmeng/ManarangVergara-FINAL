using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using System.Security.Claims;
using System.Text.Json;

// ... imports

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
        var start = startDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var end = endDate ?? DateTime.Today;

        // 1. FETCH RAW SALES DATA (For Tabular Report)
        var rawSales = await _context.SalesItems
            .Include(si => si.Sales).ThenInclude(s => s.Employee)
            .Include(si => si.Product).ThenInclude(p => p.Category)
            .Include(si => si.Product).ThenInclude(p => p.Inventories)
            .Where(si => si.Sales.Status == "Completed"
                       && si.Sales.SalesDate >= start
                       && si.Sales.SalesDate < end.AddDays(1))
            .OrderByDescending(si => si.Sales.SalesDate)
            .ToListAsync();

        // Map to ViewModel
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

        // 2. FETCH FULL INVENTORY (For Tabular Report)
        var rawInventory = await _context.Inventories
            .Include(i => i.Product)
            .Where(i => i.Quantity > 0) // Only show items physically in store
            .OrderBy(i => i.ExpiryDate)
            .ToListAsync();

        var fullInventory = rawInventory.Select(i => new DetailedInventoryData
        {
            ProductName = i.Product.Name,
            BatchNo = i.BatchNumber,
            ExpiryDate = i.ExpiryDate,
            Quantity = i.Quantity,
            DaysUntilExpiry = i.ExpiryDate.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber,
            Status = (i.ExpiryDate <= DateOnly.FromDateTime(DateTime.Today)) ? "Expired" :
                     (i.Quantity < 20) ? "Low Stock" : "Good"
        }).ToList();

        // 3. PROFITABILITY (Kept as is, but ensuring it fits the model)
        var profitability = rawSales
            .GroupBy(si => si.Product)
            .Select(g => {
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

        // 4. VOID LOGS
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
                ManagerName = v.Employee.EmployeeName,
                Reason = v.VoidReason,
                TotalAmount = v.Sales.TotalAmount
            }).ToListAsync();

        bool canViewFinancials = User.IsInRole("Owner") || User.IsInRole("Admin");

        var model = new ReportViewModel
        {
            StartDate = start,
            EndDate = end,

            // Sales Tab
            DetailedSales = detailedSales,
            TotalRevenue = detailedSales.Sum(x => x.Total),

            // Inventory Tab
            FullInventory = fullInventory,
            TotalAssetValue = canViewFinancials ? rawInventory.Sum(i => i.Quantity * i.CostPrice) : 0,

            // Profit Tab
            ProductProfitability = profitability,
            GrossProfit = canViewFinancials ? profitability.Sum(x => x.Profit) : 0,

            // Audit Tab
            VoidLogs = voids,

            GeneratedBy = User.Identity.Name,
            GeneratedAt = DateTime.Now,
            ShowFinancials = canViewFinancials
        };

        return View(model);
    }
}