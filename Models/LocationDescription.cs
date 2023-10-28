#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class LocationDescription
{
    public int Id { get; set; }

    public string Description { get; set; }

    public Timeline Timeline { get; set; }
}