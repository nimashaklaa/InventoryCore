namespace InventoryCore.Models
{
    public class Association
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();
        public Guid SourceProductId { get; set; }
        public Product? SourceProduct { get; set; }
        public Guid TargetProductId { get; set; }
        public AssociationType AssociationType { get; set; }
        public decimal Score { get; set; }=0.5m;
        public DateTimeOffset ValidSince { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? ValidUntil { get; set; }
    }
}