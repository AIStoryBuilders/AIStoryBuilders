using AIStoryBuilders.Models;
using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using OpenAI.Files;
using System.IO.Compression;
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

                // Delete the temp directory
                Directory.Delete(TempPath, true);

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

        #region public byte[] ExportProject(Story objStory)
        public byte[] ExportProject(Story objStory)
        {
            try
            {
                #region Create Temp Directories

                // Create _Temp
                string TempPath =
                    $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/_Temp";

                if (!Directory.Exists(TempPath))
                {
                    Directory.CreateDirectory(TempPath);
                }
                else
                {
                    // Delete the temp directory
                    Directory.Delete(TempPath, true);

                    // Create the directory if it doesn't exist
                    if (!Directory.Exists(TempPath))
                    {
                        Directory.CreateDirectory(TempPath);
                    }
                }

                // Create _TempZip
                string TempZipPath =
                    $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}/AIStoryBuilders/_TempZip";

                if (!Directory.Exists(TempZipPath))
                {
                    Directory.CreateDirectory(TempZipPath);
                }
                else
                {
                    // Delete the temp directory
                    Directory.Delete(TempZipPath, true);

                    // Create the directory if it doesn't exist
                    if (!Directory.Exists(TempZipPath))
                    {
                        Directory.CreateDirectory(TempZipPath);
                    }
                } 
                #endregion

                // Create the manifest file
                string ManifestFilePath = Path.Combine(TempPath, "Manifest.config");

                // Create JSON from objStory
                JSONManifest objJSONManifest = new JSONManifest
                {
                    Version = _appMetadata.Version,
                    StoryTitle = objStory.Title,
                    CreatedDate = DateTime.Now.ToString()
                };

                string JSONManifest = JsonConvert.SerializeObject(objJSONManifest);

                using (var streamWriter = new StreamWriter(ManifestFilePath))
                {
                    streamWriter.WriteLine(JSONManifest);
                }

                // Get the Story
                var StoriesPath = $"{BasePath}/{objStory.Title}";
                string ExportFileName = $"{objStory.Title}.stybld";
                string ExportFilePath = $"{TempZipPath}/{ExportFileName}";

                // Copy the story folder to the temp directory
                CopyDirectory(StoriesPath, TempPath);

                // Zip the files
                ZipFile.CreateFromDirectory(TempPath, ExportFilePath);

                // Read the Zip file into a byte array
                byte[] ExportFileBytes = File.ReadAllBytes(ExportFilePath);

                // Delete the temp directories
                Directory.Delete(TempPath, true);
                Directory.Delete(TempZipPath, true);

                return ExportFileBytes;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("ExportFile: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                return null;
            }
        }
        #endregion

        // Utility

        #region public void CopyDirectory(string sourceDir, string targetDir)
        public void CopyDirectory(string sourceDir, string targetDir)
        {
            // Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, targetDir));
            }

            // Copy all the files & Replaces any files with the same name
            foreach (string filePath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string targetFilePath = filePath.Replace(sourceDir, targetDir);
                CopyFileToAppDataDirectory(filePath, targetFilePath);
            }
        }

        private void CopyFileToAppDataDirectory(string sourceFilePath, string targetFilePath)
        {
            // Check if the target file already exists, and if so, delete it to allow overwriting
            if (File.Exists(targetFilePath))
            {
                File.Delete(targetFilePath);
            }

            // Copy the file from the source to the target
            // The 'true' parameter allows the method to overwrite the file if it already exists
            File.Copy(sourceFilePath, targetFilePath, true);
        } 
        #endregion
    }
}
