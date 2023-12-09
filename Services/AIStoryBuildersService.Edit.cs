using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System.Text.Json;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        #region public void RestructureParagraphs(Chapter objChapter, int ParagraphNumber, RestructureType RestructureType)
        public void RestructureParagraphs(Chapter objChapter, int ParagraphNumber, RestructureType RestructureType)
        {
            try
            {
                int CountOfParagraphs = CountParagraphs(objChapter);

                if (RestructureType == RestructureType.Add)
                {
                    // Add a paragraph
                    Paragraph objParagraph = new Paragraph();
                    objParagraph.Sequence = ParagraphNumber + 1;
                    objParagraph.ParagraphContent = "";

                }
                else if (RestructureType == RestructureType.Delete)
                {
                    // Delete paragraph

                }

            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("RestructureParagraphs: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }
        #endregion

        #region public void RestructureChapters(int ChapterNumber, RestructureType RestructureType)
        public void RestructureChapters(int ChapterNumber, RestructureType RestructureType)
        {
            try
            {
                if (RestructureType == RestructureType.Add)
                {

                }
                else if (RestructureType == RestructureType.Delete)
                {

                }
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("RestructureChapters: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }
        #endregion
    }
}
