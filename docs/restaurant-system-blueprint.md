# Restaurant System Blueprint

Dokumen ini merangkum kondisi project saat ini, target arsitektur yang disarankan, dan roadmap implementasi agar pengembangan berikutnya konsisten.

## 1. Ringkasan Kondisi Saat Ini

Project saat ini adalah aplikasi ASP.NET Core MVC `.NET 8` dengan:

- `Entity Framework Core` untuk akses database SQL Server
- `ASP.NET Identity` untuk autentikasi dan role
- area public untuk customer
- area admin untuk master data restoran

### Modul yang sudah tersedia

- autentikasi dasar dan role seeding
- manajemen user
- manajemen menu
- manajemen meja
- manajemen bahan mentah
- manajemen inventory
- tampilan website customer
- login/register customer berbasis modal AJAX

### Modul yang belum selesai

- sesi meja
- order dan order item
- pembayaran
- kasir workflow
- dapur workflow
- owner reporting
- damage report
- member profile, point, dan level
- QR meja dan QRIS

## 2. Role Sistem Target

### Admin

- full access
- kelola user
- kelola role
- kelola master data
- kelola konfigurasi operasional

### Supervisor

- input kebutuhan dapur
- input fasilitas restoran
- memantau kebutuhan harian
- laporan kerusakan fasilitas

### Kasir

- input pesanan offline
- proses pembayaran tunai
- cetak struk kasir
- lihat transaksi hari ini

### Bagian Masak

- lihat pesanan masuk
- lihat detail pesanan per meja
- ubah status pesanan dapur
- cetak tiket dapur

### Owner

- lihat pendapatan
- lihat laporan penjualan
- lihat stok bahan dan inventory
- lihat data barang rusak atau hilang

### Customer

- scan QR meja
- lihat menu
- buat pesanan
- bayar online atau lewat kasir
- login sebagai member

## 3. Prinsip Desain Yang Disarankan

### 3.1 Pisahkan master data dan transaksi

Master data mencakup entitas yang relatif stabil:

- `Product`
- `Table`
- `Ingredient`
- `InventoryItem`
- `ApplicationUser`

Transaksi harus dipisahkan ke entitas yang merekam kejadian bisnis:

- `TableSession`
- `Order`
- `OrderItem`
- `Payment`
- `DamageReport`
- `StockMovement`

### 3.2 Status meja jangan disimpan sebagai string tunggal

Status meja sebaiknya diturunkan dari session aktif.

- jika ada `TableSession` aktif, meja dianggap terisi
- jika tidak ada `TableSession` aktif, meja dianggap kosong

Dengan pola ini, histori okupansi meja tetap tersimpan.

### 3.3 Customer member tetap gunakan Identity

Best practice untuk project ini adalah tetap memakai `ApplicationUser` sebagai identitas login, lalu menambahkan data member di entitas terpisah atau properti tambahan.

Pendekatan yang direkomendasikan:

- `ApplicationUser` untuk akun login dan role
- `MemberProfile` untuk data loyalti customer

## 4. Entitas Final Yang Direkomendasikan

## ApplicationUser

- `Id`
- `FullName`
- `UserName`
- `Email`
- `IsActive`
- `CreatedAt`

Catatan:

- role tetap dikelola oleh Identity
- user internal memakai role seperti `Admin`, `Supervisor`, `Kasir`, `Owner`, `Kitchen`
- customer login memakai role `Customer`

## MemberProfile

- `Id`
- `UserId`
- `Phone`
- `Level`
- `Point`
- `JoinedAt`

Relasi:

- satu `ApplicationUser` customer dapat memiliki satu `MemberProfile`

## Table

- `Id`
- `Number`
- `Capacity`
- `QrCodeToken`
- `IsActive`

## TableSession

- `Id`
- `TableId`
- `SessionCode`
- `GuestType`
- `MemberUserId`
- `StartTime`
- `EndTime`
- `Status`

