using Microsoft.Extensions.AI;

namespace AIStoryBuilders.Models;

// ── Session & Display ──────────────────────────────────────

public class ConversationSession
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();
}

public class ChatDisplayMessage
{
    public bool IsUser { get; set; }
    public string Content { get; set; } = "";
}

// ── Read-Tool DTOs ─────────────────────────────────────────

public class CharacterDto
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Backstory { get; set; } = "";
}

public class LocationDto
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public class TimelineDto
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
}

public class ChapterDto
{
    public string Title { get; set; } = "";
    public string BeatsSummary { get; set; } = "";
    public int ParagraphCount { get; set; }
}

public class ParagraphDto
{
    public string Text { get; set; } = "";
    public string Location { get; set; } = "";
    public string Timeline { get; set; } = "";
    public List<string> Characters { get; set; } = new();
}

public class RelationshipDto
{
    public string RelatedTo { get; set; } = "";
    public string EdgeLabel { get; set; } = "";
}

public class AppearanceDto
{
    public string Chapter { get; set; } = "";
    public int ParagraphIndex { get; set; }
}

public class LocationUsageDto
{
    public string Chapter { get; set; } = "";
    public List<string> Characters { get; set; } = new();
}

public class InteractionDto
{
    public string InteractsWith { get; set; } = "";
    public List<string> Chapters { get; set; } = new();
}

public class OrphanDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
}

public class ArcStepDto
{
    public string Chapter { get; set; } = "";
    public string Location { get; set; } = "";
    public string Timeline { get; set; } = "";
    public List<string> CoCharacters { get; set; } = new();
}

public class LocationEventDto
{
    public string Chapter { get; set; } = "";
    public string Timeline { get; set; } = "";
    public List<string> Characters { get; set; } = new();
}

public class GraphSummaryDto
{
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
}

// ── Write-Tool DTO ─────────────────────────────────────────

public class MutationResult
{
    public bool Success { get; set; }
    public bool IsPreview { get; set; }
    public string Operation { get; set; } = "";
    public string Entity { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Error { get; set; }
    public List<string> AffectedFiles { get; set; } = new();
    public bool EmbeddingsUpdated { get; set; }
    public bool GraphRefreshed { get; set; }
}
