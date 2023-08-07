SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Chapter]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Chapter](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[StoryId] [int] NOT NULL,
	[ChapterName] [nvarchar](150) NOT NULL,
	[Synopsis] [nvarchar](500) NOT NULL,
	[Sequence] [int] NOT NULL,
 CONSTRAINT [PK_Chapter] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Character]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Character](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[StoryId] [int] NOT NULL,
	[CharacterName] [nvarchar](4000) NOT NULL,
	[Description] [nvarchar](4000) NOT NULL,
	[Goals] [nvarchar](4000) NOT NULL,
 CONSTRAINT [PK_Character] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CharacterBackground]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[CharacterBackground](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CharacterId] [int] NOT NULL,
	[Sequence] [int] NOT NULL,
	[Type] [nvarchar](100) NOT NULL,
	[Description] [nvarchar](4000) NOT NULL,
	[ParagraphContent] [nvarchar](3000) NOT NULL,
	[TimelineId] [int] NULL,
 CONSTRAINT [PK_CharacterBackgroundParagraph] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CharacterBackgroundVectorData]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[CharacterBackgroundVectorData](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CharacterBackgroundId] [int] NOT NULL,
	[vector_value_id] [int] NOT NULL,
	[vector_value] [float] NOT NULL,
 CONSTRAINT [PK_ChracterBackgroundParagraphVectorData] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Location]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Location](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[StoryId] [int] NOT NULL,
	[LocationName] [nvarchar](4000) NOT NULL,
	[Description] [nvarchar](4000) NOT NULL,
 CONSTRAINT [PK_Location] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Paragraph]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Paragraph](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ChapterId] [int] NOT NULL,
	[Sequence] [int] NOT NULL,
	[Type] [nvarchar](100) NOT NULL,
	[Description] [nvarchar](4000) NOT NULL,
	[ParagraphContent] [nvarchar](3000) NOT NULL,
	[TimelineId] [int] NULL,
 CONSTRAINT [PK_Paragraph] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Paragraph_Character]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Paragraph_Character](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ParagraphId] [int] NOT NULL,
	[CharacterId] [int] NOT NULL,
 CONSTRAINT [PK_Paragraph_Character] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Paragraph_Location]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Paragraph_Location](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ParagraphId] [int] NOT NULL,
	[LocationId] [int] NOT NULL,
 CONSTRAINT [PK_Paragraph_Location] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ParagraphVectorData]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ParagraphVectorData](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ParagraphId] [int] NOT NULL,
	[vector_value_id] [int] NOT NULL,
	[vector_value] [float] NOT NULL,
 CONSTRAINT [PK_ParagragraphVectorData] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Story]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Story](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Title] [nvarchar](500) NOT NULL,
	[Style] [nvarchar](500) NOT NULL,
	[Theme] [nvarchar](4000) NOT NULL,
	[Synopsis] [nvarchar](4000) NOT NULL,
 CONSTRAINT [PK_Story] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Timeline]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Timeline](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[StoryId] [int] NOT NULL,
	[TimelineName] [nvarchar](250) NOT NULL,
	[TimelineDescription] [nvarchar](2000) NOT NULL,
	[StartDate] [datetime] NULL,
	[StopDate] [datetime] NULL,
 CONSTRAINT [PK_Timeline] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[CharacterBackgroundVectorData]') AND name = N'NonClusteredColumnStoreIndex')
CREATE NONCLUSTERED COLUMNSTORE INDEX [NonClusteredColumnStoreIndex] ON [dbo].[CharacterBackgroundVectorData]
(
	[CharacterBackgroundId]
)WITH (DROP_EXISTING = OFF, COMPRESSION_DELAY = 0, DATA_COMPRESSION = COLUMNSTORE) ON [PRIMARY]
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ParagraphVectorData]') AND name = N'NonClusteredColumnStoreIndex')
CREATE NONCLUSTERED COLUMNSTORE INDEX [NonClusteredColumnStoreIndex] ON [dbo].[ParagraphVectorData]
(
	[ParagraphId]
)WITH (DROP_EXISTING = OFF, COMPRESSION_DELAY = 0, DATA_COMPRESSION = COLUMNSTORE) ON [PRIMARY]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Chapter_Story]') AND parent_object_id = OBJECT_ID(N'[dbo].[Chapter]'))
ALTER TABLE [dbo].[Chapter]  WITH CHECK ADD  CONSTRAINT [FK_Chapter_Story] FOREIGN KEY([StoryId])
REFERENCES [dbo].[Story] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Chapter_Story]') AND parent_object_id = OBJECT_ID(N'[dbo].[Chapter]'))
ALTER TABLE [dbo].[Chapter] CHECK CONSTRAINT [FK_Chapter_Story]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Character_Story]') AND parent_object_id = OBJECT_ID(N'[dbo].[Character]'))
ALTER TABLE [dbo].[Character]  WITH CHECK ADD  CONSTRAINT [FK_Character_Story] FOREIGN KEY([StoryId])
REFERENCES [dbo].[Story] ([Id])
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Character_Story]') AND parent_object_id = OBJECT_ID(N'[dbo].[Character]'))
ALTER TABLE [dbo].[Character] CHECK CONSTRAINT [FK_Character_Story]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CharacterBackground_Character]') AND parent_object_id = OBJECT_ID(N'[dbo].[CharacterBackground]'))
ALTER TABLE [dbo].[CharacterBackground]  WITH CHECK ADD  CONSTRAINT [FK_CharacterBackground_Character] FOREIGN KEY([CharacterId])
REFERENCES [dbo].[Character] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CharacterBackground_Character]') AND parent_object_id = OBJECT_ID(N'[dbo].[CharacterBackground]'))
ALTER TABLE [dbo].[CharacterBackground] CHECK CONSTRAINT [FK_CharacterBackground_Character]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CharacterBackground_Timeline]') AND parent_object_id = OBJECT_ID(N'[dbo].[CharacterBackground]'))
ALTER TABLE [dbo].[CharacterBackground]  WITH CHECK ADD  CONSTRAINT [FK_CharacterBackground_Timeline] FOREIGN KEY([TimelineId])
REFERENCES [dbo].[Timeline] ([Id])
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CharacterBackground_Timeline]') AND parent_object_id = OBJECT_ID(N'[dbo].[CharacterBackground]'))
ALTER TABLE [dbo].[CharacterBackground] CHECK CONSTRAINT [FK_CharacterBackground_Timeline]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CharacterBackgroundParagraphVectorData_CharacterBackgroundParagraph]') AND parent_object_id = OBJECT_ID(N'[dbo].[CharacterBackgroundVectorData]'))
ALTER TABLE [dbo].[CharacterBackgroundVectorData]  WITH CHECK ADD  CONSTRAINT [FK_CharacterBackgroundParagraphVectorData_CharacterBackgroundParagraph] FOREIGN KEY([CharacterBackgroundId])
REFERENCES [dbo].[CharacterBackground] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_CharacterBackgroundParagraphVectorData_CharacterBackgroundParagraph]') AND parent_object_id = OBJECT_ID(N'[dbo].[CharacterBackgroundVectorData]'))
ALTER TABLE [dbo].[CharacterBackgroundVectorData] CHECK CONSTRAINT [FK_CharacterBackgroundParagraphVectorData_CharacterBackgroundParagraph]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Location_Story]') AND parent_object_id = OBJECT_ID(N'[dbo].[Location]'))
ALTER TABLE [dbo].[Location]  WITH CHECK ADD  CONSTRAINT [FK_Location_Story] FOREIGN KEY([StoryId])
REFERENCES [dbo].[Story] ([Id])
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Location_Story]') AND parent_object_id = OBJECT_ID(N'[dbo].[Location]'))
ALTER TABLE [dbo].[Location] CHECK CONSTRAINT [FK_Location_Story]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Chapter]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph]'))
ALTER TABLE [dbo].[Paragraph]  WITH CHECK ADD  CONSTRAINT [FK_Paragraph_Chapter] FOREIGN KEY([ChapterId])
REFERENCES [dbo].[Chapter] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Chapter]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph]'))
ALTER TABLE [dbo].[Paragraph] CHECK CONSTRAINT [FK_Paragraph_Chapter]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Timeline]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph]'))
ALTER TABLE [dbo].[Paragraph]  WITH CHECK ADD  CONSTRAINT [FK_Paragraph_Timeline] FOREIGN KEY([TimelineId])
REFERENCES [dbo].[Timeline] ([Id])
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Timeline]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph]'))
ALTER TABLE [dbo].[Paragraph] CHECK CONSTRAINT [FK_Paragraph_Timeline]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Character_Character]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph_Character]'))
ALTER TABLE [dbo].[Paragraph_Character]  WITH CHECK ADD  CONSTRAINT [FK_Paragraph_Character_Character] FOREIGN KEY([CharacterId])
REFERENCES [dbo].[Character] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Character_Character]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph_Character]'))
ALTER TABLE [dbo].[Paragraph_Character] CHECK CONSTRAINT [FK_Paragraph_Character_Character]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Character_Paragraph]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph_Character]'))
ALTER TABLE [dbo].[Paragraph_Character]  WITH CHECK ADD  CONSTRAINT [FK_Paragraph_Character_Paragraph] FOREIGN KEY([ParagraphId])
REFERENCES [dbo].[Paragraph] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Character_Paragraph]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph_Character]'))
ALTER TABLE [dbo].[Paragraph_Character] CHECK CONSTRAINT [FK_Paragraph_Character_Paragraph]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Location_Location]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph_Location]'))
ALTER TABLE [dbo].[Paragraph_Location]  WITH CHECK ADD  CONSTRAINT [FK_Paragraph_Location_Location] FOREIGN KEY([LocationId])
REFERENCES [dbo].[Location] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Location_Location]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph_Location]'))
ALTER TABLE [dbo].[Paragraph_Location] CHECK CONSTRAINT [FK_Paragraph_Location_Location]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Location_Paragraph]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph_Location]'))
ALTER TABLE [dbo].[Paragraph_Location]  WITH CHECK ADD  CONSTRAINT [FK_Paragraph_Location_Paragraph] FOREIGN KEY([ParagraphId])
REFERENCES [dbo].[Paragraph] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Paragraph_Location_Paragraph]') AND parent_object_id = OBJECT_ID(N'[dbo].[Paragraph_Location]'))
ALTER TABLE [dbo].[Paragraph_Location] CHECK CONSTRAINT [FK_Paragraph_Location_Paragraph]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_ParagragraphVectorData_Paragraph]') AND parent_object_id = OBJECT_ID(N'[dbo].[ParagraphVectorData]'))
ALTER TABLE [dbo].[ParagraphVectorData]  WITH CHECK ADD  CONSTRAINT [FK_ParagragraphVectorData_Paragraph] FOREIGN KEY([ParagraphId])
REFERENCES [dbo].[Paragraph] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_ParagragraphVectorData_Paragraph]') AND parent_object_id = OBJECT_ID(N'[dbo].[ParagraphVectorData]'))
ALTER TABLE [dbo].[ParagraphVectorData] CHECK CONSTRAINT [FK_ParagragraphVectorData_Paragraph]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Timeline_Story]') AND parent_object_id = OBJECT_ID(N'[dbo].[Timeline]'))
ALTER TABLE [dbo].[Timeline]  WITH CHECK ADD  CONSTRAINT [FK_Timeline_Story] FOREIGN KEY([StoryId])
REFERENCES [dbo].[Story] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_Timeline_Story]') AND parent_object_id = OBJECT_ID(N'[dbo].[Timeline]'))
ALTER TABLE [dbo].[Timeline] CHECK CONSTRAINT [FK_Timeline_Story]
GO