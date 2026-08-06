using System;
using System.Collections.Generic;

namespace Scoutix.Models;

public partial class WebhookLog
{
    public int Id { get; set; }

    public string PaddleEventId { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public string Payload { get; set; } = null!;

    public bool ProcessedSuccessfully { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime ReceivedAt { get; set; }
}
