#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIStoryBuildersV2.Shared.Models;

public class AIPrompt
{
    public string AIPromptText { get; set; }
    public string NumberOfWords { get; set; }
    public string ModelId { get; set; }

    /// <summary>NEW in V2 (plan §6.5): run KnowledgeContextRetriever before writing (D15, D16).</summary>
    public bool SearchKnowledgeBase { get; set; }
}
