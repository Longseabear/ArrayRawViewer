# Structure Templates native-host regression

Run `./tests/OptionsPagePreview/run.ps1 -Configuration Release` after building
the extension. The harness loads the actual extension control, uses an isolated
solution identity, and never saves templates. It briefly displays a test window.

Checks:

- Add template focuses the new class-name editor.
- Every ancestor of the editing textbox has `WS_EX_CONTROLPARENT`.
- A native dialog containing the real control can deactivate while the editor
  has focus (`WM_ACTIVATE / WA_INACTIVE`).
- `afaf` followed by Enter commits only that cell. Repeated Enter does not
  trigger the host's default button or add rows.
- Closing/reopening the actual DialogPage retains the same control and HWND.
  Three close/activate cycles remain editable. The standalone SDK lifecycle
  test supplies a UI-thread JoinableTaskContext; it does not run a VS instance.

The 0.11.60 follow-up failure was disposal of the cached control in `OnClosed`.
The shell retains its property-page host across Options openings; the control
must live until page disposal. Refresh the solution data on the next activation
instead of disposing/replacing the window. This is independent of the native
focus-traversal flag above.

## Why the native host matters

On 2026-09-14, installed 0.11.59 hung in Visual Studio's native Options host.
The managed stack only showed `ToolsOptionsCommand`; native symbols located it
in `USER32!xxxRemoveDefaultButton`, `xxxSaveDlgFocus`, and `msenv!PropSheetDlgProc`.
The DataGridView's ExStyle was 0, while the editing panel and other ancestors
had `WS_EX_CONTROLPARENT` (0x10000). Native dialog traversal skipped the focused
editor's subtree and did not terminate. Typing an unknown class name was not
performing a debugger search.

The old installed DLL reproduced the hang in this native-host harness; the
fixed DLL completed it and the Enter checks. The earlier WinForms-only host
did not reproduce the failure.

For an intentional old-DLL reproduction, run the built test executable with
the old assembly directory and `--native-only`. This skips the early style
assertion, allowing the native hang to occur. Use a disposable test process
with an external timeout; never point the test at a production process.

Final Visual Studio acceptance test after installing the new VSIX:

1. Options > Array RAW Viewer > Structure Templates > Add template.
2. Type `afaf`, press Enter; edit another cell.
3. While editing, switch to another app and back; repeat.
4. Cancel Options without saving, reopen and verify responsiveness.
