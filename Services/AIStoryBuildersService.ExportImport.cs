using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System.Text.Json;
using Xceed.Document.NET;
using Xceed.Words.NET;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        #region public byte[] ExportWordDocument(Story objStory)
        public byte[] ExportWordDocument(Story objStory)
        {
            try
            {
                string TempPath =
                    $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/_Temp";

                // Create the directory if it doesn't exist
                if (!Directory.Exists(TempPath))
                {
                    Directory.CreateDirectory(TempPath);
                }

                string WordFileName = $"{objStory.Title}.docx";
                string WordFilePath = $"{TempPath}/{WordFileName}";

                using (DocX document = DocX.Create(WordFilePath))
                {
                    // Create Cover Page
                    document.InsertParagraph(objStory.Title).FontSize(25d).SpacingAfter(50d).Alignment = Alignment.center;
 
                    var colChapters = GetChapters(objStory);

                    foreach (var objChapter in colChapters)
                    {
                        document.InsertSectionPageBreak();

                        // Create Chapter Title
                        document.InsertParagraph(objChapter.ChapterName).FontSize(20d).SpacingAfter(25d).Alignment = Alignment.center;

                        var colParagraphs = GetParagraphs(objChapter);

                        foreach (var objParagraph in colParagraphs)
                        {
                            // Break up objParagraph.ParagraphContent by \n
                            string[] sections = objParagraph.ParagraphContent.Split('\n');

                            // Create a new paragraph for each line
                            foreach (string section in sections)
                            {
                                var p = document.InsertParagraph(section);
                                p.IndentationFirstLine = 28f;
                            }
                        }
                    }

                    // Save the document.
                    document.Save();
                }

                // Read the document and convert to base64
                byte[] WordFileBytes = File.ReadAllBytes(WordFilePath);

                // Delete the file
                File.Delete(WordFilePath);

                return WordFileBytes;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("ExportWordDocument: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                return null;
            }
        }
        #endregion

    }
}
