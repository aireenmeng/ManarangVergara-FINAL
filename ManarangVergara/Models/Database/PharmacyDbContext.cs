using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Scaffolding.Internal;

namespace ManarangVergara.Models.Database;

public partial class PharmacyDbContext : DbContext
{
    public PharmacyDbContext()
    {
    }

    public PharmacyDbContext(DbContextOptions<PharmacyDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<Inventory> Inventories { get; set; }

    public virtual DbSet<ItemLog> ItemLogs { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductCategory> ProductCategories { get; set; }

    public virtual DbSet<PurchaseOrder> PurchaseOrders { get; set; }

    public virtual DbSet<SalesItem> SalesItems { get; set; }

    public virtual DbSet<Supplier> Suppliers { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    public virtual DbSet<Void> Voids { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseMySql("server=127.0.0.1;port=3306;database=pharmacy_db;user=root", Microsoft.EntityFrameworkCore.ServerVersion.Parse("10.4.32-mariadb"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_general_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.EmployeeId).HasName("PRIMARY");

            entity
                .ToTable("employees")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.HasIndex(e => e.Username, "Username").IsUnique();

            entity.Property(e => e.EmployeeId)
                .HasColumnType("int(11)")
                .HasColumnName("Employee_ID");
            entity.Property(e => e.ContactInfo)
                .HasMaxLength(100)
                .HasColumnName("Contact_Info");
            entity.Property(e => e.EmployeeName)
                .HasMaxLength(100)
                .HasColumnName("Employee_Name");
            entity.Property(e => e.Password).HasMaxLength(255);
            entity.Property(e => e.Position).HasColumnType("enum('Admin','Manager','Cashier','')");
            entity.Property(e => e.Username).HasMaxLength(50);
        });

        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.HasKey(e => e.InventoryId).HasName("PRIMARY");

            entity
                .ToTable("inventory")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.HasIndex(e => e.ProductId, "Product_ID");

            entity.Property(e => e.InventoryId)
                .HasColumnType("int(11)")
                .HasColumnName("Inventory_ID");
            entity.Property(e => e.BatchNumber)
                .HasMaxLength(50)
                .HasColumnName("Batch_Number");
            entity.Property(e => e.CostPrice)
                .HasPrecision(10, 2)
                .HasColumnName("Cost_Price");
            entity.Property(e => e.ExpiryDate).HasColumnName("Expiry_Date");
            entity.Property(e => e.IsExpired)
                .HasColumnType("tinyint(4)")
                .HasColumnName("Is_Expired");
            entity.Property(e => e.LastUpdated)
                .HasColumnType("datetime")
                .HasColumnName("Last_Updated");
            entity.Property(e => e.ProductId)
                .HasColumnType("int(11)")
                .HasColumnName("Product_ID");
            entity.Property(e => e.Quantity).HasColumnType("int(11)");
            entity.Property(e => e.SellingPrice)
                .HasPrecision(10, 2)
                .HasColumnName("Selling_Price");

            entity.HasOne(d => d.Product).WithMany(p => p.Inventories)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("inventory_ibfk_1");
        });

        modelBuilder.Entity<ItemLog>(entity =>
        {
            entity.HasKey(e => e.LogId).HasName("PRIMARY");

            entity
                .ToTable("item_logs")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.HasIndex(e => e.EmployeeId, "Employee_ID");

            entity.HasIndex(e => e.ProductId, "Product_ID");

            entity.Property(e => e.LogId)
                .HasColumnType("int(11)")
                .HasColumnName("Log_ID");
            entity.Property(e => e.Action).HasColumnType("enum('Added','Removed','Sold','Updated','Voided')");
            entity.Property(e => e.EmployeeId)
                .HasColumnType("int(11)")
                .HasColumnName("Employee_ID");
            entity.Property(e => e.LogReason)
                .HasColumnType("text")
                .HasColumnName("Log_Reason");
            entity.Property(e => e.LoggedAt)
                .HasColumnType("datetime")
                .HasColumnName("Logged_At");
            entity.Property(e => e.ProductId)
                .HasColumnType("int(11)")
                .HasColumnName("Product_ID");
            entity.Property(e => e.Quantity).HasColumnType("int(11)");

            entity.HasOne(d => d.Employee).WithMany(p => p.ItemLogs)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("item_logs_ibfk_1");

            entity.HasOne(d => d.Product).WithMany(p => p.ItemLogs)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("item_logs_ibfk_2");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.ProductId).HasName("PRIMARY");

            entity
                .ToTable("products")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.HasIndex(e => e.CategoryId, "Category_ID");

            entity.HasIndex(e => e.SupplierId, "Supplier_ID");

            entity.Property(e => e.ProductId)
                .HasColumnType("int(11)")
                .HasColumnName("Product_ID");
            entity.Property(e => e.CategoryId)
                .HasColumnType("int(11)")
                .HasColumnName("Category_ID");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasColumnName("Created_At");
            entity.Property(e => e.IsActive)
                .HasDefaultValueSql("b'1'")
                .HasColumnType("bit(1)");
            entity.Property(e => e.Manufacturer).HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.SupplierId)
                .HasColumnType("int(11)")
                .HasColumnName("Supplier_ID");

            entity.HasOne(d => d.Category).WithMany(p => p.Products)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("products_ibfk_1");

            entity.HasOne(d => d.Supplier).WithMany(p => p.Products)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("products_ibfk_2");
        });

        modelBuilder.Entity<ProductCategory>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PRIMARY");

            entity
                .ToTable("product_categories")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.CategoryId)
                .HasColumnType("int(11)")
                .HasColumnName("Category_ID");
            entity.Property(e => e.CategoryName)
                .HasMaxLength(100)
                .HasColumnName("Category_Name");
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasKey(e => e.PoId).HasName("PRIMARY");

            entity
                .ToTable("purchase_orders")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.HasIndex(e => e.ProductId, "Product_ID");

            entity.HasIndex(e => e.SupplierId, "Supplier_ID");

            entity.Property(e => e.PoId)
                .HasColumnType("int(11)")
                .HasColumnName("PO_ID");
            entity.Property(e => e.DateReceived)
                .HasColumnType("datetime")
                .HasColumnName("Date_Received");
            entity.Property(e => e.ProductId)
                .HasColumnType("int(11)")
                .HasColumnName("Product_ID");
            entity.Property(e => e.QuantityReceIved)
                .HasColumnType("int(11)")
                .HasColumnName("Quantity_ReceIved");
            entity.Property(e => e.SupplierId)
                .HasColumnType("int(11)")
                .HasColumnName("Supplier_ID");

            entity.HasOne(d => d.Product).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("purchase_orders_ibfk_1");

            entity.HasOne(d => d.Supplier).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("purchase_orders_ibfk_2");
        });

        modelBuilder.Entity<SalesItem>(entity =>
        {
            entity.HasKey(e => e.SalesItemId).HasName("PRIMARY");

            entity
                .ToTable("sales_items")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.HasIndex(e => e.ProductId, "Product_ID");

            entity.HasIndex(e => e.SalesId, "Sales_ID");

            entity.Property(e => e.SalesItemId)
                .HasColumnType("int(11)")
                .HasColumnName("Sales_Item_ID");
            entity.Property(e => e.Discount).HasPrecision(10, 2);
            entity.Property(e => e.Price).HasPrecision(10, 2);
            entity.Property(e => e.ProductId)
                .HasColumnType("int(11)")
                .HasColumnName("Product_ID");
            entity.Property(e => e.QuantitySold)
                .HasColumnType("int(11)")
                .HasColumnName("Quantity_Sold");
            entity.Property(e => e.SalesId)
                .HasColumnType("int(11)")
                .HasColumnName("Sales_ID");

            entity.HasOne(d => d.Product).WithMany(p => p.SalesItems)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("sales_items_ibfk_2");

            entity.HasOne(d => d.Sales).WithMany(p => p.SalesItems)
                .HasForeignKey(d => d.SalesId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("sales_items_ibfk_1");
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasKey(e => e.SupplierId).HasName("PRIMARY");

            entity
                .ToTable("suppliers")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.SupplierId)
                .HasColumnType("int(11)")
                .HasColumnName("Supplier_ID");
            entity.Property(e => e.ContactInfo)
                .HasMaxLength(100)
                .HasColumnName("Contact_Info");
            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.SalesId).HasName("PRIMARY");

            entity
                .ToTable("transactions")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.HasIndex(e => e.EmployeeId, "fk_transactions_employees");

            entity.Property(e => e.SalesId)
                .HasColumnType("int(11)")
                .HasColumnName("Sales_ID");
            entity.Property(e => e.EmployeeId)
                .HasColumnType("int(11)")
                .HasColumnName("Employee_ID");
            entity.Property(e => e.PaymentMethod)
                .HasColumnType("enum('Cash','Gcash','PayMaya')")
                .HasColumnName("Payment_Method");
            entity.Property(e => e.SalesDate)
                .HasColumnType("datetime")
                .HasColumnName("Sales_Date");
            entity.Property(e => e.Status).HasColumnType("enum('Completed','Refunded','Pending')");
            entity.Property(e => e.TotalAmount)
                .HasPrecision(10, 2)
                .HasColumnName("Total_Amount");

            entity.HasOne(d => d.Employee).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_transactions_employees");
        });

        modelBuilder.Entity<Void>(entity =>
        {
            entity.HasKey(e => e.VoidId).HasName("PRIMARY");

            entity
                .ToTable("void")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.HasIndex(e => e.EmployeeId, "Employee_ID");

            entity.HasIndex(e => e.SalesId, "Sales_ID");

            entity.Property(e => e.VoidId)
                .HasColumnType("int(11)")
                .HasColumnName("Void_ID");
            entity.Property(e => e.EmployeeId)
                .HasColumnType("int(11)")
                .HasColumnName("Employee_ID");
            entity.Property(e => e.SalesId)
                .HasColumnType("int(11)")
                .HasColumnName("Sales_ID");
            entity.Property(e => e.VoidReason)
                .HasColumnType("text")
                .HasColumnName("Void_Reason");
            entity.Property(e => e.VoidedAt)
                .HasColumnType("datetime")
                .HasColumnName("Voided_At");

            entity.HasOne(d => d.Employee).WithMany(p => p.Voids)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("void_ibfk_1");

            entity.HasOne(d => d.Sales).WithMany(p => p.Voids)
                .HasForeignKey(d => d.SalesId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("void_ibfk_2");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
