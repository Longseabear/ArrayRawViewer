# Structure Template Options workflow

The shell's OK/Apply hook and the page's Save button use the same validated
transaction. Enter commits an edit without intentionally activating the host OK
button; Cancel does not save the draft. Native focus-container protections remain.

Files are separated by responsibility:

- StructureTemplateOptionsPage.cs: Visual Studio page lifetime / Apply.
- StructureTemplateGrid.cs: grid keyboard routing / native focus container.
- StructureTemplateOptionsControl.cs: draft editing, inline feedback, JSON actions.
- StructureTemplateStore.cs: validation, one settings transaction and notification.

Template definitions and search options are written together, preserving other
solutions' records. Atomic replacement retains a .bak of the previous file.
Unchanged Save/OK calls do not rewrite the file or broadcast another update.

Loaded viewers subscribe to successful saves and unsubscribe on unload. Updates
are queued on their UI dispatcher, scoped to the viewer's loaded solution, and
invalidate old search results. They do not evaluate debugger expressions or change
root, source buffer, image cache, or zoom. Other VS processes and external file
edits are not broadcast; use Reload templates there.

Automated checks (isolated temporary settings; no user settings overwritten):

- tests/OptionsPagePreview/run.ps1 -Configuration Release: actual hosted WinForms
  control, SDK OnApply/OnClosed hooks, Enter, invalid/duplicate rows, Cancel/reopen,
  complete save notification, unchanged save, explicitly empty sample templates.
- ViewerWorkspacePreview with --verify against the Release DLL: actual WPF
  viewer receives saved definitions and updated selected member paths without
  Reload, while root/buffer/frame/zoom remain unchanged; other-solution saves
  leave this viewer alone.
- Existing native focus-deactivation, afaf/reopen, Array-tab, Watch, search and
  core decoding regression suites remain enabled.

Manual check after installing the new VSIX:

1. Keep Viewer open in a solution; open Tools > Options > Array RAW Viewer.
2. Add/rename a template; press Enter from its editor and search timeout input.
   Neither should dismiss the page. Save should show inline confirmation.
3. Change its RAW member while the row is still editing; click Options OK.
   Reopen and verify the edit was saved; the Viewer list must already reflect it.
4. Blank a required cell and click OK: stay in Options with an error on that cell.
5. Cancel a timeout edit, reopen and verify the saved timeout is restored.
6. Verify the existing selected root, pointer, zoom and image have not changed.

Standalone tests do not certify every Visual Studio shell's native keyboard
routing. Real VS2015 and installed-VS end-to-end verification remain separate.
