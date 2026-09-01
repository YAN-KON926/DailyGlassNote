# Release Test Checklist

Use fictional test data. Run functional checks on every supported system and visual checks in a local console session rather than Remote Desktop.

## Startup and window

- [ ] Application starts without installation or an unhandled error.
- [ ] Main note does not appear in the taskbar.
- [ ] Tray icon appears and uses the application icon.
- [ ] Closing the main note hides it; double-clicking the tray icon restores it.
- [ ] Tray Exit terminates all note windows.
- [ ] Window can be moved and resized from its edges and corners.
- [ ] Always-on-top and position lock behave correctly.
- [ ] Saved position and size survive restart.

## Task editing

- [ ] Plus adds exactly one task at the bottom.
- [ ] A new task defaults to red.
- [ ] Enter confirms without creating or focusing another row.
- [ ] The check button confirms the same way as Enter.
- [ ] Long text displays from the beginning after confirmation.
- [ ] Clicking outside editable areas does not activate title or task editing.
- [ ] Sequence numbers update after adding, deleting, or reordering.

## Task actions

- [ ] Right-clicking a row exposes Delete this task.
- [ ] Deleting removes only the selected task.
- [ ] Dragging from the status area changes task order.
- [ ] Dragging from the text editor does not accidentally reorder the task.
- [ ] Notes and deadlines save and reopen correctly.
- [ ] Cancel and Escape discard unsaved note-dialog changes.

## Status behavior

- [ ] Red means unfinished and is the default.
- [ ] Green means must complete today.
- [ ] Blue means completed.
- [ ] Only the selected ball is highlighted.
- [ ] Blue dims task text, sequence number, and note icon.
- [ ] Blue draws a continuous strike-through across the task content.
- [ ] Completed tasks do not appear the next day.
- [ ] Unfinished green tasks carry forward and become red.
- [ ] Remaining unfinished tasks preserve their order during rollover.

## Text and multiple notes

- [ ] Text mode provides a separate title and multiline content field.
- [ ] Title editing starts only when the title is clicked.
- [ ] Content editing starts only when the content area is clicked.
- [ ] Non-editable areas can drag the window.
- [ ] Additional task and text notes can be created.
- [ ] Open secondary notes return after normal restart.
- [ ] Explicitly closed secondary notes do not return.

## Appearance

- [ ] Six text colors preview and apply immediately.
- [ ] Glass transparency previews continuously from 10% to 100%.
- [ ] Background remains visibly blurred and interactive.
- [ ] No black surface appears behind the content.
- [ ] No blur exists outside rounded corners.
- [ ] No rectangular border appears when the note loses focus.
- [ ] Status balls, separators, and note icons remain aligned at supported sizes.
- [ ] Scrollbar hides after inactivity and leaves no visible reserved column.

## Storage and privacy

- [ ] Data is stored only under `%AppData%\daily-sticky`.
- [ ] A 30-day history is retained.
- [ ] Repository package contains no user JSON, backup, cache, or real task content.

