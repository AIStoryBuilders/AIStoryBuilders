using AIStoryBuilders.Models.JSON;
using Newtonsoft.Json;

namespace AIStoryBuilders.Services
{
    public partial class AIStoryBuildersService
    {

        #region *** JSONNewStory ***
        public JSONNewStory ParseJSONNewStory(string RawJSON)
        {
            try
            {
                // Convert the JSON to a dynamic object
                //JSONNewStory ParsedNewStory = JsonConvert.DeserializeObject<JSONNewStory>(RawJSON);

                // Parse the JSON as a dynamic object
                dynamic ParsedJSON = JsonConvert.DeserializeObject(RawJSON);

                int i = 0;
                int ii = 0;

                JSONNewStory ParsedNewStory = new JSONNewStory();

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
                LogService.WriteToLog(ex.Message);

                throw;
            }
        }
        #endregion

    }
}
