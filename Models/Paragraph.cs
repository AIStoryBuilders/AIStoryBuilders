#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class Paragraph
{
    public int Id { get; set; }

    public int Sequence { get; set; }

    public string ParagraphContent { get; set; }

    public Chapter Chapter { get; set; }

    public Location Location { get; set; } 

    public Timeline Timeline { get; set; }

    public List<Character> Characters { get; set; } = new List<Character>();
}