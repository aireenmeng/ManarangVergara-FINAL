using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using System.Text.Json; // Needed for JSON serialization

namespace ManarangVergara.Controllers
{
    // ============================================================
    // HOME CONTROLLER (the dashboard)
    // ============================================================
    // this controller handles the main landing page of the application.
    // it gathers data from all over the database (sales, inventory, alerts)
    // to show the user a quick snapshot of how the business is doing.
    [Authorize]
    public class HomeController : Controller
    {
        private readonly PharmacyDbContext _context;

        // this is the constructor. it gets the database connection ready 
        // so we can ask questions to the database later.
        public HomeController(PharmacyDbContext context)
        {
            _context = context;
        }

        // ============================================================
        // 1. DASHBOARD LOGIC (main view)
        // ============================================================

        // this function runs when you visit the home page.
        // it calculates all the numbers, charts, and tables you see on the dashboard.
        public async Task<IActionResult> Index()
        {
            // setup some important dates to help us filter the data.
            // we need 'today' for daily sales and '30 days from now' to check for expiring meds.
            var today = DateTime.Today;
            var todayDateOnly = DateOnly.FromDateTime(today);
            var thirtyDaysFromNow = todayDateOnly.AddDays(30);
            int lowStockThreshold = 20; // items with less than this amount are considered "low stock"

            // ---------------------------------------------------------
            // 1. existing kpi logic (the big cards at the top)
            // ---------------------------------------------------------

            // calculate how much money we made today.
            // we only look at transactions that happened 'today' and are marked 'completed'.
            var dailySales = await _context.Transactions
                .Where(t => t.SalesDate >= today && t.Status == "Completed")
                .ToListAsync();
            decimal dailyGrossProfit = dailySales.Sum(t => t.TotalAmount);

            // calculate the total value of all items sitting on the shelves (cost * quantity).
            var totalStockValue = await _context.Inventories.SumAsync(i => i.CostPrice * i.Quantity);

            // count how many products are running low on stock.
            var lowStockCount = await _context.Inventories.CountAsync(i => i.Quantity <= lowStockThreshold);

            // count how many products will expire in the next 30 days.
            var nearExpiryCount = await _context.Inventories.CountAsync(i => i.ExpiryDate <= thirtyDaysFromNow && i.ExpiryDate >= todayDateOnly);

            // ---------------------------------------------------------
            // 2. proactive alerts logic (the warning table)
            // ---------------------------------------------------------

            // here we fetch the actual details of the problem items found above.
            // we ask the database for items that are either low stock OR near expiry.
            // we sort them by expiry date so the most urgent ones appear first.
            var rawAlerts = await _context.Inventories
                .Include(i => i.Product)
                .Where(i => i.Quantity <= lowStockThreshold || (i.ExpiryDate <= thirtyDaysFromNow && i.ExpiryDate >= todayDateOnly))
                .OrderBy(i => i.ExpiryDate).Take(10).ToListAsync();

            // we transform that raw data into a simple list designed for the dashboard view.
            // this includes calculating exactly how many days are left before expiration.
            var alertList = rawAlerts.Select(i => new ProactiveAlertVM
            {
                ProductName = i.Product.Name,
                Quantity = i.Quantity,
                ExpiryDate = i.ExpiryDate,
                DaysLeft = i.ExpiryDate.DayNumber - todayDateOnly.DayNumber,
                AlertType = (i.Quantity <= lowStockThreshold && i.ExpiryDate <= thirtyDaysFromNow) ? "CRITICAL: BOTH" : (i.Quantity <= lowStockThreshold) ? "Low Stock" : "Near Expiry",
                Urgency = (i.Quantity == 0 || i.ExpiryDate <= todayDateOnly) ? "Critical" : "Warning"
            }).ToList();

            // fetches the 8 most recent sales to show a live feed of activity on the right side.
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

            // ---------------------------------------------------------
            // 3. chart data logic (visualizations)
            // ---------------------------------------------------------

            // A. Bar Chart: Sales Last 7 Days
            // we look back 7 days to see the sales trend.
            var sevenDaysAgo = today.AddDays(-6);
            var salesHistory = await _context.Transactions
                .Where(t => t.SalesDate >= sevenDaysAgo && t.Status == "Completed")
                .GroupBy(t => t.SalesDate.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(t => t.TotalAmount) })
                .ToListAsync();

            // arrays to hold the data for the chart.
            var barLabels = new string[7];
            var barData = new decimal[7];

            // loop through the last 7 days. if we made sales on a specific day, record it.
            // if no sales happened (e.g., sunday), we put a '0' so the chart doesn't break.
            for (int i = 0; i < 7; i++)
            {
                var date = sevenDaysAgo.AddDays(i);
                barLabels[i] = date.ToString("MMM dd"); // e.g., "Nov 15"
                barData[i] = salesHistory.FirstOrDefault(s => s.Date == date)?.Total ?? 0;
            }

            // B. Pie Chart: Top 5 Categories by Volume
            // we count how many items were sold for each category (e.g., how many antibiotics vs vitamins).
            var categoryStats = await _context.SalesItems
                .Include(si => si.Product).ThenInclude(p => p.Category)
                .GroupBy(si => si.Product.Category.CategoryName)
                .Select(g => new { Category = g.Key, Count = g.Sum(si => si.QuantitySold) })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            // separate the names and the numbers into arrays for the chart tool.
            var pieLabels = categoryStats.Select(x => x.Category).ToArray();
            var pieData = categoryStats.Select(x => x.Count).ToArray();

            // -----------------------------

            // pack everything into the viewmodel to ship it to the webpage.
            var viewModel = new DashboardViewModel
            {
                DailyGrossProfit = dailyGrossProfit,
                TotalStockValue = totalStockValue,
                LowStockCount = lowStockCount,
                NearExpiryCount = nearExpiryCount,
                ProactiveAlerts = alertList,
                RecentTransactions = recentTxns,

                // assign chart data
                BarChartLabels = barLabels,
                BarChartData = barData,
                PieChartLabels = pieLabels,
                PieChartData = pieData
            };

            return View(viewModel);
        }

        // ============================================================
        // 4. OTHER PAGES
        // ============================================================

        public IActionResult Privacy()
        {
            return View();
        }

        // this handles unexpected crashes. it shows a friendly "oops" page 
        // instead of scary code to the user.
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}