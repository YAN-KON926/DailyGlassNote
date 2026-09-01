# DailyGlassNote Product Specification

This document is the durable functional specification for DailyGlassNote. A port or refactor must preserve these behaviors unless a later confirmed requirement explicitly changes them.

## Product scope

DailyGlassNote is a lightweight, portable desktop sticky-note application. The current stable release targets 64-bit Windows 10 and provides two note modes: daily tasks and free-form text.

## Window behavior

- The main note is a borderless, resizable, draggable desktop window.
- The window uses a translucent white frosted-glass surface with rounded corners.
- The application does not appear in the Windows taskbar; it remains available in the notification area.
- Closing the main window hides it to the tray. Double-clicking the tray icon shows it again.
- The tray menu provides Show and Exit actions.
- The application can remain always on top and can lock its position.
- Window position, size, appearance, and content are saved automatically.
- The title is edited only when the title itself is clicked. Clicking a non-editable area must not activate title editing.
- The area outside text inputs can be used to drag the note when position locking is disabled.

## Daily task mode

- The header shows the selected date and completed-task count.
- Previous and next buttons move one day at a time. Clicking the date returns to today.
- Up to 30 days of task data are retained.
- The visible list is scrollable when tasks exceed the available height.
- A centered plus button below the final task appends a new task.
- Each task row contains an automatic sequence number, editable task text, a note button, and three status balls.
- Enter or the check button confirms task editing. Confirmation must not create or focus the next row.
- After confirmation, long task text is displayed from its beginning rather than its final characters.
- Right-clicking a task row opens the Delete this task action.
- Dragging from the right-side status area reorders tasks. Sequence numbers update automatically.

## Task states

- Red (`todo`): unfinished. This is the default state.
- Green (`today`): must be completed today.
- Blue (`done`): completed.
- Only the selected state ball is highlighted; the other two remain muted.
- A completed task uses dimmed text, a full-width strike-through line, and a dimmed note button.
- Completed tasks do not carry into the next day.
- A green task that remains incomplete at day rollover carries into the next day and becomes red.
- Other unfinished tasks carry forward in their existing order.

## Notes and deadlines

- The note button opens a rounded dialog using the same visual language.
- Each task can store detailed notes and a time requirement.
- Save commits changes; Cancel or Escape discards the current edit.

## Text note mode

- Text mode contains a separately editable title and a multiline content area.
- The title uses a larger semibold type treatment.
- The title edit indicator appears only while the title is being edited.
- The content area enters editing only when it is clicked directly.
- Clicking outside the title and content inputs must not activate either editor.
- Content is saved automatically.

## Multiple notes

- Users can create additional task notes or text notes from Settings.
- Each note stores its own content, mode, position, size, and appearance.
- Secondary notes that remain open when the application exits are restored on the next launch.
- Secondary notes explicitly closed by the user are not restored.
- Historical secondary-note data files may remain as backups but must not force closed windows to reopen.

## Appearance

- Text color can be selected from six grayscale levels, black through white.
- Glass transparency can be adjusted from 10% to 100% with live preview.
- The outer frame uses large rounded corners and a restrained light/dark edge treatment.
- The window must not show blur outside its rounded corners.
- The inactive window must not show a rectangular black or white system border.
- Task rows use clear separators without individual 3D card effects.
- Status transitions use subtle visual animation.

## Storage and privacy

- Data is stored locally under `%AppData%\daily-sticky`.
- User task data, notes, text content, caches, and local JSON files must never be committed to the repository.
- The repository and documentation may use only fictional task examples.

## Current platform status

- Stable and visually approved: 64-bit Windows 10.
- Windows 11 is not currently supported. The Win10 composition implementation may render as a black surface on Windows 11.

