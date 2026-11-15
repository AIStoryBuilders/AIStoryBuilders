#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIStoryBuildersV2.Shared.Models;

public class SimpleCharacter
{
    public string CharacterName { get; set; }
    public List<SimpleCharacterBackground> CharacterBackground { get; set; }
}

public class SimpleCharacterBackground
{
    public string Type { get; set; }
    public string Description { get; set; }
}
