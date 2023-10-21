
#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class ParagraphVectorData
{
    public int Id { get; set; }

    public int ParagraphId { get; set; }

    public int VectorValueId { get; set; }

    public double VectorValue { get; set; }

    public virtual Paragraph Paragraph { get; set; }
}