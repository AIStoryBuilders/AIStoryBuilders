# AIStoryBuilders
### [AIStoryBuilders.com](https://AIStoryBuilders.com)
### A parent project of [AIStoryBuilders](https://github.com/ADefWebserver/AIStoryBuilders)
#
![image](https://github.com/ADefWebserver/AIStoryBuilders/assets/1857799/1e9e7b28-ed00-42da-b038-063d0d0b45d7)

### Data Structure

- **Story**
  - *Title*
  - *Style*
  - *Synopsis*
 
- **Timelines**
  - *Timeline Name*
  - *Description*
  - *StartDate*
  - *EndDate* (nullable)
  
- **Locations**
  - *Location Name*
  - *Description*
  
- **Characters**
  - *Character Name*
  - *Descriptions*
    - *Desciption Type*
    - *Timeline Name*

- **Chapters**
  - *Synopsis*
  - *Paragraphs*   
    - *Content*
    - *Timeline Name*
    - *Location Name*
    - *Characters*
