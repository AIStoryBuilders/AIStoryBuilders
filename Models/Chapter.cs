
#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class Chapter
{
    public int Id { get; set; }

    public string ChapterName { get; set; }

    public string Synopsis { get; set; }

    public int Sequence { get; set; }

    public List<Paragraph> Paragraph { get; set; } = new List<Paragraph>();

    public virtual Story Story { get; set; }
}