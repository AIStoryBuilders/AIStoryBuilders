using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Devices.Sensors;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        // O1 — Embedding cache keyed by content hash
        private readonly ConcurrentDictionary<string, float[]> _embeddingCache = new();

        public void ClearEmbeddingCache() => _embeddingCache.Clear();
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
            // O1 — Build texts to embed with cache-aware logic
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
                // O1 — Check cache, only embed uncached texts
                var uncachedTexts = new List<(int Index, string Text)>();
                var cachedResults = new float[textsToEmbed.Count][];

                for (int i = 0; i < textsToEmbed.Count; i++)
                {
                    var key = ComputeContentHash(textsToEmbed[i]);
                    if (_embeddingCache.TryGetValue(key, out var cached))
                        cachedResults[i] = cached;
                    else
                        uncachedTexts.Add((i, textsToEmbed[i]));
                }

                if (uncachedTexts.Count > 0)
                {
                    var freshEmbeddings = await OrchestratorMethods
                        .GetBatchEmbeddings(uncachedTexts.Select(u => u.Text).ToArray());

                    for (int j = 0; j < uncachedTexts.Count; j++)
                    {
                        var key = ComputeContentHash(uncachedTexts[j].Text);
                        _embeddingCache[key] = freshEmbeddings[j];
                        cachedResults[uncachedTexts[j].Index] = freshEmbeddings[j];
                    }
                }

                paragraphVectors = paragraphIdx >= 0 ? cachedResults[paragraphIdx] : null;
                promptVectors = promptIdx >= 0 ? cachedResults[promptIdx] : null;
            }

            // O4 + O3 + O2 — Single DB call per chapter, pre-deserialized vectors, parallel processing
            string ParagraphTimelineName = objParagraph.Timeline?.TimelineName ?? "";

            var sameTimelineBag = new ConcurrentBag<ParagraphVectorEntry>();
            var crossTimelineBag = new ConcurrentBag<ParagraphVectorEntry>();

            var AllChapters = GetChapters(objChapter.Story);
            var earlierChapters = AllChapters.Where(c => c.Sequence < objChapter.Sequence).ToList();

            // O2 — Parallel chapter search
            await Parallel.ForEachAsync(earlierChapters, async (chapter, ct) =>
            {
                // O4 — Single method call per chapter; O3 — vectors already deserialized
                var allEntries = GetAllParagraphVectorsForChapter(chapter);

                foreach (var entry in allEntries)
                {
                    if (entry.TimelineName == ParagraphTimelineName)
                        sameTimelineBag.Add(entry);
                    else
                        crossTimelineBag.Add(entry with { Id = entry.Id + "_X" });
                }

                await Task.CompletedTask; // satisfy async lambda
            });

            var sameTimelineEntries = sameTimelineBag.ToList();
            var crossTimelineEntries = crossTimelineBag.ToList();

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

        #region O1 — ComputeContentHash helper
        private static string ComputeContentHash(string content)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hash, 0, 8);
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