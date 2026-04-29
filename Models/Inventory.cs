namespace WebApplication1.Models
{
    public class Inventory
    {
        public int Id { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Qty { get; set; }
        public string Condition { get; set; } = string.Empty;
    }
}
