#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class Location
{
    public int Id { get; set; }

    public string LocationName { get; set; }

    public List<LocationDescription> LocationDescription { get; set; }

    public Story Story { get; set; }
}