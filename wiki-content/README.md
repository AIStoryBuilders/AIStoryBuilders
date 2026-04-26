# Wiki Content

This directory contains the content for the [AIStoryBuilders Wiki](https://github.com/AIStoryBuilders/AIStoryBuilders/wiki).

## Purpose

The files in this directory are synchronized to the GitHub wiki through the `update-wiki.yml` GitHub Action workflow. This allows wiki content to be version-controlled alongside the main codebase.

## Automated Sync

Changes to files in this directory will automatically be pushed to the wiki when:
1. Changes are merged to the `main` branch
2. The `Update Wiki` workflow is manually triggered

## Editing Wiki Pages

To edit wiki pages:
1. Edit the corresponding `.md` file in this directory
2. Commit and push your changes
3. The GitHub Action will automatically sync the changes to the wiki

## Manual Sync

To manually sync the wiki content:
1. Go to the **Actions** tab
2. Select the **Update Wiki** workflow
3. Click **Run workflow**
