using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;
using System.Text.Json;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {
        #region *** JSONNewStory ***
        public JSONStory ParseJSONNewStory(string RawJSON)
        {
            try
            {
                // Parse the JSON as a dynamic object
                dynamic ParsedJSON = JsonConvert.DeserializeObject(RawJSON);

                int i = 0;
                int ii = 0;

                JSONStory ParsedNewStory = new JSONStory();

                int charactersCount = 1;
                int locationsCount = 1;
                int timelinesCount = 1;

                if (ParsedJSON.characters != null)
                {                    
                    charactersCount = ((Newtonsoft.Json.Linq.JContainer)ParsedJSON.characters).Count;
                }
                if (ParsedJSON.locations != null)
                {
                    locationsCount = ((Newtonsoft.Json.Linq.JContainer)ParsedJSON.locations).Count;
                }
                if (ParsedJSON.timelines != null)
                {
                    timelinesCount = ((Newtonsoft.Json.Linq.JContainer)ParsedJSON.timelines).Count;
                }

                ParsedNewStory.characters = new Character[charactersCount];
                ParsedNewStory.locations = new Locations[locationsCount];
                ParsedNewStory.timelines = new Timelines[timelinesCount];

                foreach (dynamic location in ParsedJSON.locations)
                {
                    // Add the location to the new story
                    ParsedNewStory.locations[i] = new Locations();
                    ParsedNewStory.locations[i].name = location.name;

                    if (location.descriptions != null)
                    {
                        ParsedNewStory.locations[i].descriptions = new string[location.descriptions.Count];

                        // See if there is more than one description
                        if (location.descriptions.Count > 1)
                        {
                            // Loop through the descriptions
                            ii = 0;
                            foreach (dynamic description in location.descriptions)
                            {
                                // Add the description to the location
                                ParsedNewStory.locations[i].descriptions[ii] = description;
                                ii++;
                            }
                        }
                        else
                        {
                            // Add the description to the location
                            ParsedNewStory.locations[i].descriptions = new string[1];
                            ParsedNewStory.locations[i].descriptions[0] = location.descriptions[0];
                        }
                    }
                    i++;
                }

                i = 0;
                foreach (dynamic timeline in ParsedJSON.timelines)
                {
                    // Add the timeline to the new story
                    ParsedNewStory.timelines[i] = new Timelines();
                    ParsedNewStory.timelines[i].name = timeline.name;
                    ParsedNewStory.timelines[i].description = timeline.description;
                    i++;
                }

                i = 0;
                foreach (dynamic character in ParsedJSON.characters)
                {
                    // Add the character to the new story
                    ParsedNewStory.characters[i] = new Character();
                    ParsedNewStory.characters[i].name = character.name;

                    if (character.descriptions != null)
                    {
                        // See if there is more than one description
                        if (character.descriptions.Count > 1)
                        {
                            // Loop through the descriptions
                            ii = 0;
                            ParsedNewStory.characters[i].descriptions = new Descriptions[character.descriptions.Count];
                            foreach (dynamic description in character.descriptions)
                            {
                                // Add the description to the character
                                ParsedNewStory.characters[i].descriptions[ii] = new Descriptions();
                                ParsedNewStory.characters[i].descriptions[ii].description_type = description.description_type;
                                ParsedNewStory.characters[i].descriptions[ii]._enum = description._enum;
                                ParsedNewStory.characters[i].descriptions[ii].description = description.description;
                                ParsedNewStory.characters[i].descriptions[ii].timeline_name = description.timeline_name;
                                ii++;
                            }
                        }
                        else
                        {
                            // Add the description to the character
                            ParsedNewStory.characters[i].descriptions = new Descriptions[1];
                            ParsedNewStory.characters[i].descriptions[0] = new Descriptions();

                            if (((Newtonsoft.Json.Linq.JContainer)character.descriptions).HasValues)
                            {
                                try
                                {
                                    ParsedNewStory.characters[i].descriptions[0].description_type = character.descriptions.description_type;
                                    ParsedNewStory.characters[i].descriptions[0]._enum = character.descriptions._enum;
                                    ParsedNewStory.characters[i].descriptions[0].description = character.descriptions.description;
                                    ParsedNewStory.characters[i].descriptions[0].timeline_name = character.descriptions.timeline_name;
                                }
                                catch
                                {
                                    ParsedNewStory.characters[i].descriptions[0].description_type = character.descriptions[0].description_type;
                                    ParsedNewStory.characters[i].descriptions[0]._enum = character.descriptions[0]._enum;
                                    ParsedNewStory.characters[i].descriptions[0].description = character.descriptions[0].description;
                                    ParsedNewStory.characters[i].descriptions[0].timeline_name = character.descriptions[0].timeline_name;
                                }
                            }
                        }
                    }
                    i++;
                }

                return ParsedNewStory;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("ParseJSONNewStory: " + ex.Message + " " + ex.StackTrace ?? "" + " " + ex.InnerException.StackTrace ?? "");

                JSONStory ParsedNewStory = new JSONStory();
                ParsedNewStory.characters = new Character[0];
                ParsedNewStory.locations = new Locations[0];
                ParsedNewStory.timelines = new Timelines[0];

                return ParsedNewStory;
            }
        }
        #endregion

        #region *** JSONNewChapters ***
        public JSONChapters ParseJSONNewChapters(string RawJSON)
        {
            try
            {
                // Parse the JSON as a dynamic object
                dynamic ParsedJSON = JsonConvert.DeserializeObject(RawJSON);

                int i = 0;
                int ii = 0;

                JSONChapters ParsedNewChapters = new JSONChapters();

                int chapterCount = 1;

                if (ParsedJSON != null)
                {
                    if (ParsedJSON.Count == null)
                    {
                        // All three chapters have been returned as one element
                        if (ParsedJSON.chapter != null)
                        {
                            // Sometimes it comes back as chapter
                            ParsedJSON = ParsedJSON.chapter;
                        }
                        else
                        {
                            // Sometimes it comes back as chapters
                            ParsedJSON = ParsedJSON.chapters;
                        }

                        chapterCount = ParsedJSON.Count;

                        ParsedNewChapters.chapter = new JSONChapter[chapterCount];

                        foreach (dynamic chapter in ParsedJSON)
                        {
                            // Add the chapter to the new story
                            ParsedNewChapters.chapter[i] = new JSONChapter();

                            if (chapter != null)
                            {
                                ParsedNewChapters.chapter[i].chapter_name = chapter.chapter_name;
                                ParsedNewChapters.chapter[i].chapter_synopsis = chapter.chapter_synopsis;

                                if (chapter.paragraphs != null)
                                {
                                    // See if there is more than one paragraph
                                    if (chapter.paragraphs.Count != null)
                                    {
                                        // Loop through the paragraphs
                                        ii = 0;
                                        ParsedNewChapters.chapter[i].paragraphs = new JSONParagraphs[chapter.paragraphs.Count];

                                        foreach (dynamic paragraph in chapter.paragraphs)
                                        {
                                            // Add the paragraph to the chapter
                                            ParsedNewChapters.chapter[i].paragraphs[ii] = new JSONParagraphs();
                                            ParsedNewChapters.chapter[i].paragraphs[ii].contents = paragraph.contents;
                                            ParsedNewChapters.chapter[i].paragraphs[ii].location_name = paragraph.location_name;
                                            ParsedNewChapters.chapter[i].paragraphs[ii].timeline_name = paragraph.timeline_name;
                                            ParsedNewChapters.chapter[i].paragraphs[ii].sequence = (ii + 1);

                                            if (paragraph.character_names != null)
                                            {
                                                ParsedNewChapters.chapter[i].paragraphs[ii].character_names = new string[paragraph.character_names.Count];

                                                // See if there is more than one character
                                                if (paragraph.character_names.Count > 1)
                                                {
                                                    // Loop through the characters
                                                    int iii = 0;
                                                    foreach (dynamic character in paragraph.character_names)
                                                    {
                                                        // Add the character to the paragraph
                                                        ParsedNewChapters.chapter[i].paragraphs[ii].character_names[iii] = character;
                                                        iii++;
                                                    }
                                                }
                                                else
                                                {
                                                    // Add the character to the paragraph
                                                    ParsedNewChapters.chapter[i].paragraphs[ii].character_names[0] = paragraph.character_names[0];
                                                }
                                            }

                                            ii++;
                                        }
                                    }
                                    else
                                    {
                                        // Add the paragraph to the chapter
                                        ParsedNewChapters.chapter[i].paragraphs = new JSONParagraphs[1];
                                        ParsedNewChapters.chapter[i].paragraphs[0] = new JSONParagraphs();
                                        ParsedNewChapters.chapter[i].paragraphs[0].contents = chapter[i].paragraphs.contents;
                                        ParsedNewChapters.chapter[i].paragraphs[0].location_name = chapter[i].paragraphs.location_name;
                                        ParsedNewChapters.chapter[i].paragraphs[0].timeline_name = chapter[i].paragraphs.timeline_name;
                                        ParsedNewChapters.chapter[i].paragraphs[0].sequence = 1;

                                        if (chapter[i].paragraphs.character_names != null)
                                        {
                                            ParsedNewChapters.chapter[i].paragraphs[0].character_names = new string[chapter[i].paragraphs.character_names.Count];

                                            // See if there is more than one character
                                            if (chapter[i].paragraphs.character_names.Count != null)
                                            {
                                                // Loop through the characters
                                                int iii = 0;
                                                foreach (dynamic character in chapter[i].paragraphs.character_names)
                                                {
                                                    // Add the character to the paragraph
                                                    ParsedNewChapters.chapter[i].paragraphs[ii].character_names[iii] = character;
                                                    iii++;
                                                }
                                            }
                                            else
                                            {
                                                // Add the character to the paragraph
                                                ParsedNewChapters.chapter[i].paragraphs[ii].character_names[0] = chapter[i].paragraphs.character_names[0];
                                            }
                                        }
                                    }
                                }
                            }
                            i++;
                        }
                    }
                    else
                    {
                        chapterCount = ParsedJSON.Count;

                        ParsedNewChapters.chapter = new JSONChapter[chapterCount];

                        foreach (dynamic chapter in ParsedJSON)
                        {
                            // Add the chapter to the new story
                            ParsedNewChapters.chapter[i] = new JSONChapter();

                            if (chapter.chapter != null)
                            {
                                ParsedNewChapters.chapter[i].chapter_name = chapter.chapter.chapter_name;
                                ParsedNewChapters.chapter[i].chapter_synopsis = chapter.chapter.chapter_synopsis;

                                if (chapter.chapter.paragraphs != null)
                                {
                                    // See if there is more than one paragraph
                                    if (chapter.chapter.paragraphs.Count != null)
                                    {
                                        // Loop through the paragraphs
                                        ii = 0;
                                        ParsedNewChapters.chapter[i].paragraphs = new JSONParagraphs[chapter.chapter.paragraphs.Count];

                                        foreach (dynamic paragraph in chapter.chapter.paragraphs)
                                        {
                                            // Add the paragraph to the chapter
                                            ParsedNewChapters.chapter[i].paragraphs[ii] = new JSONParagraphs();
                                            ParsedNewChapters.chapter[i].paragraphs[ii].contents = paragraph[ii].contents;
                                            ParsedNewChapters.chapter[i].paragraphs[ii].location_name = paragraph[ii].location_name;
                                            ParsedNewChapters.chapter[i].paragraphs[ii].timeline_name = paragraph[ii].timeline_name;
                                            ParsedNewChapters.chapter[i].paragraphs[ii].sequence = (ii + 1);

                                            if (paragraph[ii].character_names != null)
                                            {
                                                ParsedNewChapters.chapter[i].paragraphs[ii].character_names = new string[paragraph[ii].character_names.Count];

                                                // See if there is more than one character
                                                if (paragraph[ii].character_names.Count > 1)
                                                {
                                                    // Loop through the characters
                                                    int iii = 0;
                                                    foreach (dynamic character in paragraph[iii].character_names)
                                                    {
                                                        // Add the character to the paragraph
                                                        ParsedNewChapters.chapter[i].paragraphs[ii].character_names[iii] = character;
                                                        iii++;
                                                    }
                                                }
                                                else
                                                {
                                                    // Add the character to the paragraph
                                                    ParsedNewChapters.chapter[i].paragraphs[ii].character_names[0] = paragraph[ii].character_names[0];
                                                }
                                            }

                                            ii++;
                                        }
                                    }
                                    else
                                    {
                                        // Add the paragraph to the chapter
                                        ParsedNewChapters.chapter[i].paragraphs = new JSONParagraphs[1];
                                        ParsedNewChapters.chapter[i].paragraphs[0] = new JSONParagraphs();
                                        ParsedNewChapters.chapter[i].paragraphs[0].contents = chapter.chapter.paragraphs.contents;
                                        ParsedNewChapters.chapter[i].paragraphs[0].location_name = chapter.chapter.paragraphs.location_name;
                                        ParsedNewChapters.chapter[i].paragraphs[0].timeline_name = chapter.chapter.paragraphs.timeline_name;
                                        ParsedNewChapters.chapter[i].paragraphs[0].sequence = 1;

                                        if (chapter.chapter.paragraphs.character_names != null)
                                        {
                                            ParsedNewChapters.chapter[i].paragraphs[0].character_names = new string[chapter.chapter.paragraphs.character_names.Count];

                                            // See if there is more than one character
                                            if (chapter.chapter.paragraphs.character_names.Count != null)
                                            {
                                                // Loop through the characters
                                                int iii = 0;
                                                foreach (dynamic character in chapter.chapter.paragraphs.character_names)
                                                {
                                                    // Add the character to the paragraph
                                                    ParsedNewChapters.chapter[i].paragraphs[ii].character_names[iii] = character;
                                                    iii++;
                                                }
                                            }
                                            else
                                            {
                                                // Add the character to the paragraph
                                                ParsedNewChapters.chapter[i].paragraphs[ii].character_names[0] = chapter.chapter.paragraphs.character_names[0];
                                            }
                                        }
                                    }
                                }
                            }
                            i++;
                        }
                    }
                }

                return ParsedNewChapters;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("ParseJSONNewChapters: " + ex.Message);

                JSONChapters ParsedNewChapters = new JSONChapters();
                ParsedNewChapters.chapter = new JSONChapter[0];

                return ParsedNewChapters;
            }
        }
        #endregion
    }
}
