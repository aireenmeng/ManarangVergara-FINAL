using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ManarangVergara.Models;
using ManarangVergara.Models.Database;
using System.Globalization;

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

        public async Task<IActionResult> Index(string period = "7days")
        {
            var today = DateTime.Today; // 12:00:00 AM Today
            var tomorrow = today.AddDays(1); // 12:00:00 AM Tomorrow

            var todayDateOnly = DateOnly.FromDateTime(today);
            var thirtyDaysFromNow = todayDateOnly.AddDays(30);
            int lowStockThreshold = 20;

            // 1. KPI CARDS (FIXED: Now capped at Tomorrow's start time)
            // Old logic: t.SalesDate >= today (Included infinite future)
            // New logic: t.SalesDate >= today && t.SalesDate < tomorrow
            var dailySales = await _context.Transactions
                .Where(t => t.SalesDate >= today && t.SalesDate < tomorrow && t.Status == "Completed")
                .ToListAsync();

            decimal dailyGrossProfit = dailySales.Sum(t => t.TotalAmount);

            var totalStockValue = await _context.Inventories.SumAsync(i => i.CostPrice * i.Quantity);
            var lowStockCount = await _context.Inventories.CountAsync(i => i.Quantity <= lowStockThreshold);
            var nearExpiryCount = await _context.Inventories.CountAsync(i => i.ExpiryDate <= thirtyDaysFromNow && i.ExpiryDate >= todayDateOnly);

            // 2. ALERTS & RECENT TXNS
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

            // 3. CHART DATA (Unchanged)
            string[] barLabels;
            decimal[] barData;
            DateTime chartStartDate;

            if (period == "thisyear")
            {
                int currentYear = today.Year;
                chartStartDate = new DateTime(currentYear, 1, 1);

                var salesByMonth = await _context.Transactions
                    .Where(t => t.SalesDate >= chartStartDate && t.Status == "Completed")
                    .GroupBy(t => t.SalesDate.Month)
                    .Select(g => new { Month = g.Key, Total = g.Sum(t => t.TotalAmount) })
                    .ToListAsync();

                barLabels = DateTimeFormatInfo.CurrentInfo.AbbreviatedMonthNames.Take(12).ToArray();
                barData = new decimal[12];

                for (int i = 0; i < 12; i++)
                {
                    barData[i] = salesByMonth.FirstOrDefault(s => s.Month == i + 1)?.Total ?? 0;
                }
            }
            else
            {
                int daysToLoad = (period == "30days") ? 30 : 7;
                chartStartDate = (period == "30days") ? today.AddDays(-29) : today.AddDays(-6);

                var salesHistory = await _context.Transactions
                    .Where(t => t.SalesDate >= chartStartDate && t.Status == "Completed")
                    .GroupBy(t => t.SalesDate.Date)
                    .Select(g => new { Date = g.Key, Total = g.Sum(t => t.TotalAmount) })
                    .ToListAsync();

                barLabels = new string[daysToLoad];
                barData = new decimal[daysToLoad];

                for (int i = 0; i < daysToLoad; i++)
                {
                    var date = chartStartDate.AddDays(i);
                    barLabels[i] = date.ToString("MMM dd");
                    barData[i] = salesHistory.FirstOrDefault(s => s.Date == date)?.Total ?? 0;
                }
            }

            // Pie Chart
            var categoryStats = await _context.SalesItems
                .Include(si => si.Product).ThenInclude(p => p.Category)
                .Include(si => si.Sales)
                .Where(si => si.Sales.SalesDate >= chartStartDate && si.Sales.Status == "Completed")
                .GroupBy(si => si.Product.Category.CategoryName)
                .Select(g => new { Category = g.Key, Count = g.Sum(si => si.QuantitySold) })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            var viewModel = new DashboardViewModel
            {
                DailyGrossProfit = dailyGrossProfit,
                TotalStockValue = totalStockValue,
                LowStockCount = lowStockCount,
                NearExpiryCount = nearExpiryCount,
                ProactiveAlerts = alertList,
                RecentTransactions = recentTxns,
                BarChartLabels = barLabels,
                BarChartData = barData,
                PieChartLabels = categoryStats.Select(x => x.Category).ToArray(),
                PieChartData = categoryStats.Select(x => x.Count).ToArray()
            };

            ViewData["CurrentPeriod"] = period;

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