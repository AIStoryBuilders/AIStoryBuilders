using AIStoryBuilders.Models;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        public async Task ReloadStoryAsync(Story story, IGraphBuilder graphBuilder, IProgress<string> progress = null)
        {
            var storyPath = $"{BasePath}/{story.Title}";

            // Step 1: Save story metadata
            progress?.Report("Step 1/5: Saving all story files…");
            TextEvent?.Invoke(this, new TextEventArgs("Saving story metadata…", 2));
            UpdateStory(story);

            // Step 2: Re-embed paragraphs
            progress?.Report("Step 2/5: Re-embedding paragraphs…");
            var paragraphFiles = Directory.Exists($"{storyPath}/Chapters")
                ? Directory.GetFiles($"{storyPath}/Chapters", "Paragraph*.txt", SearchOption.AllDirectories)
                : Array.Empty<string>();

            int total = paragraphFiles.Length;
            int processed = 0;
            foreach (var file in paragraphFiles)
            {
                processed++;
                TextEvent?.Invoke(this, new TextEventArgs(
                    $"Re-embedding paragraph {processed}/{total}…", 1));

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

            // Step 3: Re-embed chapters, characters, locations
            progress?.Report("Step 3/5: Re-embedding chapters, characters, locations…");
            var chapterFiles = Directory.Exists($"{storyPath}/Chapters")
                ? Directory.GetFiles($"{storyPath}/Chapters", "Chapter*.txt", SearchOption.AllDirectories)
                : Array.Empty<string>();
            var characterFiles = Directory.Exists($"{storyPath}/Characters")
                ? Directory.GetFiles($"{storyPath}/Characters", "*.csv", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            var locationFiles = Directory.Exists($"{storyPath}/Locations")
                ? Directory.GetFiles($"{storyPath}/Locations", "*.csv", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

            foreach (var file in chapterFiles)
            {
                TextEvent?.Invoke(this, new TextEventArgs(
                    $"Re-embedding chapter…", 1));

                var text = File.ReadAllText(file).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                var synopsisEnd = text.IndexOf("|[");
                string synopsis = synopsisEnd > 0 ? text.Substring(0, synopsisEnd) : text;

                string newEmbedding = await OrchestratorMethods.GetVectorEmbedding(synopsis, true);
                File.WriteAllText(file, newEmbedding);
            }

            foreach (var file in characterFiles)
            {
                TextEvent?.Invoke(this, new TextEventArgs(
                    $"Re-embedding character…", 1));
                await ReEmbedCsvDescriptionFile(file, hasTypeAndTimeline: true);
            }

            foreach (var file in locationFiles)
            {
                TextEvent?.Invoke(this, new TextEventArgs(
                    $"Re-embedding location…", 1));
                await ReEmbedCsvDescriptionFile(file, hasTypeAndTimeline: false);
            }

            // Step 4: Rebuild Knowledge Graph
            progress?.Report("Step 4/5: Rebuilding Knowledge Graph…");
            TextEvent?.Invoke(this, new TextEventArgs("Rebuilding Knowledge Graph…", 3));
            var fullStory = LoadFullStory(new Story { Title = story.Title });
            var graph = graphBuilder.Build(fullStory);
            await PersistGraphAsync(fullStory, graph, storyPath);
            GraphState.Current = graph;
            GraphState.CurrentStory = fullStory;

            // Step 5: Reload story data
            progress?.Report("Step 5/5: Reloading story data…");
            TextEvent?.Invoke(this, new TextEventArgs("Reloading story data…", 2));

            // Refresh the story object parameters
            story.Character = fullStory.Character;
            story.Location = fullStory.Location;
            story.Timeline = fullStory.Timeline;
            story.Chapter = fullStory.Chapter;

            progress?.Report("Re-load complete!");
            TextEvent?.Invoke(this, new TextEventArgs(
                $"Re-load complete — all embeddings regenerated, Knowledge Graph rebuilt.", 5));
        }

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

                    // If the description looks like an embedding vector, it was already
                    // corrupted — skip re-embedding this line to avoid further damage
                    if (!string.IsNullOrEmpty(description)
                        && description.TrimStart().StartsWith("[")
                        && description.Contains(","))
                    {
                        rebuilt.Add(line);
                        continue;
                    }

                    string newVector = await OrchestratorMethods.GetVectorEmbedding(description, false);
                    rebuilt.Add($"{prefix}|{description}|{newVector}");
                }
                else if (parts.Length >= 2)
                {
                    description = parts[0];
                    prefix = $"{parts[0]}|{parts[1]}";

                    string newVector = await OrchestratorMethods.GetVectorEmbedding(description, false);
                    rebuilt.Add($"{prefix}|{newVector}");
                }
                else continue;
            }

            File.WriteAllLines(filePath, rebuilt);
        }
    }
}
