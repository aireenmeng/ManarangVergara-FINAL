using System;
using System.Collections.Generic;

namespace ManarangVergara.Models.Database;

public partial class Employee
{
    public int EmployeeId { get; set; }

    public string Username { get; set; } = null!;

    public string Password { get; set; } = null!;

    public string EmployeeName { get; set; } = null!;

    public string Position { get; set; } = null!;

    public string ContactInfo { get; set; } = null!;

    public virtual ICollection<ItemLog> ItemLogs { get; set; } = new List<ItemLog>();

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    public virtual ICollection<Void> Voids { get; set; } = new List<Void>();
}
