#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuildersV2.Shared.Models;
public partial class Timeline
{
    public int Id { get; set; }

    public string TimelineName { get; set; }

    public string TimelineDescription { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? StopDate { get; set; }

    public Story Story { get; set; }
}
