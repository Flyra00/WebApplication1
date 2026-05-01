using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace WebApplication1.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? FullName { get; set; }

        [NotMapped]
        public IList<string> Roles { get; set; } = new List<string>();

        public ICollection<TableSession> MemberTableSessions { get; set; } = new List<TableSession>();

        public ICollection<Order> CustomerOrders { get; set; } = new List<Order>();
    }
}
