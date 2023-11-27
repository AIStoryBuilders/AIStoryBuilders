using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        public async Task<JSONMasterStory> CreateMasterStory(Chapter objChapter, Paragraph objParagraph, List<Models.Character> colCharacter, List<Paragraph> colParagraphs)
        {
            JSONMasterStory objMasterStory = new JSONMasterStory();

            try
            {
                objMasterStory.StoryTitle = objChapter.Story.Title;
                objMasterStory.StorySynopsis = objChapter.Story.Synopsis;

                objMasterStory.CurrentLocation = ConvertToJSONLocation(objParagraph.Location);
                objMasterStory.CharacterList = ConvertToJSONCharacter(colCharacter, objParagraph);
                objMasterStory.CurrentParagraph = ConvertToJSONParagraph(objParagraph);
                objMasterStory.CurrentChapter = ConvertToJSONChapter(objChapter);

                objMasterStory.PreviousParagraphs = new List<Paragraphs>();

                foreach (var paragraph in colParagraphs)
                {
                    objMasterStory.PreviousParagraphs.Add(ConvertToJSONParagraph(paragraph));
                }
                
                //objMasterStory.RelatedParagraphs = await OrchestratorMethods.GetRelatedParagraphs(objChapter, colParagraphs);
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog(ex.Message);
            }

            return objMasterStory;
        }
    }
}
