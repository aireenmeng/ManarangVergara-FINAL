using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using System.Text.Json; // Needed for JSON serialization

namespace ManarangVergara.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly PharmacyDbContext _context;

        public HomeController(PharmacyDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var todayDateOnly = DateOnly.FromDateTime(today);
            var thirtyDaysFromNow = todayDateOnly.AddDays(30);
            int lowStockThreshold = 20;

            // 1. Existing KPI Logic (Keep this)
            var dailySales = await _context.Transactions
                .Where(t => t.SalesDate >= today && t.Status == "Completed")
                .ToListAsync();
            decimal dailyGrossProfit = dailySales.Sum(t => t.TotalAmount);

            var totalStockValue = await _context.Inventories.SumAsync(i => i.CostPrice * i.Quantity);
            var lowStockCount = await _context.Inventories.CountAsync(i => i.Quantity <= lowStockThreshold);
            var nearExpiryCount = await _context.Inventories.CountAsync(i => i.ExpiryDate <= thirtyDaysFromNow && i.ExpiryDate >= todayDateOnly);

            // 2. Existing Alerts & Recent Transactions Logic (Keep this)
            var rawAlerts = await _context.Inventories
                .Include(i => i.Product)
                .Where(i => i.Quantity <= lowStockThreshold || (i.ExpiryDate <= thirtyDaysFromNow && i.ExpiryDate >= todayDateOnly))
                .OrderBy(i => i.ExpiryDate).Take(10).ToListAsync();

            var alertList = rawAlerts.Select(i => new ProactiveAlertVM
            {
                ProductName = i.Product.Name,
                Quantity = i.Quantity,
                ExpiryDate = i.ExpiryDate,
                DaysLeft = i.ExpiryDate.DayNumber - todayDateOnly.DayNumber,
                AlertType = (i.Quantity <= lowStockThreshold && i.ExpiryDate <= thirtyDaysFromNow) ? "CRITICAL: BOTH" : (i.Quantity <= lowStockThreshold) ? "Low Stock" : "Near Expiry",
                Urgency = (i.Quantity == 0 || i.ExpiryDate <= todayDateOnly) ? "Critical" : "Warning"
            }).ToList();

            var recentTxns = await _context.Transactions
                .OrderByDescending(t => t.SalesDate).Take(8)
                .Select(t => new TransactionPreviewVM
                {
                    TransactionId = t.SalesId,
                    Date = t.SalesDate,
                    TotalAmount = t.TotalAmount,
                    PaymentMethod = t.PaymentMethod,
                    ItemCount = t.SalesItems.Sum(si => si.QuantitySold),
                    CashierName = "N/A"
                }).ToListAsync();

            // --- NEW: CHART DATA LOGIC (Phase 4) ---

            // A. Bar Chart: Sales Last 7 Days
            var sevenDaysAgo = today.AddDays(-6);
            var salesHistory = await _context.Transactions
                .Where(t => t.SalesDate >= sevenDaysAgo && t.Status == "Completed")
                .GroupBy(t => t.SalesDate.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.TotalAmount) })
                .ToListAsync();

            var barLabels = new string[7];
            var barData = new decimal[7];

            for (int i = 0; i < 7; i++)
            {
                var date = sevenDaysAgo.AddDays(i);
                barLabels[i] = date.ToString("MMM dd"); // e.g., "Nov 15"
                // Find total for this date, or 0 if no sales
                barData[i] = salesHistory.FirstOrDefault(s => s.Date == date)?.Total ?? 0;
            }

            // B. Pie Chart: Top 5 Categories by Volume
            var categoryStats = await _context.SalesItems
                .Include(si => si.Product).ThenInclude(p => p.Category)
                .GroupBy(si => si.Product.Category.CategoryName)
                .Select(g => new { Category = g.Key, Count = g.Sum(si => si.QuantitySold) })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            var pieLabels = categoryStats.Select(x => x.Category).ToArray();
            var pieData = categoryStats.Select(x => x.Count).ToArray();

            // -----------------------------

            var viewModel = new DashboardViewModel
            {
                DailyGrossProfit = dailyGrossProfit,
                TotalStockValue = totalStockValue,
                LowStockCount = lowStockCount,
                NearExpiryCount = nearExpiryCount,
                ProactiveAlerts = alertList,
                RecentTransactions = recentTxns,

                // Assign Chart Data
                BarChartLabels = barLabels,
                BarChartData = barData,
                PieChartLabels = pieLabels,
                PieChartData = pieData
            };

            return View(viewModel);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}