
#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class Location
{
    public int Id { get; set; }

    public int StoryId { get; set; }

    public string LocationName { get; set; }

    public string Description { get; set; }

    public virtual ICollection<ParagraphLocation> ParagraphLocation { get; set; } = new List<ParagraphLocation>();

    public virtual Story Story { get; set; }
}