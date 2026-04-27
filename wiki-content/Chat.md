# Chat
* * *

When editing a story, the **Chat** tab provides a conversational AI assistant
that is aware of the entire story you are working on — its **Details**,
**Timelines**, **Locations**, **Characters**, **Chapters**, and the
paragraph **Sections** within them. You can ask questions, brainstorm
plot ideas, request suggestions, and even ask the AI to make changes to your
story directly from the chat.

The first time the **Chat** tab is opened for a story, **AIStoryBuilders**
builds a knowledge graph of the story's contents in the background. After that,
the chat uses the graph to answer questions accurately and quickly without
re-sending the entire story to the AI on every message.

## Opening the Chat

While editing a story, click the **Chat** tab (the seventh tab in the
story-editing dialog, after **Chapters**).

The **Chat** panel has the following features:

- **Story Chat Assistant** title bar - Shows that you are talking to the
assistant for the current story.
- **Model dropdown** - Lets you choose which **AI** model is used to answer
chat messages. The list reflects the models available for the **AI** service
you have configured on the **Settings** page (for example, **OpenAI** or
**Azure OpenAI**). Changing the model here only affects the **Chat** tab.
- **🔄 Clear button** - Clears the current conversation and starts a new
chat session. Your story content is not affected; only the chat history is
cleared.
- **Message area** - Displays the conversation between you and the **AI**.
Your messages appear on the right; the **AI**'s replies appear on the left.
The assistant streams its replies as they are generated, and formats them with
Markdown so headings, bullet lists, and code blocks render properly.
- **Input box** - The text area at the bottom of the panel. Type your
question or instruction here.
- **Send button** - Sends your message to the **AI**. You can also press
**Enter** to send (use **Shift+Enter** to insert a new line).

## Asking Questions about Your Story

The chat is grounded in the actual content of the story you are editing, so
you can ask **AIStoryBuilders** about anything in the **Story Database**.
Possible questions include (but are not limited to):

- "Tell me about the main characters in this story."
- "Are there any orphaned characters that don't appear in any **Section**?"
- "Which **Chapters** take place at the *Lighthouse* **Location**?"
- "Summarise what happens to *Daniel* across the **Timeline**."
- "Find any **Sections** where *Tom* and *Daniel* appear together."
- "What **Locations** appear in **Chapter 3**?"

The assistant will look up the relevant **Characters**, **Locations**,
**Timelines**, **Chapters**, and paragraph **Sections** in the story's
knowledge graph and answer based on what it finds.

## Brainstorming and Suggestions

You can also use the **Chat** tab as a creative collaborator. Useful
prompts include:

- "Suggest three plot twists for the next **Chapter**."
- "What would *Tom* most likely do after the events of **Chapter 5**?"
- "Give me ideas for a new **Location** that would fit this story."
- "Propose a backstory for *Daniel* that is consistent with what is already
written."

Because the assistant has access to your existing story data, its suggestions
stay consistent with the **Characters**, **Locations**, and **Timelines**
you have already defined.

## Making Changes to Your Story

In addition to answering questions, the **Chat** tab can make changes to
the story for you. You can ask it to add, rename, update, or remove
**Characters**, **Locations**, **Timelines**, **Chapters**, and paragraph
**Sections**. Examples include:

- "Add a new **Character** named *Eleanor*, a retired schoolteacher who
appears in **Chapter 2**."
- "Rename the **Location** *The Inn* to *The Black Boar Inn* everywhere it
is used."
- "Add a new **Timeline** called *Flashback - 1985*."
- "Insert a new paragraph **Section** at the start of **Chapter 4** that
describes the storm."
- "Remove the **Character** *Background* entry that says *attended college
in Boston*."

The assistant will describe what it is about to do before applying the change,
and any changes it makes are saved into the story's folder structure, just as
if you had made them through the **Details**, **Timelines**, **Locations**,
**Characters**, **Chapters**, or **Sections** tabs.

## Notes

- The **Chat** tab requires a valid **OpenAI** key or **Azure OpenAI**
configuration on the **Settings** page, the same as the rest of the
application.
- All conversations and tool calls are recorded in the **Logs**. If a chat
response is missing data or behaves unexpectedly, the **Logs** page is the
best place to see what was asked and what the **AI** returned.
- Clearing the conversation (the **🔄** button) does not delete any story
content. It only resets the chat history shown in the **Chat** panel.
