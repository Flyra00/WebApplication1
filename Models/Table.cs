using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace WebApplication1.Models
{
    [Index(nameof(Number), IsUnique = true)]
    public class Table
    {
        public int Id { get; set; }
        [Required]
        public int Number { get; set; }
        [Required]
        [Range (1,20, ErrorMessage ="Kapasitas meja tidak bisa melebihi ini")]
        public int Capacity { get; set; }

        public ICollection<TableSession> Sessions { get; set; } = new List<TableSession>();

    }
}
