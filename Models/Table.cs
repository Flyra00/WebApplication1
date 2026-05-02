using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    [Index(nameof(Number), IsUnique = true)]
    [Index(nameof(QrCodeToken), IsUnique = true)]
    public class Table
    {
        public int Id { get; set; }
        [Required]
        public int Number { get; set; }
        [Required]
        [Range (1,20, ErrorMessage ="Kapasitas meja tidak bisa melebihi ini")]
        public int Capacity { get; set; }

        [MaxLength(64)]
        public string QrCodeToken { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public ICollection<TableSession> Sessions { get; set; } = new List<TableSession>();

    }
}
