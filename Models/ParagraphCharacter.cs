
#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class ParagraphCharacter
{
    public int Id { get; set; }

    public int ParagraphId { get; set; }

    public int CharacterId { get; set; }

    public virtual Character Character { get; set; }

    public virtual Paragraph Paragraph { get; set; }
}