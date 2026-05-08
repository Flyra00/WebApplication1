namespace WebApplication1.ViewModels.TrackOrder
{
    public sealed class TrackOrderViewModel
    {
        public string OrderNumber { get; set; } = string.Empty;

        public string OrderStatus { get; set; } = string.Empty;

        public string OrderStatusLabel { get; set; } = string.Empty;

        public int ProgressPercent { get; set; }

        public string PaymentStatusLabel { get; set; } = string.Empty;

        public DateTime OrderDate { get; set; }

        public List<TrackOrderItemViewModel> Items { get; set; } = new();
    }

    public sealed class TrackOrderItemViewModel
    {
        public string ProductName { get; set; } = string.Empty;

        public int Qty { get; set; }

        public string KitchenStatus { get; set; } = string.Empty;

        public string KitchenStatusLabel { get; set; } = string.Empty;
    }
}
