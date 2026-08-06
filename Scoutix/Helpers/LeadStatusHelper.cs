using Scoutix.Models.Enums;

namespace Scoutix.Helpers
{
    public static class LeadStatusHelper
    {
        public static string GetStatusClass(LeadStatus status)
        {
            return status switch
            {
                LeadStatus.New => "gm-status-default",
                LeadStatus.Interested => "gm-status-interested",
                LeadStatus.FollowUp => "gm-status-followup",
                LeadStatus.CalledNoAnswer => "gm-status-noanswer",
                LeadStatus.NotInterested => "gm-status-notinterested",
                _ => "gm-status-default"
            };
        }
    }
}
