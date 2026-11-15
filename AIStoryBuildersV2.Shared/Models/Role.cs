#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIStoryBuildersV2.Shared.Models;

public enum Role
{
    System = 1,
    Assistant,
    User,
    Function,
    Tool
}
