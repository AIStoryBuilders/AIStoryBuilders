﻿// <auto-generated> This file has been auto generated by EF Core Power Tools. </auto-generated>
#nullable disable
using System;
using System.Collections.Generic;

namespace AIStoryBuilders.Models;

public partial class Story
{
    public int Id { get; set; }

    public string Title { get; set; }

    public string Style { get; set; }

    public string Theme { get; set; }

    public string Synopsis { get; set; }

    public virtual ICollection<Chapter> Chapter { get; set; } = new List<Chapter>();
}