using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Devices.Sensors;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        #region public async Task<JSONMasterStory> CreateMasterStory(Chapter objChapter, Paragraph objParagraph, List<Models.Character> colCharacter, List<Paragraph> colParagraphs, AIPrompt AIPromptResult)
        public async Task<JSONMasterStory> CreateMasterStory(Chapter objChapter, Paragraph objParagraph, List<Models.Character> colCharacter, List<Paragraph> colParagraphs, AIPrompt AIPromptResult)
        {
            JSONMasterStory objMasterStory = new JSONMasterStory();

            objMasterStory.StoryTitle = objChapter.Story.Title ?? "";
            objMasterStory.StorySynopsis = objChapter.Story.Synopsis ?? "";
            objMasterStory.StoryStyle = objChapter.Story.Style ?? "";
            objMasterStory.SystemMessage = objChapter.Story.Theme ?? "";

            objMasterStory.CurrentLocation = ConvertToJSONLocation(objParagraph.Location, objParagraph);
            objMasterStory.CharacterList = ConvertToJSONCharacter(colCharacter, objParagraph);
            objMasterStory.CurrentParagraph = ConvertToJSONParagraph(objParagraph);
            objMasterStory.CurrentChapter = ConvertToJSONChapter(objChapter);

            // C1 — LINQ projections with recency scoring (§4.3.5)
            int totalPrev = colParagraphs.Count;
            objMasterStory.PreviousParagraphs = colParagraphs.Select((p, idx) =>
            {
                var json = ConvertToJSONParagraph(p);
                json.relevance_score = totalPrev > 0 ? (float)(idx + 1) / totalPrev : 0f;
                return json;
            }).ToList();

            // RelatedParagraphs with similarity scores (§4.3.5)
            var relatedWithScores = await GetRelatedParagraphs(objChapter, objParagraph, AIPromptResult);
            objMasterStory.RelatedParagraphs = relatedWithScores.Select(r =>
            {
                var json = ConvertToJSONParagraph(r.Paragraph);
                json.relevance_score = r.Score;
                return json;
            }).ToList();

            // §4.3.3 — World Facts
            objMasterStory.WorldFacts = (objChapter.Story.WorldFacts ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            return objMasterStory;
        }
        #endregion

        #region public async Task<List<(Paragraph Paragraph, float Score)>> GetRelatedParagraphs(...)
        public async Task<List<(Paragraph Paragraph, float Score)>> GetRelatedParagraphs(Chapter objChapter, Paragraph objParagraph, AIPrompt AIPromptResult)
        {
            // R7 — Batch embedding calls
            var textsToEmbed = new List<string>();
            int paragraphIdx = -1, promptIdx = -1;

            if (!string.IsNullOrWhiteSpace(objParagraph.ParagraphContent))
            {
                paragraphIdx = textsToEmbed.Count;
                textsToEmbed.Add(objParagraph.ParagraphContent);
            }
            if (!string.IsNullOrWhiteSpace(AIPromptResult.AIPromptText))
            {
                promptIdx = textsToEmbed.Count;
                textsToEmbed.Add(AIPromptResult.AIPromptText);
            }

            float[] paragraphVectors = null;
            float[] promptVectors = null;

            if (textsToEmbed.Count > 0)
            {
                var embeddingResults = await OrchestratorMethods.GetBatchEmbeddings(textsToEmbed.ToArray());
                paragraphVectors = paragraphIdx >= 0 ? embeddingResults[paragraphIdx] : null;
                promptVectors = promptIdx >= 0 ? embeddingResults[promptIdx] : null;
            }

            // R3 — Build ParagraphVectorEntry list (replaces Dictionary)
            // §4.3.1 — Cross-timeline search
            string ParagraphTimelineName = objParagraph.Timeline?.TimelineName ?? "";

            var sameTimelineEntries = new List<ParagraphVectorEntry>();
            var crossTimelineEntries = new List<ParagraphVectorEntry>();

            var AllChapters = GetChapters(objChapter.Story);

            foreach (var chapter in AllChapters)
            {
                if (chapter.Sequence < objChapter.Sequence)
                {
                    // Same-timeline paragraphs
                    var sameTimeParagraphs = GetParagraphVectors(chapter, ParagraphTimelineName);
                    foreach (var paragraph in sameTimeParagraphs)
                    {
                        if (!string.IsNullOrEmpty(paragraph.vectors))
                        {
                            // R2 — Deserialize vectors once at load time
                            var vectors = JsonConvert.DeserializeObject<float[]>(paragraph.vectors);
                            sameTimelineEntries.Add(new ParagraphVectorEntry(
                                Id: $"Ch{chapter.Sequence}_P{paragraph.sequence}",
                                Content: paragraph.contents,
                                Vectors: vectors,
                                TimelineName: paragraph.timeline_name));
                        }
                    }

                    // Cross-timeline paragraphs (§4.3.1)
                    var allParagraphs = GetAllParagraphVectors(chapter);
                    foreach (var paragraph in allParagraphs)
                    {
                        if (!string.IsNullOrEmpty(paragraph.vectors) && paragraph.timeline_name != ParagraphTimelineName)
                        {
                            var vectors = JsonConvert.DeserializeObject<float[]>(paragraph.vectors);
                            crossTimelineEntries.Add(new ParagraphVectorEntry(
                                Id: $"Ch{chapter.Sequence}_P{paragraph.sequence}_X",
                                Content: paragraph.contents,
                                Vectors: vectors,
                                TimelineName: paragraph.timeline_name));
                        }
                    }
                }
            }

            // R1 — Calculate similarities using extracted helper
            var similarities = new List<(string Id, string Content, float Score)>();

            if (paragraphVectors != null)
            {
                similarities.AddRange(CalculateSimilarities(paragraphVectors, sameTimelineEntries, 1.0f));
                similarities.AddRange(CalculateSimilarities(paragraphVectors, crossTimelineEntries, 0.7f));
            }

            if (promptVectors != null)
            {
                similarities.AddRange(CalculateSimilarities(promptVectors, sameTimelineEntries, 1.0f));
                similarities.AddRange(CalculateSimilarities(promptVectors, crossTimelineEntries, 0.7f));
            }

            // R4 — Group by Id, keep max score
            var top10 = similarities
                .GroupBy(s => s.Id)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .Take(10)
                .ToList();

            // R5 — HashSet for result de-duplication; R8 — safe Paragraph objects
            var seen = new HashSet<string>();
            var resultList = new List<(Paragraph Paragraph, float Score)>(10);
            int seq = 0;

            foreach (var entry in top10)
            {
                if (seen.Add(entry.Content))
                {
                    resultList.Add((new Paragraph
                    {
                        Sequence = seq++,
                        Location = new Models.Location { LocationName = "" },
                        Timeline = new Timeline { TimelineName = ParagraphTimelineName },
                        Characters = new List<Models.Character>(),
                        ParagraphContent = entry.Content
                    }, entry.Score));
                }
            }

            return resultList;
        }
        #endregion

        #region private static CalculateSimilarities helper (R1)
        private static List<(string Id, string Content, float Score)> CalculateSimilarities(
            float[] queryVector,
            List<ParagraphVectorEntry> corpus,
            float weight = 1.0f)
        {
            var results = new List<(string, string, float)>(corpus.Count);
            foreach (var entry in corpus)
            {
                if (entry.Vectors != null && entry.Vectors.Length > 0)
                {
                    float similarity = CosineSimilarityStatic(queryVector, entry.Vectors);
                    results.Add((entry.Id, entry.Content, similarity * weight));
                }
            }
            return results;
        }
        #endregion

        #region private static CosineSimilarityStatic
        private static float CosineSimilarityStatic(float[] vector1, float[] vector2)
        {
            float dotProduct = 0;
            float magnitude1 = 0;
            float magnitude2 = 0;

            for (int i = 0; i < vector1.Length; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                magnitude1 += vector1[i] * vector1[i];
                magnitude2 += vector2[i] * vector2[i];
            }

            magnitude1 = (float)Math.Sqrt(magnitude1);
            magnitude2 = (float)Math.Sqrt(magnitude2);

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0f;

            return dotProduct / (magnitude1 * magnitude2);
        }
        #endregion
    }
}