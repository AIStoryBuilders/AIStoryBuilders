#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class Character
{
    public int Id { get; set; }

    public int StoryId { get; set; }

    public string CharacterName { get; set; }

    public string Description { get; set; }

    public string Goals { get; set; }

    public virtual ICollection<CharacterBackground> CharacterBackground { get; set; } = new List<CharacterBackground>();

    public virtual Story Story { get; set; }
}