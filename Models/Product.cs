using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    public class Product
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Nama produk wajib diisi!")]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Kategori tidak boleh kosong!")]
        [MaxLength(100)]
        public string Category { get; set; } = "";

[Precision(16, 2)]
        [Required(ErrorMessage = "Harga harus diisi!")]
        [Range(0, double.MaxValue, ErrorMessage = "Harga tidak boleh minus.")] 
        public decimal Price { get; set; }

        [Precision(5, 2)]
        [Range(0, 100, ErrorMessage = "Diskon tidak boleh negatif atau lebih dari 100%")]
        public decimal? MemberDiscountPercentage { get; set; }

        [MaxLength(100)]
        public string ImageFileName { get; set; } = "";

        public bool IsAvailable { get; set; } = true;

        [Range(0, int.MaxValue, ErrorMessage = "Stok tidak boleh minus.")]
        public int Stock { get; set; } = 0;

        public DateTime Created { get; set; } = DateTime.Now;

        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}
