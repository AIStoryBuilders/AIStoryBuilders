using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System.Text.Json;
using Xceed.Words.NET;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        #region public async Task ExportWordDocument(Story objStory)
        public async Task<byte[]> ExportWordDocument(Story objStory)
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
                    // Add a new Paragraph to the document.
                    var p = document.InsertParagraph();

                    // Append some text.
                    p.Append("Hello World").Font("Arial Black");

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