Catatan:

- `GuestType` dapat berupa `Guest` atau `Member`
- `Status` dapat berupa `Open`, `Closed`, `Cancelled`

## Product

- `Id`
- `Name`
- `Category`
- `Price`
- `ImageFileName`
- `IsAvailable`
- `Stock`
- `CreatedAt`

Catatan:

- jika stok menu perlu sederhana, field `Stock` cukup di `Product`
- jika stok menu diturunkan dari resep bahan, field `Stock` bisa dihilangkan dan dihitung dari bahan

## Order

- `Id`
- `TableSessionId`
- `OrderNumber`
- `OrderDate`
- `Status`
- `OrderSource`
- `Subtotal`
- `DiscountAmount`
- `TaxAmount`
- `ServiceAmount`
- `Total`
- `CreatedByUserId`

Catatan:

- `OrderSource` dapat berupa `CustomerQr`, `CashierOffline`
- `Status` dapat berupa `Draft`, `Submitted`, `Processing`, `Completed`, `Cancelled`, `Paid`

## OrderItem

- `Id`
- `OrderId`
- `ProductId`
- `Qty`
- `UnitPrice`
- `LineTotal`
- `KitchenStatus`
- `Note`

Catatan:

- `KitchenStatus` dapat berupa `Queued`, `Cooking`, `Ready`, `Served`

## Payment

- `Id`
- `OrderId`
- `Method`
- `Amount`
- `PaymentDate`
- `Status`
- `ReferenceNumber`
- `PaidByUserId`

Catatan:

- `Method` dapat berupa `Cash`, `QRIS`, `Transfer`
- `Status` dapat berupa `Pending`, `Paid`, `Failed`, `Refunded`

## Ingredient

- `Id`
- `ItemName`
- `Unit`
- `Qty`
- `MinimumStock`

## InventoryItem

- `Id`
- `ItemName`
- `Category`
- `Qty`
- `Condition`
- `IsBreakable`

## DamageReport

- `Id`
- `InventoryItemId`
- `Qty`
- `Description`
- `ReportedByUserId`
- `ReportDate`
- `Status`

## StockMovement

- `Id`
- `ItemType`
- `ReferenceId`
- `MovementType`
- `Qty`
- `Note`
- `CreatedByUserId`
- `CreatedAt`

Catatan:

- `ItemType` dapat membedakan `Ingredient` dan `InventoryItem`
- `MovementType` dapat berupa `In`, `Out`, `Adjustment`, `Waste`

## 5. Relasi Utama

- `ApplicationUser` 1..1 `MemberProfile`
- `Table` 1..n `TableSession`
- `TableSession` 1..n `Order`
- `Order` 1..n `OrderItem`
- `Order` 1..n `Payment`
- `Product` 1..n `OrderItem`
- `InventoryItem` 1..n `DamageReport`

## 6. Pembagian Controller Yang Disarankan

### Public / Customer

- `HomeController`
  - landing page
  - menu customer
- `AuthController`
  - login
  - register
  - logout
- `TableSessionController`
  - start session dari QR meja
  - validasi meja aktif
- `CustomerOrderController`
  - tambah item ke order
  - lihat cart
  - submit order
- `CustomerPaymentController`
  - payment online
  - payment status

### Back Office / Internal

- `AdminController`
  - dashboard utama admin
- `UserController`
  - user management
- `RoleController`
  - role management jika nanti diperlukan UI khusus
- `ProductsController`
  - manajemen menu
- `TableController`
  - manajemen meja
- `IngredientController`
  - stok bahan
- `InventoryController`
  - inventaris barang
- `DamageReportController`
  - laporan barang rusak

### Operasional

- `CashierController`
  - transaksi kasir
  - pembayaran offline
  - cetak struk
- `KitchenController`
  - daftar pesanan dapur
  - update status masak
  - print tiket dapur
- `SupervisorController`
  - kebutuhan dapur
  - monitoring fasilitas
