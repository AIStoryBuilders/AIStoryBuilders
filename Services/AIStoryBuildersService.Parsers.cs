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

                int i = 0;
                int ii = 0;
                JSONNewStory ParsedNewStory = new JSONNewStory();

                // Parse the JSON as a dynamic object
                dynamic ParsedJSON = JsonConvert.DeserializeObject(RawJSON);

                foreach (dynamic location in ParsedJSON.locations)
                {
                    // Add the location to the new story
                    ParsedNewStory.locations[i] = new Locations();
                    ParsedNewStory.locations[i].name = location.name;

                    // See if there is more than one description
                    if (location.descriptions.Count > 1)
                    {
                        // Loop through the descriptions
                        ii = 0;
                        foreach (dynamic description in location.descriptions)
                        {
                            // Add the description to the location
                            ParsedNewStory.locations[ii].descriptions[description] = description;
                            ii++;
                        }
                    }
                    else
                    {
                        // Add the description to the location
                        ParsedNewStory.locations[i].descriptions[location.descriptions] = location.descriptions;
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
                            ParsedNewStory.characters[i].descriptions[i] = new Descriptions();
                            ParsedNewStory.characters[i].descriptions[i].description_type = character.descriptions.description_type;
                            ParsedNewStory.characters[i].descriptions[i]._enum = character.descriptions._enum;
                            ParsedNewStory.characters[i].descriptions[i].description = character.descriptions.description;
                            ParsedNewStory.characters[i].descriptions[i].timeline_name = character.descriptions.timeline_name;
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
