using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    public class OrderItem
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public int ProductId { get; set; }

        [Range(1, int.MaxValue)]
        public int Qty { get; set; }

        [Precision(16, 2)]
        public decimal UnitPrice { get; set; }

        [Precision(16, 2)]
        public decimal LineTotal { get; set; }

        [MaxLength(200)]
        public string? Note { get; set; }

        public Order Order { get; set; } = null!;

        public Product Product { get; set; } = null!;
    }
}
