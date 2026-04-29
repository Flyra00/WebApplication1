using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models
{
    public class Order
    {
        public int Id { get; set; }
        [Required]
        public int TableId { get; set; }
        public int? MemberId { get; set; }
        [Required]
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }   

        public virtual Table Table { get; set; }
    }
}
