using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using System.Text.Json;

namespace ManarangVergara.Controllers
{
    // ============================================================
    // HOME CONTROLLER (dashboard)
    // ============================================================
    // serves as the landing page after login.
    // responsible for aggregating kpis, charts, and alerts for the dashboard view.
    [Authorize]
    public class HomeController : Controller
    {
        private readonly PharmacyDbContext _context;

        public HomeController(PharmacyDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 1. DASHBOARD INDEX
        // ============================================================
        // fetches real-time business metrics to display on the home screen.
        // calculates daily sales, low stock alerts, and prepares chart data.
        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var todayDateOnly = DateOnly.FromDateTime(today);
            var thirtyDaysFromNow = todayDateOnly.AddDays(30);
            int lowStockThreshold = 20;

            // 1. kpi logic: daily sales & gross profit
            // fetches all completed transactions for today to sum up the total revenue.
            var dailySales = await _context.Transactions
                .Where(t => t.SalesDate >= today && t.Status == "Completed")
                .ToListAsync();
            decimal dailyGrossProfit = dailySales.Sum(t => t.TotalAmount);

            // kpi logic: inventory health
            // calculates total asset value (cost * qty) and counts items needing attention.
            var totalStockValue = await _context.Inventories.SumAsync(i => i.CostPrice * i.Quantity);
            var lowStockCount = await _context.Inventories.CountAsync(i => i.Quantity <= lowStockThreshold);
            var nearExpiryCount = await _context.Inventories.CountAsync(i => i.ExpiryDate <= thirtyDaysFromNow && i.ExpiryDate >= todayDateOnly);

            // 2. proactive alerts logic
            // fetches the specific items that are low stock or near expiry to show in a table.
            var rawAlerts = await _context.Inventories
                .Include(i => i.Product)
                .Where(i => i.Quantity <= lowStockThreshold || (i.ExpiryDate <= thirtyDaysFromNow && i.ExpiryDate >= todayDateOnly))
                .OrderBy(i => i.ExpiryDate).Take(10).ToListAsync();

            // maps the raw data to a viewmodel designed for the alert table ui.
            var alertList = rawAlerts.Select(i => new ProactiveAlertVM
            {
                ProductName = i.Product.Name,
                Quantity = i.Quantity,
                ExpiryDate = i.ExpiryDate,
                DaysLeft = i.ExpiryDate.DayNumber - todayDateOnly.DayNumber,
                AlertType = (i.Quantity <= lowStockThreshold && i.ExpiryDate <= thirtyDaysFromNow) ? "CRITICAL: BOTH" : (i.Quantity <= lowStockThreshold) ? "Low Stock" : "Near Expiry",
                Urgency = (i.Quantity == 0 || i.ExpiryDate <= todayDateOnly) ? "Critical" : "Warning"
            }).ToList();

            // fetches the 8 most recent transactions for the "recent sales" feed.
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

            // --- new: chart data logic (visualizations) ---

            // a. bar chart: sales last 7 days
            // prepares data for the bar chart by grouping sales by date.
            var sevenDaysAgo = today.AddDays(-6);
            var salesHistory = await _context.Transactions
                .Where(t => t.SalesDate >= sevenDaysAgo && t.Status == "Completed")
                .GroupBy(t => t.SalesDate.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.TotalAmount) })
                .ToListAsync();

            var barLabels = new string[7];
            var barData = new decimal[7];

            // fills in the array for the last 7 days, ensuring 0s for days with no sales.
            for (int i = 0; i < 7; i++)
            {
                var date = sevenDaysAgo.AddDays(i);
                barLabels[i] = date.ToString("MMM dd"); // e.g., "Nov 15"
                barData[i] = salesHistory.FirstOrDefault(s => s.Date == date)?.Total ?? 0;
            }

            // b. pie chart: top 5 categories by volume
            // groups sales items by category to see what type of products sell the most.
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

            // constructs the final viewmodel with all the aggregated data.
            var viewModel = new DashboardViewModel
            {
                DailyGrossProfit = dailyGrossProfit,
                TotalStockValue = totalStockValue,
                LowStockCount = lowStockCount,
                NearExpiryCount = nearExpiryCount,
                ProactiveAlerts = alertList,
                RecentTransactions = recentTxns,

                // assign chart data for javascript to consume
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

        // handles errors in production environment
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}