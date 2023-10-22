#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class Character
{
    public int Id { get; set; }

    public int StoryId { get; set; }

    public string CharacterName { get; set; }

    public Story Story { get; set; }

    public List<CharacterBackground> CharacterBackground { get; set; } = new List<CharacterBackground>();

}