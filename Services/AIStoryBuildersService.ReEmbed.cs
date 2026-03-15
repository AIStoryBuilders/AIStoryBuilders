using AIStoryBuilders.Models;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        public async Task ReEmbedStory(Story story)
        {
            var storyPath = $"{BasePath}/{story.Title}";
            int totalFiles = 0;
            int processedFiles = 0;

            // 1. Count total files to re-embed
            var paragraphFiles = Directory.Exists($"{storyPath}/Chapters")
                ? Directory.GetFiles($"{storyPath}/Chapters", "Paragraph*.txt", SearchOption.AllDirectories)
                : Array.Empty<string>();
            var chapterFiles = Directory.Exists($"{storyPath}/Chapters")
                ? Directory.GetFiles($"{storyPath}/Chapters", "Chapter*.txt", SearchOption.AllDirectories)
                : Array.Empty<string>();
            var characterFiles = Directory.Exists($"{storyPath}/Characters")
                ? Directory.GetFiles($"{storyPath}/Characters", "*.csv", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            var locationFiles = Directory.Exists($"{storyPath}/Locations")
                ? Directory.GetFiles($"{storyPath}/Locations", "*.csv", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            totalFiles = paragraphFiles.Length + chapterFiles.Length + characterFiles.Length + locationFiles.Length;

            // 2. Re-embed Paragraph files
            foreach (var file in paragraphFiles)
            {
                processedFiles++;
                TextEvent?.Invoke(this, new TextEventArgs(
                    $"Re-embedding paragraph {processedFiles}/{totalFiles}...", 1));

                var lines = File.ReadAllLines(file)
                                .Where(l => l.Trim() != "").ToArray();
                if (lines.Length == 0) continue;

                var parts = lines[0].Split('|');
                if (parts.Length < 4) continue;

                var content = parts[3];
                if (string.IsNullOrWhiteSpace(content)) continue;

                string newEmbedding = await OrchestratorMethods.GetVectorEmbedding(content, true);
                string rebuilt = $"{parts[0]}|{parts[1]}|{parts[2]}|{newEmbedding}";
                File.WriteAllText(file, rebuilt);
            }

            // 3. Re-embed Chapter synopsis files
            foreach (var file in chapterFiles)
            {
                processedFiles++;
                TextEvent?.Invoke(this, new TextEventArgs(
                    $"Re-embedding chapter {processedFiles}/{totalFiles}...", 1));

                var text = File.ReadAllText(file).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                var synopsisEnd = text.IndexOf("|[");
                string synopsis = synopsisEnd > 0 ? text.Substring(0, synopsisEnd) : text;

                string newEmbedding = await OrchestratorMethods.GetVectorEmbedding(synopsis, true);
                File.WriteAllText(file, newEmbedding);
            }

            // 4. Re-embed Character description files
            foreach (var file in characterFiles)
            {
                processedFiles++;
                TextEvent?.Invoke(this, new TextEventArgs(
                    $"Re-embedding character {processedFiles}/{totalFiles}...", 1));

                await ReEmbedCsvDescriptionFile(file, hasTypeAndTimeline: true);
            }

            // 5. Re-embed Location description files
            foreach (var file in locationFiles)
            {
                processedFiles++;
                TextEvent?.Invoke(this, new TextEventArgs(
                    $"Re-embedding location {processedFiles}/{totalFiles}...", 1));

                await ReEmbedCsvDescriptionFile(file, hasTypeAndTimeline: false);
            }

            TextEvent?.Invoke(this, new TextEventArgs(
                $"Re-embedding complete — {totalFiles} files processed.", 5));
        }

        /// <summary>
        /// Re-embeds each line in a CSV file that stores description + vector.
        /// </summary>
        private async Task ReEmbedCsvDescriptionFile(string filePath, bool hasTypeAndTimeline)
        {
            var lines = File.ReadAllLines(filePath)
                            .Where(l => l.Trim() != "").ToList();
            var rebuilt = new List<string>();

            foreach (var line in lines)
            {
                var parts = line.Split('|');
                string description;
                string prefix;

                if (hasTypeAndTimeline && parts.Length >= 3)
                {
                    description = parts[2];
                    prefix = $"{parts[0]}|{parts[1]}";
                }
                else if (parts.Length >= 2)
                {
                    description = parts[0];
                    prefix = $"{parts[0]}|{parts[1]}";
                }
                else continue;

                string newVector = await OrchestratorMethods.GetVectorEmbedding(description, false);
                rebuilt.Add($"{prefix}|{newVector}");
            }

            File.WriteAllLines(filePath, rebuilt);
        }
    }
}
