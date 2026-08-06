namespace Scoutix.Models.DTOs
{
    public class UpdateLeadStatus
    {
        public int UserLeadId { get; set; }
        public int Status { get; set; }
        public string? Notes { get; set; }
        public DateTime? FollowUpDate { get; set; }
    }
}