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
        [Range(0, double.MaxValue, ErrorMessage = "Harga kaga boleh minus, dongo!")] 
        public decimal Price { get; set; }

        [MaxLength(100)]
        public string ImageFileName { get; set; } = "";

        public DateTime Created { get; set; } = DateTime.Now;
    }
}