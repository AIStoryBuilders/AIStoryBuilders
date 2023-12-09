using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System.Text.Json;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        #region public void RestructureParagraphs(Chapter objChapter, int ParagraphToRemove, string RestructureType)
        public void RestructureParagraphs(Chapter objChapter, int ParagraphToRemove, string RestructureType)
        {
            try
            {
                
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("RestructureParagraphs: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }
        #endregion

        #region public void RestructureChapters(int ChapterNumber, string RestructureType)
        public void RestructureChapters(int ChapterNumber, string RestructureType)
        {
            try
            {

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
