# Import
* * *

The **Home** screen of **AIStoryBuilders** has two import buttons that allow
you to bring an existing story into the application:

- **Import Story** - Imports a previously exported **AIStoryBuilders**
project file (`.stybld`).
- **Import docx/pdf/txt** - Imports a manuscript from a Microsoft Word
document (`.docx`), a PDF file (`.pdf`), a plain-text file (`.txt`), or a
Markdown file (`.md`). The manuscript is parsed by the **AI** into a fully
structured **AIStoryBuilders** story.

This page describes both options.

## Import Story (.stybld)

The **Import Story** button restores a project that was previously saved
with the **Export Project** button.

To import a project:

- On the **Home** screen, click the **Import Story** button.
- Select the `.stybld` file from your computer.
- The story is added to your list of stories on the **Home** screen.
- A knowledge graph is built in the background so the new story works
correctly with the **Chat** tab.

This is the recommended way to move stories between machines, between the
**Online Web Browser Version** and the **Microsoft Windows Desktop Version**,
or between different web browsers.

## Import docx/pdf/txt

The **Import docx/pdf/txt** button takes an existing manuscript and converts
it into a fully structured **AIStoryBuilders** story. After the import
finishes, the story has a populated **Story Database** with **Characters**,
**Locations**, **Timelines**, **Chapters**, and paragraph **Sections**, and
the **Chapters** tab is ready to edit.

Supported file types:

- **`.docx`** - Microsoft Word documents.
- **`.pdf`** - PDF manuscripts.
- **`.txt`** - Plain-text manuscripts.
- **`.md`** - Markdown manuscripts.

To import a manuscript:

- On the **Home** screen, click the **Import docx/pdf/txt** button.
- Select the manuscript file from your computer.
- The application displays a progress bar and a status message describing
the current parsing stage.
- When parsing finishes, the new story is added to your list of stories
and the **Chapters** tab is opened automatically so you can review the
result.

## What the AI Does During the Import

The manuscript is taken through a multi-stage **AI** pipeline. Each stage
updates the progress bar so you can see what is happening.

- **Extract text** - The raw text of the manuscript is read from the file
and cleaned up (Unicode normalization, whitespace tidy-up).
- **Split into Chapters** - The **AI** identifies natural **Chapter**
boundaries in the manuscript and assigns each one a sequence number, a
title, and a synopsis.
- **Split each Chapter into Sections** - Each **Chapter** is broken into
paragraph **Sections**, the same unit the **Sections** tab uses when you
write manually.
- **Extract Characters** - The **AI** scans the manuscript for **Characters**
and creates a **Character** entry for each one, including any backgrounds
that can be inferred from the text.
- **Extract Locations** - The **AI** scans the manuscript for **Locations**
and creates a **Location** entry for each one.
- **Extract Timelines** - The **AI** identifies **Timelines** in the
manuscript and creates a **Timeline** entry for each one.
- **Annotate Sections** - Each paragraph **Section** is associated with
the **Characters**, **Location**, and **Timeline** that apply to it, the
same associations you can edit later from the **Sections** tab.
- **Generate embeddings** - Local embeddings are generated for the
paragraph **Sections** so the **Chat** tab and other **AI** features can
search the story by meaning, not just by keyword.
- **Build knowledge graph** - A knowledge graph of the imported story is
built so the **Chat** tab can answer questions about the new story
immediately.

The amount of time the import takes depends on the length of the manuscript
and the speed of the **AI** model that is configured on the **Settings**
page. Longer manuscripts can take several minutes.

## After the Import

When the import finishes:

- The new story appears in your list of stories on the **Home** screen,
just like a story you created from scratch.
- The story opens in the editor and is ready for you to review.
- All of the usual tabs are populated: **Details**, **Timelines**,
**Locations**, **Characters**, **Chapters**, **Sections**, and **Chat**.
- You can edit anything the **AI** got wrong using the standard tabs. The
**AI** is a good first pass, but human review is recommended, especially for
long manuscripts.

## Notes

- The **Import docx/pdf/txt** feature requires a valid **OpenAI** key or
**Azure OpenAI** configuration on the **Settings** page, because the parsing
pipeline calls the **AI** repeatedly.
- If the import fails partway through, the error is shown as a notification
and recorded in the **Logs**. The **Logs** page is the best place to see
what the **AI** returned at each stage.
- Importing a manuscript does not modify the original file. The manuscript is
read, parsed, and saved as a brand-new **AIStoryBuilders** story.
- For very long manuscripts, consider splitting the text into a few smaller
files and importing them one **Chapter** range at a time, then merging them
later from within **AIStoryBuilders**.
