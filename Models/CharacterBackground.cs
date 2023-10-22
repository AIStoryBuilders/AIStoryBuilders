
#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class CharacterBackground
{
    public int Id { get; set; }

    public int Sequence { get; set; }

    public string Type { get; set; }

    public string Description { get; set; }

    public string VectorContent { get; set; }

    public virtual Character Character { get; set; }

    public virtual Timeline Timeline { get; set; }
}