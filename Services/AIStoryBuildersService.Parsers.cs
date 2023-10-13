using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;

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

                if(ParsedJSON.characters != null)
                {
                    charactersCount = ParsedJSON.characters.Count;
                }
                if (ParsedJSON.locations != null)
                {
                    locationsCount = ParsedJSON.locations.Count;
                }
                if (ParsedJSON.timelines != null)
                {
                    timelinesCount = ParsedJSON.timelines.Count;
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
                            ParsedNewStory.characters[i].descriptions[0].description_type = character.descriptions.description_type;
                            ParsedNewStory.characters[i].descriptions[0]._enum = character.descriptions._enum;
                            ParsedNewStory.characters[i].descriptions[0].description = character.descriptions.description;
                            ParsedNewStory.characters[i].descriptions[0].timeline_name = character.descriptions.timeline_name;
                        }
                    }
                    i++;
                }

                return ParsedNewStory;
            }
            catch (Exception ex)
            {
                // Log error
                LogService.WriteToLog("ParseJSONNewStory: " + ex.Message);

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

                    chapterCount = ParsedJSON.Count;

                    ParsedNewChapters.chapter = new JSONChapter[chapterCount];

                    foreach (dynamic chapter in ParsedJSON)
                    {
                        // Add the chapter to the new story
                        ParsedNewChapters.chapter[i] = new JSONChapter();
                        ParsedNewChapters.chapter[i].chapter_name = chapter[i].chapter_name;
                        ParsedNewChapters.chapter[i].chapter_synopsis = chapter[i].chapter_synopsis;

                        if (chapter[i].paragraphs != null)
                        {
                            ParsedNewChapters.chapter[i].paragraphs = new Paragraphs[chapter[i].paragraphs.Count];

                            // See if there is more than one paragraph
                            if (chapter[i].paragraphs.Count > 1)
                            {
                                // Loop through the paragraphs
                                ii = 0;
                                foreach (dynamic paragraph in chapter[i].paragraphs)
                                {
                                    // Add the paragraph to the chapter
                                    ParsedNewChapters.chapter[i].paragraphs[ii] = new Paragraphs();
                                    ParsedNewChapters.chapter[i].paragraphs[ii].contents = paragraph[ii].contents;
                                    ParsedNewChapters.chapter[i].paragraphs[ii].location_name = paragraph[ii].location_name;
                                    ParsedNewChapters.chapter[i].paragraphs[ii].timeline_name = paragraph[ii].timeline_name;

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
                                ParsedNewChapters.chapter[i].paragraphs = new Paragraphs[1];
                                ParsedNewChapters.chapter[i].paragraphs[0] = new Paragraphs();
                                ParsedNewChapters.chapter[i].paragraphs[0].contents = chapter.paragraphs;
                            }
                        }
                        i++;
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
