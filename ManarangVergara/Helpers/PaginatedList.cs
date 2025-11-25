using System;
using System.Collections.Generic;
using System.Linq;

namespace ManarangVergara.Helpers
{
    // HELPER CLASS: MANAGES LIST PAGES
    // this tool takes a massive list (like 1000 sales) and chops it into small chunks (pages)
    // so the website doesn't crash trying to load everything at once.
    public class PaginatedList<T> : List<T>
    {
        public int PageIndex { get; private set; } // which page are we on right now? (e.g., page 2)
        public int TotalPages { get; private set; } // how many pages are there in total?

        // CONSTRUCTOR: SETS UP THE MATH
        public PaginatedList(List<T> items, int count, int pageIndex, int pageSize)
        {
            PageIndex = pageIndex;
            // calculates total pages. if we have 105 items and page size is 10, we need 11 pages.
            // math.ceiling rounds up any decimals.
            TotalPages = (int)Math.Ceiling(count / (double)pageSize);

            this.AddRange(items); // adds the actual data for this specific page to the list
        }

        // checks if we should show the "previous" arrow button
        public bool HasPreviousPage => PageIndex > 1;

        // checks if we should show the "next" arrow button
        public bool HasNextPage => PageIndex < TotalPages;

        // MAIN FUNCTION: SLICES THE DATABASE QUERY
        // this is the engine that actually talks to the database
        public static PaginatedList<T> Create(IQueryable<T> source, int pageIndex, int pageSize)
        {
            var count = source.Count(); // counts total items in the database first

            // MATH LOGIC:
            // if we are on page 3 and size is 10:
            // "skip" ((3-1) * 10) = skip the first 20 items.
            // "take" (10) = grab the next 10 items.
            var items = source.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();

            return new PaginatedList<T>(items, count, pageIndex, pageSize);
        }
    }
}