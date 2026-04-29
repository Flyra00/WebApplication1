namespace WebApplication1.Models
{
    public class Ingredient
    {
        public int Id { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public int Qty { get; set; }
        public int MinimumStock { get; set; }
    }
}