- `OwnerController`
  - laporan dan dashboard owner

## 7. Area View Yang Disarankan

Pisahkan layout agar akses dan UI lebih jelas.

- `Views/Shared/_Layout.cshtml` untuk public site
- `Views/Shared/_LayoutAdmin.cshtml` untuk admin/back office
- `Views/Shared/_LayoutOperational.cshtml` untuk kasir dan dapur jika kebutuhan UI berbeda
- `Views/Shared/_LayoutOwner.cshtml` untuk owner jika fokusnya dashboard laporan

## 8. Struktur Folder Yang Disarankan

```text
Controllers/
  AdminController.cs
  AuthController.cs
  CashierController.cs
  CustomerOrderController.cs
  CustomerPaymentController.cs
  DamageReportController.cs
  HomeController.cs
  IngredientController.cs
  InventoryController.cs
  KitchenController.cs
  OwnerController.cs
  ProductsController.cs
  SupervisorController.cs
  TableController.cs
  TableSessionController.cs
  UserController.cs

Data/
  AppDbContext.cs
  IdentitySeeder.cs

Models/
  ApplicationUser.cs
  DamageReport.cs
  Ingredient.cs
  InventoryItem.cs
  MemberProfile.cs
  Order.cs
  OrderItem.cs
  Payment.cs
  Product.cs
  StockMovement.cs
  Table.cs
  TableSession.cs

Views/
  Admin/
  Auth/
  Cashier/
  Home/
  Ingredient/
  Inventory/
  Kitchen/
  Owner/
  Products/
  Supervisor/
  Table/
  TableSession/
  User/
  Shared/
```

## 9. Roadmap Implementasi Bertahap

### Fase 1 - Rapikan Fondasi Yang Sudah Ada

- rapikan bug view dan route yang sudah jelas
- sinkronkan nama model dengan domain bisnis
- tambah missing view atau redirect yang aman
- ganti `EnsureCreated()` ke `Migrate()` saat skema sudah stabil

### Fase 2 - Bangun Domain Transaksi Minimum

- tambah `TableSession`
- aktifkan `Order`
- tambah `OrderItem`
- hubungkan order ke customer dan meja
- buat alur submit pesanan customer

### Fase 3 - Bangun Operasional Restoran

- buat modul kasir
- buat modul dapur
- tambah print ticket kasir dan dapur
- tambah status order dan kitchen status

### Fase 4 - Bangun Pembayaran

- tambah entitas `Payment`
- dukung pembayaran tunai
- siapkan struktur untuk QRIS
- tandai order `Paid` setelah pembayaran berhasil

### Fase 5 - Bangun Monitoring dan Laporan

- dashboard owner
- laporan penjualan
- laporan stok
- laporan kerusakan barang

### Fase 6 - Bangun Fitur Member Lanjutan

- `MemberProfile`
- point dan level
- histori transaksi customer

## 10. Keputusan Implementasi Yang Disarankan Untuk Project Ini

Untuk menjaga perubahan tetap bertahap dan aman, implementasi berikut paling disarankan:

1. Pertahankan `ApplicationUser` sebagai basis semua role.
2. Tambahkan entitas transaksi baru tanpa mengubah drastis modul master data yang sudah jalan.
3. Gunakan `TableSession` sebagai pusat okupansi meja.
4. Hubungkan semua transaksi ke `Order` dan `OrderItem`.
5. Pisahkan role internal operasional dari role customer.
6. Bangun pembayaran tunai terlebih dahulu sebelum QRIS penuh.

## 11. Prioritas Teknis Berikutnya

Jika melanjutkan implementasi dari project saat ini, urutan terbaik adalah:

1. perbaiki bug kecil yang sudah jelas
2. buat `TableSession`
3. hidupkan `Order` dan `OrderItem`
4. buat halaman customer order
5. buat halaman kasir dan dapur
6. tambah `Payment`
7. tambah laporan owner
