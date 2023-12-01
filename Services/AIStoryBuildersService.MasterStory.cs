using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        #region public async Task<JSONMasterStory> CreateMasterStory(Chapter objChapter, Paragraph objParagraph, List<Models.Character> colCharacter, List<Paragraph> colParagraphs)
        public async Task<JSONMasterStory> CreateMasterStory(Chapter objChapter, Paragraph objParagraph, List<Models.Character> colCharacter, List<Paragraph> colParagraphs)
        {
            JSONMasterStory objMasterStory = new JSONMasterStory();

            try
            {
                objMasterStory.StoryTitle = objChapter.Story.Title;
                objMasterStory.StorySynopsis = objChapter.Story.Synopsis;

                objMasterStory.CurrentLocation = ConvertToJSONLocation(objParagraph.Location, objParagraph);
                objMasterStory.CharacterList = ConvertToJSONCharacter(colCharacter, objParagraph);
                objMasterStory.CurrentParagraph = ConvertToJSONParagraph(objParagraph);
                objMasterStory.CurrentChapter = ConvertToJSONChapter(objChapter);

                // PreviousParagraphs
                objMasterStory.PreviousParagraphs = new List<JSONParagraphs>();

                foreach (var paragraph in colParagraphs)
                {
                    objMasterStory.PreviousParagraphs.Add(ConvertToJSONParagraph(paragraph));
                }

                // RelatedParagraphs
                objMasterStory.RelatedParagraphs = new List<JSONParagraphs>();

                var RelatedParagraphs = await GetRelatedParagraphs(objChapter, objParagraph);

                foreach (var paragraph in RelatedParagraphs)
                {
                    objMasterStory.RelatedParagraphs.Add(ConvertToJSONParagraph(paragraph));
                }
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog(ex.Message);
            }

            return objMasterStory;
        }
        #endregion
        
        #region public async Task<List<Paragraph>> GetRelatedParagraphs(Chapter objChapter, Paragraph objParagraph)
        public async Task<List<Paragraph>> GetRelatedParagraphs(Chapter objChapter, Paragraph objParagraph)
        {
            List<Paragraph> colParagraph = new List<Paragraph>();

            // Get the vector embedding for the paragraph content
            var ParagraphContentEmbeddingVectors = await OrchestratorMethods.GetVectorEmbeddingAsFloats(objParagraph.ParagraphContent);

            // ************************************************************************************
            // Read all Paragraph files in memory for Chapters that come before the current Chapter
            // Perform vector search on PreviousParagraphs and all Paragraph files in memory
            // Add only the top 10 Paragraphs (include their Timelines)

            Dictionary<string, string> AIStoryBuildersMemory = new Dictionary<string, string>();

            string ParagraphTimelineName = "";

            if (objParagraph.Timeline != null)
            {
                ParagraphTimelineName = objParagraph.Timeline.TimelineName;
            }

            var AllChapters = GetChapters(objChapter.Story);

            foreach (var chapter in AllChapters)
            {
                if (chapter.Sequence < objChapter.Sequence)
                {
                    // Get all the paragraphs for the chapter on the Timeline
                    var colPargraphs = GetParagraphVectors(chapter, ParagraphTimelineName);

                    foreach (var paragraph in colPargraphs)
                    {
                        AIStoryBuildersMemory.Add(paragraph.contents, paragraph.vectors);
                    }
                }
            }

            // Reset the similarities list
            List<(string, float)> similarities = new List<(string, float)>();

            // Calculate the similarity between the prompt's
            // embedding and each existing embedding
            foreach (var embedding in AIStoryBuildersMemory)
            {
                if (embedding.Value != null)
                {
                    if (embedding.Value != "")
                    {
                        var ConvertEmbeddingToFloats = JsonConvert.DeserializeObject<List<float>>(embedding.Value);

                        var similarity =
                        OrchestratorMethods.CosineSimilarity(
                            ParagraphContentEmbeddingVectors,
                        ConvertEmbeddingToFloats.ToArray());

                        similarities.Add((embedding.Key, similarity));
                    }
                }
            }

            // Sort the results by similarity in descending order
            similarities.Sort((a, b) => b.Item2.CompareTo(a.Item2));

            var Top10similarities = similarities.Take(10).ToList();

            if(Top10similarities.Count > 0)
            {
                int i = 0;
                foreach (var similarity in Top10similarities)
                {
                    // Create a Paragraph
                    Paragraph AISimilaritiesParagraph = new Paragraph();
                    AISimilaritiesParagraph.Sequence = i;
                    AISimilaritiesParagraph.Location = new Models.Location();
                    AISimilaritiesParagraph.Timeline = new Timeline() { TimelineName = ParagraphTimelineName };
                    AISimilaritiesParagraph.Characters = new List<Models.Character>();
                    AISimilaritiesParagraph.ParagraphContent = similarity.Item1;

                    colParagraph.Add(AISimilaritiesParagraph);
                    i++;
                }
            }

            return colParagraph;
        }
        #endregion
    }
}
