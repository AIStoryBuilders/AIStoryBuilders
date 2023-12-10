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
                string OldParagraphPath = "";
                string NewParagraphPath = "";
                var ChapterNameParts = objChapter.ChapterName.Split(' ');
                string ChapterName = ChapterNameParts[0] + ChapterNameParts[1];
                var AIStoryBuildersParagraphsPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/{ChapterName}";
                int CountOfParagraphs = CountParagraphs(objChapter);

                // Loop through all remaining paragraphs and rename them
                if (RestructureType == RestructureType.Add)
                {
                    for (int i = CountOfParagraphs; ParagraphNumber <= i; i--)
                    {
                        OldParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{i}.txt";
                        NewParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{i + 1}.txt";

                        // Rename file
                        System.IO.File.Move(OldParagraphPath, NewParagraphPath);
                    }
                }
                else if (RestructureType == RestructureType.Delete)
                {
                    for (int i = ParagraphNumber; i <= CountOfParagraphs; i++)
                    {
                        OldParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{i + 1}.txt";
                        NewParagraphPath = $"{AIStoryBuildersParagraphsPath}/Paragraph{i}.txt";

                        // Rename file
                        System.IO.File.Move(OldParagraphPath, NewParagraphPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("RestructureParagraphs: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");
            }
        }
        #endregion

        #region public void RestructureChapters(Chapter objChapter, RestructureType RestructureType)
        public void RestructureChapters(Chapter objChapter, RestructureType RestructureType)
        {
            try
            {
                string OldChapterPath = "";
                string NewChapterPath = "";
                string OldChapterFolderPath = "";
                string NewChapterFolderPath = "";

                int CountOfChapters = CountChapters(objChapter.Story);

                if (RestructureType == RestructureType.Add)
                {
                    for (int i = CountOfChapters; objChapter.Sequence <= i; i--)
                    {
                        // Rename Chapter file
                        OldChapterPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/Chapter{i}/Chapter{i}.txt";
                        NewChapterPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/Chapter{i}/Chapter{i + 1}.txt";
                        System.IO.File.Move(OldChapterPath, NewChapterPath);

                        // Rename Chapter folder
                        OldChapterFolderPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/Chapter{i}";
                        NewChapterFolderPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/Chapter{i + 1}";
                        System.IO.Directory.Move(OldChapterFolderPath, NewChapterFolderPath);
                    }
                }
                else if (RestructureType == RestructureType.Delete)
                {
                    for (int i = objChapter.Sequence; i <= CountOfChapters; i++)
                    {
                        // Rename Chapter file
                        OldChapterPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/Chapter{i + 1}/Chapter{i + 1}.txt";
                        NewChapterPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/Chapter{i + 1}/Chapter{i}.txt";
                        System.IO.File.Move(OldChapterPath, NewChapterPath);

                        // Rename Chapter folder
                        OldChapterFolderPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/Chapter{i + 1}";
                        NewChapterFolderPath = $"{BasePath}/{objChapter.Story.Title}/Chapters/Chapter{i}";
                        System.IO.Directory.Move(OldChapterFolderPath, NewChapterFolderPath);
                    }
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
