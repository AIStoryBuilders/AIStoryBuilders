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
                var ChapterNameParts = objChapter.ChapterName.Split(' ');
                string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];
                var AIStoryBuildersParagraphsPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/{ChapterName}";               
                int CountOfParagraphs = CountParagraphs(objChapter);

                // Loop through all remaining paragraphs and rename them
                for (int i = CountOfParagraphs; ParagraphNumber <= i; i--)
                {
                    string OldParagraphPath = "";
                    string NewParagraphPath = "";

                    if (RestructureType == RestructureType.Add)
                    {
                        OldParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{i}.txt";
                        NewParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{i + 1}.txt";
                    }
                    else if (RestructureType == RestructureType.Delete)
                    {
                        OldParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{i}.txt";
                        NewParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{i - 1}.txt";
                    }

                    // Rename file

                    System.IO.File.Move(OldParagraphPath, NewParagraphPath);
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
